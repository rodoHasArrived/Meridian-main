# Meridian AI Recent Changes

> Auto-generated on 2026-11-05T23:19:23Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

Rolling source-file activity for the last 14 days.

| File | Subsystem | Last modified | Commit | Summary | Touches |
|---|---|---|---|---|---|
| `src/Meridian.Contracts/README.md` | Host and Composition | 2026-11-05T22:37:53+00:00 | `a87817c8` | fix(security-master): refuse shared-reference primaries on create and amend | 30 |
| `src/Meridian.FSharp/README.md` | Domain, Ledger, and F# | 2026-11-05T22:37:53+00:00 | `a87817c8` | fix(security-master): refuse shared-reference primaries on create and amend | 14 |
| `src/Meridian.FSharp/Domain/SecurityMasterCommands.fs` | Domain, Ledger, and F# | 2026-11-05T22:37:53+00:00 | `a87817c8` | fix(security-master): refuse shared-reference primaries on create and amend | 9 |
| `src/Meridian.Storage/SecurityMaster/Migrations/064_custom_asset_profile_field_projection.sql` | Providers and Storage | 2026-11-05T22:34:11+00:00 | `a26dab51` | Index CustomAsset text values by hash so long text cannot fail the projection write | 2 |
| `src/Meridian.Storage/SecurityMaster/PostgresCustomAssetProfileFieldProjectionStore.cs` | Providers and Storage | 2026-11-05T22:34:11+00:00 | `a26dab51` | Index CustomAsset text values by hash so long text cannot fail the projection write | 2 |
| `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore.cs` | Providers and Storage | 2026-11-05T22:11:28+00:00 | `9ad456e9` | Select the option projection tables by descriptor fact, not by class name | 10 |
| `src/Meridian.Application/README.md` | Host and Composition | 2026-11-05T19:20:34+00:00 | `e7d86c2b` | Refuse OtherSecurity classification text the label reader would misread | 32 |
| `src/Meridian.Application/SecurityMaster/SecurityMasterService.cs` | Host and Composition | 2026-11-05T19:20:34+00:00 | `e7d86c2b` | Refuse OtherSecurity classification text the label reader would misread | 12 |
| `src/Meridian.Application/SecurityMaster/SecurityMasterService.Creation.cs` | Host and Composition | 2026-11-05T19:20:34+00:00 | `e7d86c2b` | Refuse OtherSecurity classification text the label reader would misread | 4 |
| `src/Meridian.Contracts/SecurityMaster/ClassificationLabelReader.cs` | Host and Composition | 2026-11-05T19:20:34+00:00 | `e7d86c2b` | Refuse OtherSecurity classification text the label reader would misread | 3 |
| `src/Meridian.Application/SecurityMaster/SecurityMasterService.ClassificationText.cs` | Host and Composition | 2026-11-05T19:20:34+00:00 | `e7d86c2b` | Refuse OtherSecurity classification text the label reader would misread | 1 |
| `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs` | Desktop and UI Workflows | 2026-11-05T19:05:10+00:00 | `1b819b69` | fix(security-master): decode each retained label exactly once | 12 |
| `src/Meridian.Ui.Shared/Services/SecurityMasterWorkbenchQueryService.cs` | Desktop and UI Workflows | 2026-11-05T19:05:10+00:00 | `1b819b69` | fix(security-master): decode each retained label exactly once | 8 |
| `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs` | Desktop and UI Workflows | 2026-11-05T18:55:46+00:00 | `4aa070c5` | fix(reporting): export trial-balance rows with their already-decoded labels | 8 |
| `src/Meridian.FSharp.DirectLending.Aggregates/ServicingSchedule.fs` | Domain, Ledger, and F# | 2026-11-05T17:51:10+00:00 | `a3fba8db` | fix(direct-lending): roll servicing schedule dates on the schedule kernel | 2 |
| `src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 12 |
| `src/Meridian.Application/DirectLending/PostgresDirectLendingCommandService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 10 |
| `src/Meridian.FSharp.DirectLending.Aggregates/README.md` | Domain, Ledger, and F# | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 9 |
| `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs` | Desktop and UI Workflows | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 9 |
| `src/Meridian.Application/DirectLending/AccrualLedgerService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/DirectLendingEventRebuilder.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/DirectLendingOutboxDispatcher.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/DirectLendingServiceSupport.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/IAccrualLedgerService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/IDirectLendingCommandService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/IDirectLendingService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/InMemoryDirectLendingService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/LoanAccountingProjector.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/PostgresDirectLendingService.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Contracts/DirectLending/DirectLendingCommandResults.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Contracts/DirectLending/DirectLendingDtos.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.FSharp.DirectLending.Aggregates/ContractAggregate.fs` | Domain, Ledger, and F# | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.FSharp.DirectLending.Aggregates/ServicingAggregate.fs` | Domain, Ledger, and F# | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Storage/DirectLending/PostgresDirectLendingStateStore.cs` | Providers and Storage | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Storage/Ledger/LedgerPeriodPostingGuard.cs` | Providers and Storage | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 8 |
| `src/Meridian.Application/DirectLending/DailyAccrualWorker.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 7 |
| `src/Meridian.FSharp.DirectLending.Aggregates/AggregateTypes.fs` | Domain, Ledger, and F# | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 7 |
| `src/Meridian.FSharp.DirectLending.Aggregates/Interop.fs` | Domain, Ledger, and F# | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 7 |
| `src/Meridian.FSharp.DirectLending.Aggregates/Meridian.FSharp.DirectLending.Aggregates.fsproj` | Domain, Ledger, and F# | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 7 |
| `src/Meridian.Application/DirectLending/DirectLendingServicingPolicyValidator.cs` | Host and Composition | 2026-11-05T15:47:57+00:00 | `11f974e8` | feat(direct-lending): PIK capitalization policy and floating-rate reset scheduling | 1 |

