using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Onboarding-workbook surface for the Data workspace. This partial adds a genuine multi-sheet
/// <c>.xlsx</c> download (a pure projection of <see cref="BuildDataUploadTemplateCatalog"/>) and a
/// workbook-scoped upload preview with per-cell and cross-sheet validation. The single-file CSV
/// preview path in the sibling partial is left untouched.
/// </summary>
public static partial class WorkstationEndpoints
{
    private const long DataUploadWorkbookMaxFileBytes = 15 * 1024 * 1024;

    // Defenses against decompression bombs: the multipart check only bounds the compressed .xlsx,
    // so cap the decompressed size of each part, the worksheet width, and the number of rows and
    // cells materialized per sheet. A lone far-right cell (e.g. XFD1) would otherwise force
    // gap-filling of thousands of empty cells per row, and many such rows can still exhaust memory
    // while staying under the byte cap.
    private const long MaxWorkbookPartUncompressedBytes = 64 * 1024 * 1024;
    private const int MaxWorkbookColumns = 256;
    private const int MaxWorkbookRowsPerSheet = 200_000;
    private const int MaxWorkbookCellsPerSheet = 1_000_000;

    private const string OnboardingWorkbookSchemaVersion = "1";
    private const string OnboardingWorkbookFileName = "meridian-onboarding-workbook.xlsx";
    private const string OnboardingWorkbookInstructionsSheet = "Instructions";
    private const string OnboardingWorkbookFieldReferenceSheet = "Field reference";
    private const string OnboardingWorkbookMetaSheet = "_meta";

    private static readonly XNamespace WorkbookSpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace WorkbookPackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly IReadOnlyList<string> OnboardingWorkbookAcceptedExtensions = [".xlsx"];

    // Stable, unique, <=31-char worksheet names so XlsxWorkbookWriter never renames a data tab and
    // the _meta sheet name -> template id mapping stays exact on the parse side.
    private static readonly IReadOnlyDictionary<string, string> OnboardingWorkbookSheetNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["trade-data"] = "Trades",
            ["transaction-data"] = "Transactions",
            ["bank-statement"] = "Bank Statement",
            ["servicer-position-statement"] = "Servicer Positions",
            ["servicer-remittance-statement"] = "Servicer Remittance",
            ["asset-information"] = "Securities",
            ["entity-configuration"] = "Entities",
        };

    private static void MapDataUploadWorkbookEndpoints(RouteGroupBuilder group, System.Text.Json.JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationDataUploadWorkbook), (HttpRequest request) =>
        {
            var catalog = BuildDataUploadTemplateCatalog();
            var requested = ParseWorkbookTemplateIdFilter(request.Query["templateIds"].ToString());
            var worksheets = BuildOnboardingWorkbookWorksheets(catalog, requested);
            if (worksheets.Count == 0)
            {
                return MissingDataUploadPayload(
                    "templateIds",
                    "None of the requested template ids match a data upload template.");
            }

            var bytes = XlsxWorkbookWriter.CreateWorkbook(worksheets, request.HttpContext.RequestAborted);
            return Results.File(bytes, WorkstationStructuredXlsxContentType, OnboardingWorkbookFileName);
        })
        .WithName("GetWorkstationOnboardingWorkbook")
        .Produces(StatusCodes.Status200OK, contentType: WorkstationStructuredXlsxContentType)
        .ProducesValidationProblem();

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationDataUploadWorkbookPreview), async (
            HttpContext context,
            HttpRequest request) =>
        {
            if (!HasOperationsContinuityMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!request.HasFormContentType)
            {
                return MissingDataUploadPayload("contentType", "Workbook preview requires multipart/form-data.");
            }

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return MissingDataUploadPayload("file", "Choose a non-empty .xlsx workbook before previewing.");
            }

            if (file.Length > DataUploadWorkbookMaxFileBytes)
            {
                return MissingDataUploadPayload(
                    "file",
                    $"Workbook preview accepts files up to {FormatBytes(DataUploadWorkbookMaxFileBytes)}.");
            }

            if (!HasAcceptedDataUploadExtension(file.FileName, OnboardingWorkbookAcceptedExtensions))
            {
                return MissingDataUploadPayload("file", "Workbook preview accepts .xlsx files.");
            }

            byte[] fileBytes;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
                fileBytes = buffer.ToArray();
            }

            IReadOnlyList<WorkbookSheetContent> sheets;
            try
            {
                sheets = ReadWorkbookSheets(fileBytes);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
            {
                return MissingDataUploadPayload("file", "The uploaded file is not a readable .xlsx workbook.");
            }

            var uploadId = BuildDataUploadId(fileBytes);
            var safeFileName = SanitizeDataUploadWorkbookFileName(file.FileName);
            var relativePath = Path.Combine("workstation", "data-uploads", uploadId, safeFileName)
                .Replace(Path.DirectorySeparatorChar, '/');
            var retainedRoot = ResolveDataUploadRoot();
            var retainedDirectory = Path.Combine(retainedRoot, uploadId);
            Directory.CreateDirectory(retainedDirectory);
            await File
                .WriteAllBytesAsync(Path.Combine(retainedDirectory, safeFileName), fileBytes, context.RequestAborted)
                .ConfigureAwait(false);

            var catalog = BuildDataUploadTemplateCatalog();
            var preview = BuildWorkbookPreview(
                sheets,
                catalog,
                catalog.MaxPreviewRows,
                uploadId,
                safeFileName,
                file.Length,
                string.IsNullOrWhiteSpace(file.ContentType) ? WorkstationStructuredXlsxContentType : file.ContentType,
                currentUser,
                relativePath);

            return Results.Json(preview, jsonOptions);
        })
        .WithName("PreviewWorkstationOnboardingWorkbook")
        .Produces<DataUploadWorkbookPreviewResultDto>(200)
        .ProducesValidationProblem()
        .Produces(403);
    }

    private static IReadOnlyList<string> ParseWorkbookTemplateIdFilter(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<XlsxWorksheet> BuildOnboardingWorkbookWorksheets(
        DataUploadTemplateCatalogDto catalog,
        IReadOnlyList<string> requestedTemplateIds)
    {
        var selected = ResolveWorkbookTemplates(catalog, requestedTemplateIds);
        if (selected.Count == 0)
        {
            return [];
        }

        var worksheets = new List<XlsxWorksheet>
        {
            BuildWorkbookInstructionsSheet(selected),
        };

        foreach (var template in selected)
        {
            worksheets.Add(BuildWorkbookDataSheet(template));
        }

        worksheets.Add(BuildWorkbookFieldReferenceSheet(selected));
        worksheets.Add(BuildWorkbookMetaSheet(selected));
        return worksheets;
    }

    private static IReadOnlyList<DataUploadTemplateDto> ResolveWorkbookTemplates(
        DataUploadTemplateCatalogDto catalog,
        IReadOnlyList<string> requestedTemplateIds)
    {
        if (requestedTemplateIds.Count == 0)
        {
            return catalog.Templates;
        }

        var requested = new HashSet<string>(requestedTemplateIds, StringComparer.OrdinalIgnoreCase);
        return catalog.Templates
            .Where(template => requested.Contains(template.TemplateId))
            .ToArray();
    }

    private static XlsxWorksheet BuildWorkbookInstructionsSheet(IReadOnlyList<DataUploadTemplateDto> templates)
    {
        IReadOnlyList<string> headers =
            ["Sheet", "Domain", "What to enter", "Required columns", "Target workflow"];
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var template in templates)
        {
            var required = template.Fields
                .Where(field => field.Required)
                .Select(field => field.Name);
            rows.Add(new object?[]
            {
                ResolveWorkbookSheetName(template),
                template.DataDomain,
                template.Description,
                string.Join(", ", required),
                template.TargetWorkflow,
            });
        }

        return new XlsxWorksheet(OnboardingWorkbookInstructionsSheet, headers, rows);
    }

    private static XlsxWorksheet BuildWorkbookDataSheet(DataUploadTemplateDto template)
    {
        var headers = ResolveWorkbookTemplateHeaders(template);
        // Data tabs ship header-only so the workbook round-trips cleanly; per-field guidance and
        // examples live on the Field reference sheet instead of greyed example rows the writer
        // cannot style.
        return new XlsxWorksheet(ResolveWorkbookSheetName(template), headers, []);
    }

    private static XlsxWorksheet BuildWorkbookFieldReferenceSheet(IReadOnlyList<DataUploadTemplateDto> templates)
    {
        IReadOnlyList<string> headers =
            ["Sheet", "Field", "Label", "Required", "Example", "Description"];
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var template in templates)
        {
            var sheetName = ResolveWorkbookSheetName(template);
            foreach (var field in template.Fields)
            {
                rows.Add(new object?[]
                {
                    sheetName,
                    field.Name,
                    field.Label,
                    field.Required ? "Required" : "Optional",
                    field.Example,
                    field.Description,
                });
            }
        }

        return new XlsxWorksheet(OnboardingWorkbookFieldReferenceSheet, headers, rows);
    }

    private static XlsxWorksheet BuildWorkbookMetaSheet(IReadOnlyList<DataUploadTemplateDto> templates)
    {
        IReadOnlyList<string> headers = ["sheetName", "templateId", "schemaVersion"];
        var rows = templates
            .Select(template => (IReadOnlyList<object?>)new object?[]
            {
                ResolveWorkbookSheetName(template),
                template.TemplateId,
                OnboardingWorkbookSchemaVersion,
            })
            .ToList();
        return new XlsxWorksheet(OnboardingWorkbookMetaSheet, headers, rows);
    }

    private static IReadOnlyList<string> ResolveWorkbookTemplateHeaders(DataUploadTemplateDto template)
    {
        if (!string.IsNullOrWhiteSpace(template.HeaderLine))
        {
            return template.HeaderLine
                .Split(',', StringSplitOptions.TrimEntries)
                .Where(header => header.Length > 0)
                .ToArray();
        }

        return template.Fields.Select(field => field.Name).ToArray();
    }

    private static string ResolveWorkbookSheetName(DataUploadTemplateDto template)
        => OnboardingWorkbookSheetNames.TryGetValue(template.TemplateId, out var sheetName)
            ? sheetName
            : template.Label;

    private static DataUploadWorkbookPreviewResultDto BuildWorkbookPreview(
        IReadOnlyList<WorkbookSheetContent> sheets,
        DataUploadTemplateCatalogDto catalog,
        int maxPreviewRows,
        string uploadId,
        string fileName,
        long fileSizeBytes,
        string contentType,
        string uploadedBy,
        string retainedPath)
    {
        var sheetTemplateMap = ResolveWorkbookSheetTemplateMap(sheets, catalog);
        var sheetPreviews = new List<DataUploadWorkbookSheetPreviewDto>();
        var totalParsedRows = 0;

        foreach (var sheet in sheets)
        {
            if (IsReservedWorkbookSheet(sheet.Name))
            {
                continue;
            }

            sheetTemplateMap.TryGetValue(sheet.Name, out var template);
            var sheetPreview = BuildWorkbookSheetPreview(sheet, template, maxPreviewRows);
            totalParsedRows += sheetPreview.ParsedRowCount;
            sheetPreviews.Add(sheetPreview);
        }

        var crossSheetIssues = BuildWorkbookCrossSheetIssues(sheets, sheetTemplateMap);

        var hasError = sheetPreviews.Any(sheet => HasWorkbookError(sheet.Issues))
            || HasWorkbookError(crossSheetIssues)
            || sheetPreviews.Count == 0;
        var status = hasError ? "NeedsSchemaRepair" : "ReadyForReview";
        var nextAction = sheetPreviews.Count == 0
            ? "No data sheets were recognized. Download the onboarding workbook and fill in its data tabs before uploading."
            : status == "ReadyForReview"
                ? "Review each sheet's rows, then route the retained workbook into validation and reconciliation."
                : "Fix the flagged cells in Excel and re-upload the corrected workbook.";

        return new DataUploadWorkbookPreviewResultDto(
            UploadId: uploadId,
            FileName: fileName,
            FileSizeBytes: fileSizeBytes,
            ContentType: contentType,
            UploadedBy: uploadedBy,
            UploadedAtUtc: DateTimeOffset.UtcNow,
            RetainedPath: retainedPath,
            SheetCount: sheetPreviews.Count,
            TotalParsedRowCount: totalParsedRows,
            Sheets: sheetPreviews,
            CrossSheetIssues: crossSheetIssues,
            Status: status,
            NextAction: nextAction);
    }

    private static DataUploadWorkbookSheetPreviewDto BuildWorkbookSheetPreview(
        WorkbookSheetContent sheet,
        DataUploadTemplateDto? template,
        int maxPreviewRows)
    {
        var issues = new List<DataUploadValidationIssueDto>();
        var headerRow = sheet.Rows.FirstOrDefault(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)));
        if (headerRow is null)
        {
            return new DataUploadWorkbookSheetPreviewDto(
                sheet.Name,
                template?.TemplateId,
                template?.Label,
                template?.DataDomain,
                ParsedRowCount: 0,
                PreviewRowCount: 0,
                Headers: [],
                PreviewRows: [],
                Issues: [],
                Status: "Empty");
        }

        var headerRowNumber = sheet.Rows.ToList().IndexOf(headerRow) + 1;
        var headers = headerRow.Select(header => header.Trim()).ToArray();
        var namedHeaders = headers.Where(header => header.Length > 0).ToArray();

        if (sheet.Truncated)
        {
            // Blocking, not a warning: rows past the cap were never read, so the sheet cannot be
            // certified review-ready or committable with unvalidated rows behind it.
            issues.Add(new DataUploadValidationIssueDto(
                "Error",
                "rows",
                $"Sheet '{sheet.Name}' is too large to validate in full (over {MaxWorkbookRowsPerSheet} rows or {MaxWorkbookCellsPerSheet} cells); rows beyond the cap were not read. Split it into smaller uploads and re-upload.",
                RowNumber: null,
                SheetName: sheet.Name,
                CellReference: null));
        }

        if (template is null)
        {
            // A non-reserved sheet that resolves to no template must block: without a template the
            // required-field checks below are skipped, so an arbitrary tab would otherwise be
            // reported ReadyForReview and treated as committable.
            issues.Add(new DataUploadValidationIssueDto(
                "Error",
                "sheet",
                $"Sheet '{sheet.Name}' does not map to a supported upload template. Rename it to a workbook data tab or include a _meta mapping before uploading.",
                RowNumber: headerRowNumber,
                SheetName: sheet.Name,
                CellReference: $"{sheet.Name}!{headerRowNumber}"));
        }

        if (template is not null)
        {
            var headerSet = new HashSet<string>(namedHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (var field in template.Fields.Where(field => field.Required))
            {
                if (!headerSet.Contains(field.Name))
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Error",
                        field.Name,
                        $"Required field '{field.Name}' is missing from the '{sheet.Name}' sheet header.",
                        RowNumber: headerRowNumber,
                        SheetName: sheet.Name,
                        CellReference: $"{sheet.Name}!{headerRowNumber}"));
                }
            }
        }

        var requiredFieldNames = template is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                template.Fields.Where(field => field.Required).Select(field => field.Name),
                StringComparer.OrdinalIgnoreCase);
        var currencyColumns = headers
            .Select((header, index) => (header, index))
            .Where(entry => string.Equals(entry.header, "currency", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.index)
            .ToArray();

        var previewRows = new List<IReadOnlyDictionary<string, string>>();
        var parsedRowCount = 0;
        var dataRows = sheet.Rows.Skip(headerRowNumber).ToArray();
        for (var dataIndex = 0; dataIndex < dataRows.Length; dataIndex++)
        {
            var row = dataRows[dataIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            parsedRowCount++;
            var displayRowNumber = headerRowNumber + parsedRowCount;

            if (row.Count != headers.Length)
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Warning",
                    "row",
                    $"Row has {row.Count.ToString(CultureInfo.InvariantCulture)} values for {headers.Length.ToString(CultureInfo.InvariantCulture)} headers.",
                    RowNumber: displayRowNumber,
                    SheetName: sheet.Name,
                    CellReference: $"{sheet.Name}!{displayRowNumber}"));
            }

            for (var column = 0; column < headers.Length; column++)
            {
                var header = headers[column];
                if (header.Length == 0)
                {
                    continue;
                }

                var value = column < row.Count ? row[column].Trim() : string.Empty;
                if (value.Length == 0 && requiredFieldNames.Contains(header))
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Error",
                        header,
                        $"Required value '{header}' is missing.",
                        RowNumber: displayRowNumber,
                        SheetName: sheet.Name,
                        CellReference: $"{sheet.Name}!{WorkbookColumnLetter(column)}{displayRowNumber}"));
                }

                if (currencyColumns.Contains(column) && value.Length > 0 && !IsLikelyCurrencyCode(value))
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Warning",
                        header,
                        $"Currency '{value}' is not a 3-letter ISO code.",
                        RowNumber: displayRowNumber,
                        SheetName: sheet.Name,
                        CellReference: $"{sheet.Name}!{WorkbookColumnLetter(column)}{displayRowNumber}"));
                }
            }

            if (previewRows.Count >= maxPreviewRows)
            {
                continue;
            }

            var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < headers.Length; column++)
            {
                if (headers[column].Length == 0)
                {
                    continue;
                }

                mapped[headers[column]] = column < row.Count ? row[column].Trim() : string.Empty;
            }

            previewRows.Add(mapped);
        }

        var status = HasWorkbookError(issues)
            ? "NeedsRepair"
            : parsedRowCount == 0
                ? "Empty"
                : "ReadyForReview";

        return new DataUploadWorkbookSheetPreviewDto(
            sheet.Name,
            template?.TemplateId,
            template?.Label,
            template?.DataDomain,
            parsedRowCount,
            previewRows.Count,
            namedHeaders,
            previewRows,
            issues,
            status);
    }

    private static IReadOnlyList<DataUploadValidationIssueDto> BuildWorkbookCrossSheetIssues(
        IReadOnlyList<WorkbookSheetContent> sheets,
        IReadOnlyDictionary<string, DataUploadTemplateDto> sheetTemplateMap)
    {
        var entitySheet = sheets.FirstOrDefault(sheet =>
            sheetTemplateMap.TryGetValue(sheet.Name, out var template)
            && string.Equals(template.TemplateId, "entity-configuration", StringComparison.OrdinalIgnoreCase));
        if (entitySheet is null)
        {
            return [];
        }

        var columns = ResolveWorkbookColumnIndexes(entitySheet, "entity_id", "parent_entity_id");
        if (columns.TryGetValue("entity_id", out var entityIdColumn) is false)
        {
            return [];
        }

        columns.TryGetValue("parent_entity_id", out var parentColumn);
        var hasParentColumn = columns.ContainsKey("parent_entity_id");

        var headerRowNumber = ResolveWorkbookHeaderRowNumber(entitySheet);
        var dataRows = entitySheet.Rows.Skip(headerRowNumber).Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToArray();
        var entityIds = new HashSet<string>(
            dataRows
                .Select(row => entityIdColumn < row.Count ? row[entityIdColumn].Trim() : string.Empty)
                .Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (!hasParentColumn)
        {
            return [];
        }

        var issues = new List<DataUploadValidationIssueDto>();
        for (var index = 0; index < dataRows.Length; index++)
        {
            var row = dataRows[index];
            var parent = parentColumn < row.Count ? row[parentColumn].Trim() : string.Empty;
            if (parent.Length == 0)
            {
                continue;
            }

            var displayRowNumber = headerRowNumber + index + 1;
            var cellReference = $"{entitySheet.Name}!{WorkbookColumnLetter(parentColumn)}{displayRowNumber}";
            var entityId = entityIdColumn < row.Count ? row[entityIdColumn].Trim() : string.Empty;
            if (string.Equals(parent, entityId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "parent_entity_id",
                    $"parent_entity_id '{parent}' refers to its own entity_id (self-referencing hierarchy).",
                    RowNumber: displayRowNumber,
                    SheetName: entitySheet.Name,
                    CellReference: cellReference));
                continue;
            }

            if (!entityIds.Contains(parent))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "parent_entity_id",
                    $"parent_entity_id '{parent}' does not resolve to an entity_id in the '{entitySheet.Name}' sheet.",
                    RowNumber: displayRowNumber,
                    SheetName: entitySheet.Name,
                    CellReference: cellReference));
            }
        }

        return issues;
    }

    private static IReadOnlyDictionary<string, DataUploadTemplateDto> ResolveWorkbookSheetTemplateMap(
        IReadOnlyList<WorkbookSheetContent> sheets,
        DataUploadTemplateCatalogDto catalog)
    {
        var map = new Dictionary<string, DataUploadTemplateDto>(StringComparer.OrdinalIgnoreCase);
        var templatesById = catalog.Templates.ToDictionary(
            template => template.TemplateId,
            StringComparer.OrdinalIgnoreCase);

        var metaSheet = sheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, OnboardingWorkbookMetaSheet, StringComparison.OrdinalIgnoreCase));
        if (metaSheet is not null)
        {
            foreach (var (sheetName, templateId) in ReadWorkbookMeta(metaSheet))
            {
                if (templatesById.TryGetValue(templateId, out var template))
                {
                    map[sheetName] = template;
                }
            }
        }

        var sheetNameToTemplate = catalog.Templates.ToDictionary(
            ResolveWorkbookSheetName,
            template => template,
            StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            if (map.ContainsKey(sheet.Name) || IsReservedWorkbookSheet(sheet.Name))
            {
                continue;
            }

            if (sheetNameToTemplate.TryGetValue(sheet.Name, out var byName))
            {
                map[sheet.Name] = byName;
            }
        }

        return map;
    }

    private static IEnumerable<(string SheetName, string TemplateId)> ReadWorkbookMeta(WorkbookSheetContent metaSheet)
    {
        var rows = metaSheet.Rows;
        if (rows.Count == 0)
        {
            yield break;
        }

        var header = rows[0].Select(cell => cell.Trim()).ToArray();
        var sheetNameColumn = Array.FindIndex(header, cell => string.Equals(cell, "sheetName", StringComparison.OrdinalIgnoreCase));
        var templateIdColumn = Array.FindIndex(header, cell => string.Equals(cell, "templateId", StringComparison.OrdinalIgnoreCase));
        if (sheetNameColumn < 0 || templateIdColumn < 0)
        {
            yield break;
        }

        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            var sheetName = sheetNameColumn < row.Count ? row[sheetNameColumn].Trim() : string.Empty;
            var templateId = templateIdColumn < row.Count ? row[templateIdColumn].Trim() : string.Empty;
            if (sheetName.Length > 0 && templateId.Length > 0)
            {
                yield return (sheetName, templateId);
            }
        }
    }

    private static Dictionary<string, int> ResolveWorkbookColumnIndexes(WorkbookSheetContent sheet, params string[] names)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = sheet.Rows.FirstOrDefault(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)));
        if (headerRow is null)
        {
            return result;
        }

        for (var column = 0; column < headerRow.Count; column++)
        {
            var header = headerRow[column].Trim();
            if (names.Any(name => string.Equals(name, header, StringComparison.OrdinalIgnoreCase)))
            {
                result[header] = column;
            }
        }

        return result;
    }

    private static int ResolveWorkbookHeaderRowNumber(WorkbookSheetContent sheet)
    {
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            if (sheet.Rows[index].Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static bool IsReservedWorkbookSheet(string sheetName)
        => string.Equals(sheetName, OnboardingWorkbookMetaSheet, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sheetName, OnboardingWorkbookInstructionsSheet, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sheetName, OnboardingWorkbookFieldReferenceSheet, StringComparison.OrdinalIgnoreCase);

    private static bool HasWorkbookError(IReadOnlyList<DataUploadValidationIssueDto> issues)
        => issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));

    private static bool IsLikelyCurrencyCode(string value)
        => value.Length == 3 && value.All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static string WorkbookColumnLetter(int zeroBasedColumn)
    {
        var column = zeroBasedColumn;
        var builder = new System.Text.StringBuilder();
        while (column >= 0)
        {
            builder.Insert(0, (char)('A' + (column % 26)));
            column = column / 26 - 1;
        }

        return builder.ToString();
    }

    private static string SanitizeDataUploadWorkbookFileName(string fileName)
    {
        var safeName = SanitizeDataUploadFileName(fileName);
        return safeName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? safeName
            : "workbook.xlsx";
    }

    private static IReadOnlyList<WorkbookSheetContent> ReadWorkbookSheets(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var sharedStrings = ReadWorkbookSharedStrings(archive);
        var sheets = new List<WorkbookSheetContent>();
        foreach (var (name, entryPath) in ResolveWorkbookSheetOrder(archive))
        {
            var entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                continue;
            }

            var document = LoadWorkbookXml(entry);
            var rows = new List<IReadOnlyList<string>>();
            var truncated = false;
            var cellCount = 0;
            foreach (var rowElement in document
                .Descendants(WorkbookSpreadsheetNamespace + "sheetData")
                .Elements(WorkbookSpreadsheetNamespace + "row"))
            {
                if (rows.Count >= MaxWorkbookRowsPerSheet || cellCount >= MaxWorkbookCellsPerSheet)
                {
                    truncated = true;
                    break;
                }

                var values = ReadWorkbookRowValues(rowElement, sharedStrings).ToArray();
                cellCount += values.Length;
                rows.Add(values);
            }

            sheets.Add(new WorkbookSheetContent(name, rows, truncated));
        }

        return sheets;
    }

    /// <summary>
    /// Loads a workbook part into memory while enforcing a hard decompressed-byte ceiling, so a
    /// small compressed <c>.xlsx</c> cannot expand a single part into hundreds of megabytes. The
    /// declared entry length is only a hint; the copy aborts on the actual byte count.
    /// </summary>
    private static XDocument LoadWorkbookXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxWorkbookPartUncompressedBytes)
        {
            throw new InvalidDataException(
                $"Workbook part '{entry.FullName}' declares {entry.Length} decompressed bytes, over the limit.");
        }

        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > MaxWorkbookPartUncompressedBytes)
            {
                throw new InvalidDataException(
                    $"Workbook part '{entry.FullName}' exceeds the {MaxWorkbookPartUncompressedBytes} byte decompression limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return XDocument.Load(buffer);
    }

    private static IEnumerable<(string Name, string EntryPath)> ResolveWorkbookSheetOrder(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
        {
            var fallback = archive.GetEntry("xl/worksheets/sheet1.xml");
            if (fallback is not null)
            {
                yield return ("Sheet1", "xl/worksheets/sheet1.xml");
            }

            yield break;
        }

        var rels = LoadWorkbookXml(relsEntry);
        var relationshipTargets = rels
            .Descendants(WorkbookPackageRelationshipsNamespace + "Relationship")
            .Where(relationship => relationship.Attribute("Id")?.Value is { Length: > 0 })
            .ToDictionary(
                relationship => relationship.Attribute("Id")!.Value,
                relationship => relationship.Attribute("Target")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        var workbook = LoadWorkbookXml(workbookEntry);

        foreach (var sheetElement in workbook.Descendants(WorkbookSpreadsheetNamespace + "sheet"))
        {
            var name = sheetElement.Attribute("name")?.Value ?? string.Empty;
            var relationshipId = sheetElement.Attribute(WorkbookRelationshipsNamespace + "id")?.Value;
            if (name.Length == 0 || string.IsNullOrEmpty(relationshipId)
                || !relationshipTargets.TryGetValue(relationshipId, out var target)
                || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var normalized = NormalizeWorkbookSheetTarget(target);
            if (normalized is not null)
            {
                yield return (name, normalized);
            }
        }
    }

    private static string? NormalizeWorkbookSheetTarget(string target)
    {
        var trimmed = target.Trim();
        if (trimmed.Length == 0
            || trimmed.Contains('\\', StringComparison.Ordinal)
            || Uri.TryCreate(trimmed, UriKind.Absolute, out _)
            || (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'))
        {
            return null;
        }

        var relative = trimmed.StartsWith('/')
            ? trimmed.TrimStart('/')
            : trimmed.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"xl/{trimmed}";

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return string.Join('/', segments);
    }

    private static string[] ReadWorkbookSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        var document = LoadWorkbookXml(entry);
        return document
            .Descendants(WorkbookSpreadsheetNamespace + "si")
            .Select(si => string.Concat(si.Descendants(WorkbookSpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static IEnumerable<string> ReadWorkbookRowValues(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var nextColumn = 1;
        foreach (var cell in row.Elements(WorkbookSpreadsheetNamespace + "c"))
        {
            var column = ResolveWorkbookColumnFromReference(cell.Attribute("r")?.Value) ?? nextColumn;
            if (column > MaxWorkbookColumns)
            {
                // Cells are written in ascending column order, so a reference beyond the safe
                // worksheet width means the rest of the row is out of range too. Stop here instead
                // of gap-filling thousands of empty cells for a lone far-right value.
                break;
            }

            while (nextColumn < column)
            {
                yield return string.Empty;
                nextColumn++;
            }

            yield return ReadWorkbookCellValue(cell, sharedStrings);
            nextColumn = column + 1;
        }
    }

    private static string ReadWorkbookCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.Element(WorkbookSpreadsheetNamespace + "v")?.Value
            ?? cell.Element(WorkbookSpreadsheetNamespace + "is")?.Value
            ?? string.Empty;
        var type = cell.Attribute("t")?.Value;
        if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return value;
    }

    private static int? ResolveWorkbookColumnFromReference(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return null;
        }

        var index = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            index = (index * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return index == 0 ? null : index;
    }

    private sealed record WorkbookSheetContent(string Name, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated = false);
}
