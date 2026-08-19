using Meridian.Identity.Auth;
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
    private const int MaxWorkbookSheets = 64;
    private const long MaxWorkbookCellsPerWorkbook = 2_000_000;
    private const int MaxWorkbookIssuesPerSheet = 1_000;

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
        .WithName("GetWorkstationOnboardingWorkbook").DeclareOpenRead("Renders the static data-upload template catalog as a workbook; carries no deployment, account or tenant state.")
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
        .WithName("PreviewWorkstationOnboardingWorkbook").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
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
            || sheetPreviews.Count == 0
            || totalParsedRows == 0;
        var status = hasError ? "NeedsSchemaRepair" : "ReadyForReview";
        var nextAction = sheetPreviews.Count == 0
            ? "No data sheets were recognized. Download the onboarding workbook and fill in its data tabs before uploading."
            : totalParsedRows == 0
                ? "The workbook has no data rows yet. Fill in at least one data tab before uploading."
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
        var headerPosition = ResolveWorkbookHeaderPosition(sheet);
        if (headerPosition < 0)
        {
            if (sheet.Truncated)
            {
                // The sheet hit the row/cell cap before any header row was reached, so unread rows
                // exist behind the cap. Block instead of reporting Empty; otherwise another sheet's
                // data could still let the overall preview reach ReadyForReview.
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "rows",
                    $"Sheet '{sheet.Name}' is too large to validate in full (over {MaxWorkbookRowsPerSheet} rows or {MaxWorkbookCellsPerSheet} cells) and no header row was reached. Split it into smaller uploads and re-upload.",
                    RowNumber: null,
                    SheetName: sheet.Name,
                    CellReference: null));
            }

            return new DataUploadWorkbookSheetPreviewDto(
                sheet.Name,
                template?.TemplateId,
                template?.Label,
                template?.DataDomain,
                ParsedRowCount: 0,
                PreviewRowCount: 0,
                Headers: [],
                PreviewRows: [],
                Issues: issues,
                Status: sheet.Truncated ? "NeedsRepair" : "Empty");
        }

        var headerRow = sheet.Rows[headerPosition];
        var headerRowNumber = sheet.RowNumbers[headerPosition];
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
        var validationTruncated = false;
        for (var position = headerPosition + 1; position < sheet.Rows.Count; position++)
        {
            var row = sheet.Rows[position];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            parsedRowCount++;
            // Use the row's real worksheet number (Excel omits blank rows from sheetData, so a
            // positional index would drift) so issue row/cell references stay aligned to the cell
            // the operator actually needs to fix.
            var displayRowNumber = sheet.RowNumbers[position];

            // Cap per-sheet issues: a large but in-bounds workbook (e.g. 200k rows each missing
            // required cells) would otherwise allocate and serialize millions of issue DTOs. Once the
            // cap is reached, stop validating further rows and record one truncation summary below.
            if (issues.Count < MaxWorkbookIssuesPerSheet)
            {
                // Excel omits empty trailing cells, so a row with fewer values than headers just means
                // optional trailing columns were left blank (missing required cells are still caught by
                // the per-cell check below). Only warn when a row carries more values than headers.
                if (row.Count > headers.Length)
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Warning",
                        "row",
                        $"Row has {row.Count.ToString(CultureInfo.InvariantCulture)} values but the sheet has {headers.Length.ToString(CultureInfo.InvariantCulture)} headers; extra trailing values are ignored.",
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
            }
            else
            {
                validationTruncated = true;
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

        if (validationTruncated)
        {
            // Blocking: rows past the issue cap were not validated, so the sheet cannot be certified
            // ready with potentially unreported errors behind it.
            issues.Add(new DataUploadValidationIssueDto(
                "Error",
                "rows",
                $"Validation stopped after {MaxWorkbookIssuesPerSheet} issues on sheet '{sheet.Name}'; fix the reported cells and re-upload to surface any remaining issues.",
                RowNumber: null,
                SheetName: sheet.Name,
                CellReference: null));
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

        var headerPosition = ResolveWorkbookHeaderPosition(entitySheet);
        if (headerPosition < 0)
        {
            return [];
        }

        var dataRows = new List<(int Number, IReadOnlyList<string> Values)>();
        for (var position = headerPosition + 1; position < entitySheet.Rows.Count; position++)
        {
            var values = entitySheet.Rows[position];
            if (values.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                dataRows.Add((entitySheet.RowNumbers[position], values));
            }
        }

        var entityIds = new HashSet<string>(
            dataRows
                .Select(row => entityIdColumn < row.Values.Count ? row.Values[entityIdColumn].Trim() : string.Empty)
                .Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (!hasParentColumn)
        {
            return [];
        }

        var issues = new List<DataUploadValidationIssueDto>();
        var parentOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rowOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (displayRowNumber, values) in dataRows)
        {
            var entityId = entityIdColumn < values.Count ? values[entityIdColumn].Trim() : string.Empty;
            if (entityId.Length > 0)
            {
                rowOf.TryAdd(entityId, displayRowNumber);
            }

            var parent = parentColumn < values.Count ? values[parentColumn].Trim() : string.Empty;
            if (parent.Length == 0)
            {
                continue;
            }

            var cellReference = $"{entitySheet.Name}!{WorkbookColumnLetter(parentColumn)}{displayRowNumber}";
            if (string.Equals(parent, entityId, StringComparison.OrdinalIgnoreCase))
            {
                if (issues.Count < MaxWorkbookIssuesPerSheet)
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Error",
                        "parent_entity_id",
                        $"parent_entity_id '{parent}' refers to its own entity_id (self-referencing hierarchy).",
                        RowNumber: displayRowNumber,
                        SheetName: entitySheet.Name,
                        CellReference: cellReference));
                }

                continue;
            }

            if (!entityIds.Contains(parent))
            {
                if (issues.Count < MaxWorkbookIssuesPerSheet)
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Error",
                        "parent_entity_id",
                        $"parent_entity_id '{parent}' does not resolve to an entity_id in the '{entitySheet.Name}' sheet.",
                        RowNumber: displayRowNumber,
                        SheetName: entitySheet.Name,
                        CellReference: cellReference));
                }

                continue;
            }

            // Valid-looking edge to an existing entity: record it so multi-row cycles (e.g. A->B->A)
            // can be detected below. Self-references and dangling parents are already reported above.
            if (entityId.Length > 0)
            {
                parentOf.TryAdd(entityId, parent);
            }
        }

        foreach (var cyclicEntityId in DetectCyclicEntities(parentOf))
        {
            if (issues.Count >= MaxWorkbookIssuesPerSheet)
            {
                break;
            }

            if (!rowOf.TryGetValue(cyclicEntityId, out var cyclicRowNumber))
            {
                continue;
            }

            issues.Add(new DataUploadValidationIssueDto(
                "Error",
                "parent_entity_id",
                $"entity_id '{cyclicEntityId}' is part of a circular parent_entity_id hierarchy and cannot be committed.",
                RowNumber: cyclicRowNumber,
                SheetName: entitySheet.Name,
                CellReference: $"{entitySheet.Name}!{WorkbookColumnLetter(parentColumn)}{cyclicRowNumber}"));
        }

        if (issues.Count >= MaxWorkbookIssuesPerSheet)
        {
            issues.Add(new DataUploadValidationIssueDto(
                "Error",
                "parent_entity_id",
                $"Cross-sheet validation stopped after {MaxWorkbookIssuesPerSheet} entity-hierarchy issues; fix the reported rows and re-upload to surface any remaining issues.",
                RowNumber: null,
                SheetName: entitySheet.Name,
                CellReference: null));
        }

        return issues;
    }

    /// <summary>
    /// Detects entity ids that participate in a parent_entity_id cycle. The parent map is a
    /// functional graph (each entity has at most one parent), so each chain is followed once and
    /// any node reached while still on the current walk marks the enclosing cycle.
    /// </summary>
    private static HashSet<string> DetectCyclicEntities(IReadOnlyDictionary<string, string> parentOf)
    {
        const int OnStack = 1;
        const int Settled = 2;
        var cyclic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in parentOf.Keys)
        {
            if (state.TryGetValue(start, out var startState) && startState == Settled)
            {
                continue;
            }

            var walk = new List<string>();
            string? current = start;
            while (current is not null
                && (!state.TryGetValue(current, out var currentState) || currentState == 0))
            {
                state[current] = OnStack;
                walk.Add(current);
                current = parentOf.TryGetValue(current, out var parent) ? parent : null;
            }

            if (current is not null
                && state.TryGetValue(current, out var reachedState)
                && reachedState == OnStack)
            {
                var cycleStart = walk.FindIndex(node => string.Equals(node, current, StringComparison.OrdinalIgnoreCase));
                for (var index = cycleStart; index >= 0 && index < walk.Count; index++)
                {
                    cyclic.Add(walk[index]);
                }
            }

            foreach (var node in walk)
            {
                state[node] = Settled;
            }
        }

        return cyclic;
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

    private static int ResolveWorkbookHeaderPosition(WorkbookSheetContent sheet)
    {
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            if (sheet.Rows[index].Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                return index;
            }
        }

        return -1;
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
        var dateStyles = ReadWorkbookDateStyles(archive);
        var usesDate1904 = ReadWorkbookUsesDate1904(archive);
        var sheets = new List<WorkbookSheetContent>();
        var workbookCellCount = 0L;
        foreach (var (name, entryPath) in ResolveWorkbookSheetOrder(archive))
        {
            // Workbook-level caps: the per-sheet limits do not bound total work when a highly
            // compressible .xlsx packs many small sheets, so reject abusive sheet/cell totals.
            if (sheets.Count >= MaxWorkbookSheets)
            {
                throw new InvalidDataException($"Workbook contains more than {MaxWorkbookSheets} worksheets.");
            }

            var entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                continue;
            }

            var document = LoadWorkbookXml(entry);
            var rows = new List<IReadOnlyList<string>>();
            var rowNumbers = new List<int>();
            var truncated = false;
            var cellCount = 0;
            var previousRowNumber = 0;
            foreach (var rowElement in document
                .Descendants(WorkbookSpreadsheetNamespace + "sheetData")
                .Elements(WorkbookSpreadsheetNamespace + "row"))
            {
                if (rows.Count >= MaxWorkbookRowsPerSheet || cellCount >= MaxWorkbookCellsPerSheet)
                {
                    truncated = true;
                    break;
                }

                // Excel omits blank rows from sheetData, so trust each row's own 1-based index (r) to
                // keep issue row/cell references aligned to the real worksheet, falling back to a
                // running counter only when the attribute is missing or not monotonic.
                var rowNumber = int.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumber)
                    && parsedNumber > previousRowNumber
                        ? parsedNumber
                        : previousRowNumber + 1;
                previousRowNumber = rowNumber;

                var values = ReadWorkbookRowValues(rowElement, sharedStrings, dateStyles, usesDate1904).ToArray();
                cellCount += values.Length;
                rows.Add(values);
                rowNumbers.Add(rowNumber);
            }

            workbookCellCount += cellCount;
            if (workbookCellCount > MaxWorkbookCellsPerWorkbook)
            {
                throw new InvalidDataException($"Workbook exceeds the {MaxWorkbookCellsPerWorkbook} total cell limit.");
            }

            sheets.Add(new WorkbookSheetContent(name, rows, rowNumbers, truncated));
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
        var relationshipTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in rels.Descendants(WorkbookPackageRelationshipsNamespace + "Relationship"))
        {
            var relationshipId = relationship.Attribute("Id")?.Value;
            if (string.IsNullOrEmpty(relationshipId))
            {
                continue;
            }

            if (!relationshipTargets.TryAdd(relationshipId, relationship.Attribute("Target")?.Value ?? string.Empty))
            {
                throw new InvalidDataException("Workbook contains duplicate relationship ids.");
            }
        }

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

    private static IEnumerable<string> ReadWorkbookRowValues(
        XElement row,
        IReadOnlyList<string> sharedStrings,
        IReadOnlySet<int> dateStyles,
        bool usesDate1904)
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

            yield return ReadWorkbookCellValue(cell, sharedStrings, dateStyles, usesDate1904);
            nextColumn = column + 1;
        }
    }

    private static string ReadWorkbookCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        IReadOnlySet<int> dateStyles,
        bool usesDate1904)
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

        // Excel stores dates as numeric serials with a date-formatted style rather than text. Return
        // an ISO date so a filled date column previews as "2026-06-01" (and required-field checks see
        // a real date), not a raw serial such as "46174".
        if ((type is null || type == "n")
            && value.Length > 0
            && int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex)
            && dateStyles.Contains(styleIndex)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial > 0)
        {
            try
            {
                // FromOADate uses the 1900 date system; workbooks flagged date1904 store serials
                // 1462 days lower for the same calendar date, so shift before converting.
                var date = DateTime.FromOADate(usesDate1904 ? serial + 1462 : serial);
                return date.TimeOfDay == TimeSpan.Zero
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException)
            {
                return value;
            }
        }

        return value;
    }

    /// <summary>
    /// Returns whether the workbook uses Excel's 1904 date system (<c>workbookPr date1904</c>), whose
    /// serials are 1462 days lower than the default 1900 system for the same calendar date.
    /// </summary>
    private static bool ReadWorkbookUsesDate1904(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry is null)
        {
            return false;
        }

        var document = LoadWorkbookXml(entry);
        var flag = document
            .Descendants(WorkbookSpreadsheetNamespace + "workbookPr")
            .Select(properties => properties.Attribute("date1904")?.Value)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

        return string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the set of <c>cellXfs</c> style indices that carry a date or time number format, so
    /// numeric cells using those styles can be normalized from Excel serials back to ISO dates.
    /// </summary>
    private static HashSet<int> ReadWorkbookDateStyles(ZipArchive archive)
    {
        var dateStyles = new HashSet<int>();
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return dateStyles;
        }

        var document = LoadWorkbookXml(entry);

        var customDateFormatIds = new HashSet<int>();
        foreach (var numberFormat in document.Descendants(WorkbookSpreadsheetNamespace + "numFmt"))
        {
            if (int.TryParse(numberFormat.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && IsDateFormatCode(numberFormat.Attribute("formatCode")?.Value))
            {
                customDateFormatIds.Add(id);
            }
        }

        var cellFormats = document
            .Descendants(WorkbookSpreadsheetNamespace + "cellXfs")
            .Elements(WorkbookSpreadsheetNamespace + "xf")
            .ToList();
        for (var index = 0; index < cellFormats.Count; index++)
        {
            if (int.TryParse(cellFormats[index].Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numFmtId)
                && (IsBuiltInDateFormat(numFmtId) || customDateFormatIds.Contains(numFmtId)))
            {
                dateStyles.Add(index);
            }
        }

        return dateStyles;
    }

    private static bool IsBuiltInDateFormat(int numFmtId)
        => numFmtId is (>= 14 and <= 22) or (>= 45 and <= 47);

    private static bool IsDateFormatCode(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode))
        {
            return false;
        }

        // Strip quoted literals, escaped characters, and bracketed tokens (e.g. [Red], [$-409]) so
        // only genuine format letters remain, then look for date/time tokens.
        var stripped = new System.Text.StringBuilder(formatCode.Length);
        var inQuote = false;
        for (var i = 0; i < formatCode.Length; i++)
        {
            var character = formatCode[i];
            if (character == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
            {
                continue;
            }

            if (character == '\\')
            {
                i++;
                continue;
            }

            if (character == '[')
            {
                while (i < formatCode.Length && formatCode[i] != ']')
                {
                    i++;
                }

                continue;
            }

            stripped.Append(character);
        }

        var text = stripped.ToString().ToLowerInvariant();
        return text.IndexOfAny(['y', 'd', 'h', 's', 'm']) >= 0;
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

    private sealed record WorkbookSheetContent(
        string Name,
        IReadOnlyList<IReadOnlyList<string>> Rows,
        IReadOnlyList<int> RowNumbers,
        bool Truncated = false);
}
