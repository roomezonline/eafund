# MiniBank - Standardized Deposit Behavior

This document summarizes the new standardized fields, UI changes, data model updates, runtime migrations, and test scenarios for Deposit Types.

## Standardized Fields (nullable for backward compatibility)
- AccrualBasis: Daily | Monthly | Annual
- PostingFrequency: Daily | Monthly | Quarterly | Annual | OnMaturity
- Compounding: None | Daily | Monthly | Quarterly | Annual
- PostingTarget: SameAccount | SupportAccount
- DayCountConvention: Actual/360 | Actual/365 | Actual/Actual | 30E/360
- RoundingRule: None | HalfUp | HalfEven
- MinBalanceForInterest: decimal
- TaxRate: decimal (percentage)

Model: `src/MiniBank.Web/Models/DepositType.cs`
Enums: `src/MiniBank.Web/Models/DepositStandards.cs`
Display helpers: `src/MiniBank.Web/Models/DepositEnumDisplayExtensions.cs`

## Database Schema
- Idempotent migration in `database/schema.sql` adds all new columns to `dbo.DepositTypes`.
- Backfill block maps textual `InterestCalcPeriod` (e.g. "روزانه/ماهانه/سالانه") to enum columns.
- Sample data (idempotent) for two deposit types and sample rates at end of the file.

## Runtime Migration Execution
The app runs `database/schema.sql` at startup and before admin pages load to ensure columns/tables exist.
- Service: `src/MiniBank.Web/Services/DatabaseInitializationService.cs`
  - `InitializeDatabaseAsync()` calls `ExecuteSchemaScriptAsync()` plus individual ensure-* methods.
  - `EnsureMembersReadyAsync()` (used by admin pages) also calls `ExecuteSchemaScriptAsync()`.
  - `ExecuteSchemaScriptAsync()` robustly locates `schema.sql` and splits batches by GO.
  - `EnsureDepositTypeStandardColumnsAsync()` explicitly adds new columns as a fallback.
- Program wiring: `src/MiniBank.Web/Program.cs`

## Feature Flag
- `appsettings.json` → `Features.UseStandardizedDepositBehavior` (default: true)
- When true, the UI shows standardized selects and numeric fields.
- When false, UI reverts to legacy fields for backward compatibility.

## UI Changes
- Edit Sheet: `Pages/Admin/DepositTypes/DepositTypeIOSSheet.razor`
  - Tooltips and Persian hints for behavioral options
  - Validation: ranges, logical constraints (e.g., compounding requires PostingTarget=SameAccount)
- List: `Pages/Admin/DepositTypes/DepositTypes.razor`
  - Shows standardized metadata (ToFa) under each item when feature flag is enabled

## Reports
- `Reports/AllDepositRatesReport.cs` and `Reports/DepositRatesReport.cs`
  - Include standardized metadata in PDF outputs, using `ToFa()`.

## Test Scenarios
1) Daily accrual, monthly posting, no compounding, SameAccount target.
2) Monthly compounding; ensure warning if target is not SameAccount.
3) MinBalance and TaxRate boundary values; BreakRate/TaxRate must be 0..100; MinBalance >= 0.
4) Backfill: existing rows with InterestCalcPeriod = "ماهانه" or "سالانه" → Accrual/Posting set accordingly.
5) Feature flag OFF: legacy UI shows; ensure no errors.
6) Reports: verify metadata is present and localized.

## Troubleshooting
- If you see "Invalid column name" errors:
  - Restart the app so `ExecuteSchemaScriptAsync()` runs.
  - Check application logs for the selected `schema.sql` path and batch execution.
  - Ensure DB user has permissions to ALTER TABLE.

