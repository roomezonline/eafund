using Dapper;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using MiniBank.Web.Utilities;
using System.Data;
using System.Globalization;

namespace MiniBank.Web.Services
{
    public class FiscalYearService
    {
        private readonly FiscalYearRepository _repo;
        private readonly JournalsRepository _journalsRepo;
        private readonly IDbConnection _connection;
        private readonly FiscalYearRangeProvider _rangeProvider;

        public FiscalYearService(FiscalYearRepository repo, JournalsRepository journalsRepo, IDbConnection connection, FiscalYearRangeProvider rangeProvider)
        {
            _repo = repo;
            _journalsRepo = journalsRepo;
            _connection = connection;
            _rangeProvider = rangeProvider;
        }

        public Task<IEnumerable<FiscalYear>> GetAllAsync() => _repo.GetAllAsync();
        public Task<FiscalYear?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
        public Task<FiscalYear?> GetByYearCodeAsync(int yearCode) => _repo.GetByYearCodeAsync(yearCode);

        public async Task<int> CreateAsync(FiscalYear model)
        {
            var existing = await _repo.GetByYearCodeAsync(model.YearCode);
            if (existing != null)
                throw new InvalidOperationException($"سال مالی {model.YearCode} قبلاً ثبت شده است");
            await EnsureNoRangeOverlapAsync(model, null);
            var id = await _repo.CreateAsync(model);
            _rangeProvider.Invalidate();
            return id;
        }

        public async Task UpdateAsync(FiscalYear model)
        {
            var existing = await _repo.GetByIdAsync(model.FiscalYearId);
            if (existing == null)
                throw new InvalidOperationException("سال مالی یافت نشد");
            if (existing.IsClosed)
                throw new InvalidOperationException("سال مالی بسته شده قابل ویرایش نیست");
            await EnsureNoRangeOverlapAsync(model, model.FiscalYearId);
            await _repo.UpdateAsync(model);
            _rangeProvider.Invalidate();
        }

        /// <summary>
        /// بازهٔ سال‌های مالی نباید همپوشانی داشته باشد (کوئری‌های "سالِ باز برای تاریخ" فرض تداخل صفر دارند).
        /// خالی گذاشتنِ فاصله بین دو سال مجاز است و این متد آن را محدود نمی‌کند.
        /// </summary>
        private async Task EnsureNoRangeOverlapAsync(FiscalYear model, int? selfId)
        {
            if (model.StartDate.Date > model.EndDate.Date)
                throw new InvalidOperationException("تاریخ شروع سال مالی نمی‌تواند بعد از تاریخ پایان باشد.");

            var all = (await _repo.GetAllAsync()).ToList();
            var clash = all.FirstOrDefault(x =>
                (!selfId.HasValue || x.FiscalYearId != selfId.Value) &&
                model.StartDate.Date <= x.EndDate.Date &&
                x.StartDate.Date <= model.EndDate.Date);

            if (clash != null)
                throw new InvalidOperationException(
                    $"بازهٔ تاریخ این سال مالی با سال مالی {clash.YearCode} " +
                    $"({clash.StartDate:yyyy/MM/dd} تا {clash.EndDate:yyyy/MM/dd}) همپوشانی دارد. " +
                    "بازهٔ سال‌های مالی نباید همپوشان باشد. (خلل بین دو سال مانعی ندارد.)");
        }

        public async Task DeleteAsync(int id)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new InvalidOperationException("سال مالی یافت نشد");

            if (existing.IsClosed)
                throw new InvalidOperationException(
                    $"سال مالی {existing.YearCode} بسته شده و قابل حذف نیست. " +
                    "در صورت نیاز ابتدا از «برگشت انتقال» استفاده کنید.");

            var journalCount = await _connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM dbo.Journals WHERE FiscalYearId = @id AND IsDeleted = 0",
                new { id });
            if (journalCount > 0)
                throw new InvalidOperationException(
                    $"سال مالی {existing.YearCode} دارای {journalCount} سند است و قابل حذف نیست. " +
                    "فقط سال مالیِ خالی قابل حذف است.");

            var balanceCount = await _connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM dbo.FiscalYearBalances WHERE FiscalYearId = @id",
                new { id });
            if (balanceCount > 0)
                throw new InvalidOperationException(
                    $"سال مالی {existing.YearCode} دارای ماندهٔ افتتاحیه است و قابل حذف نیست. " +
                    "فقط سال مالیِ خالی قابل حذف است.");

            await _repo.DeleteAsync(id);
            _rangeProvider.Invalidate();
        }

        public async Task<bool> IsYearOpenForDateAsync(DateTime date)
        {
            var fy = await _repo.GetOpenYearForDateAsync(date);
            return fy != null;
        }

        public async Task<ClosingPreview> BuildClosingPreviewAsync(int fiscalYearId)
        {
            var fy = await _repo.GetByIdAsync(fiscalYearId);
            if (fy == null) throw new InvalidOperationException("سال مالی یافت نشد");
            if (fy.IsClosed) throw new InvalidOperationException("سال مالی قبلاً بسته شده است");

            var preview = new ClosingPreview { FiscalYear = fy };

            var nextYear = await _repo.GetByYearCodeAsync(fy.YearCode + 1);
            if (nextYear == null)
                throw new InvalidOperationException($"سال مالی {fy.YearCode + 1} وجود ندارد. ابتدا سال مالی جدید را بسازید.");
            if (nextYear.IsClosed)
                throw new InvalidOperationException($"سال مالی {nextYear.YearCode} بسته شده است.");
            preview.NextFiscalYear = nextYear;

            // Pre-check uses the SAME date basis as the balance computation below, so journals that
            // carry no FiscalYearId (legacy/inserted while the trigger was disabled) are not skipped.
            preview.UnfinalizedJournalCount = await _connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM dbo.Journals
                  WHERE IsDeleted = 0 AND Status < 3
                    AND (FiscalYearId = @fiscalYearId
                         OR (OccurredAt >= @start AND OccurredAt < DATEADD(DAY, 1, @end)))",
                new { fiscalYearId, start = fy.StartDate, end = fy.EndDate });

            var accountRows = (await _connection.QueryAsync<(long AccountId, int AccountType, string AccountCode, string AccountName, string TypeName, decimal TotalDebit, decimal TotalCredit)>(
                @"SELECT l1.AccountId, l1.AccountType, l1.Code AS AccountCode, l1.Name AS AccountName, at.Name AS TypeName,
                          ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Debit ELSE 0 END), 0)
                        + ISNULL(SUM(CASE WHEN j2.OccurredAt >= @start AND j2.OccurredAt < DATEADD(DAY, 1, @end) THEN jl2.Debit ELSE 0 END), 0) AS TotalDebit,
                          ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Credit ELSE 0 END), 0)
                        + ISNULL(SUM(CASE WHEN j2.OccurredAt >= @start AND j2.OccurredAt < DATEADD(DAY, 1, @end) THEN jl2.Credit ELSE 0 END), 0) AS TotalCredit
                  FROM dbo.Accounts l1
                  JOIN dbo.AccountTypes at ON at.AccountTypeId = l1.AccountType
                  LEFT JOIN dbo.JournalLines jl ON l1.AccountId = jl.AccountId
                  LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                  LEFT JOIN dbo.JournalLines jl2 ON (
                      jl2.ParentAccountId = l1.AccountId OR
                      jl2.ParentAccountId IN (SELECT AccountId FROM dbo.Accounts WHERE ParentAccountId = l1.AccountId AND IsActive = 1) OR
                      jl2.ParentAccountId IN (
                          SELECT child.AccountId FROM dbo.Accounts parent
                          INNER JOIN dbo.Accounts child ON child.ParentAccountId = parent.AccountId
                          WHERE parent.ParentAccountId = l1.AccountId AND parent.IsActive = 1 AND child.IsActive = 1
                      )
                  )
                  LEFT JOIN dbo.vw_ApprovedJournals j2 ON jl2.JournalId = j2.JournalId
                  WHERE l1.Level = 2 AND l1.IsActive = 1
                  GROUP BY l1.AccountId, l1.AccountType, l1.Code, l1.Name, at.Name
                  ORDER BY l1.Code",
                new { start = fy.StartDate, end = fy.EndDate })).ToList();

            foreach (var r in accountRows.Where(r => r.TotalDebit != r.TotalCredit))
            {
                var rawNet = r.TotalDebit - r.TotalCredit;
                var balance = (r.AccountType == 2 || r.AccountType == 3 || r.AccountType == 4) ? -rawNet : rawNet;
                preview.SummaryRows.Add(new ClosingSummaryRow
                {
                    AccountType = r.AccountType,
                    AccountId = r.AccountId,
                    AccountCode = r.AccountCode,
                    AccountName = r.AccountName,
                    TypeName = r.TypeName,
                    TotalDebit = r.TotalDebit,
                    TotalCredit = r.TotalCredit,
                    NetBalance = balance
                });
            }

            // اطلاع‌رسانی: حساب‌های غیرفعالی که در این بازه مانده دارند در محاسبهٔ مانده لحاظ نمی‌شوند
            // و باعث نامتوازن شدن سند می‌شوند. فقط هشدار است؛ رفتار انتقال را تغییر نمی‌دهد.
            foreach (var r in await GetInactiveAccountsWithBalanceAsync(fy.StartDate, fy.EndDate))
                preview.InactiveAccountsWithBalance.Add(r);

            var openingRows = (await _connection.QueryAsync<(long MoeenId, string MoeenCode, string MoeenName, long AccountId, string Code, string Name, int Level, decimal Debit, decimal Credit)>(
                @"SELECT
                      COALESCE(moeen.AccountId, a.AccountId) AS MoeenId,
                      COALESCE(moeen.Code, a.Code) AS MoeenCode,
                      COALESCE(moeen.Name, a.Name) AS MoeenName,
                      a.AccountId, a.Code, a.Name, a.Level,
                      CASE WHEN SUM(jl.Debit - jl.Credit) >= 0 THEN SUM(jl.Debit - jl.Credit) ELSE 0 END AS Debit,
                      CASE WHEN SUM(jl.Debit - jl.Credit) < 0 THEN -SUM(jl.Debit - jl.Credit) ELSE 0 END AS Credit
                  FROM dbo.JournalLines jl
                  INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                  INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                  LEFT JOIN dbo.Accounts moeen ON jl.ParentAccountId = moeen.AccountId AND moeen.Level = 3
                  WHERE j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end)
                    AND a.IsActive = 1
                    AND a.Level IN (3, 4)
                  GROUP BY
                      COALESCE(moeen.AccountId, a.AccountId),
                      COALESCE(moeen.Code, a.Code),
                      COALESCE(moeen.Name, a.Name),
                      a.AccountId, a.Code, a.Name, a.Level
                  HAVING ABS(SUM(jl.Debit - jl.Credit)) > 0.01
                  ORDER BY COALESCE(moeen.Code, a.Code), a.Code",
                new { start = fy.StartDate, end = fy.EndDate })).ToList();

            var lineNo = 1;
            foreach (var r in openingRows)
            {
                preview.JournalLines.Add(new ClosingJournalLine
                {
                    LineNo = lineNo++,
                    MoeenId = r.MoeenId,
                    MoeenCode = r.MoeenCode,
                    MoeenName = r.MoeenName,
                    AccountId = r.AccountId,
                    AccountCode = r.Code,
                    AccountName = r.Name,
                    Level = r.Level,
                    Description = BuildTransferNotes(fy.YearCode, nextYear.YearCode),
                    Debit = r.Debit,
                    Credit = r.Credit
                });
            }

            preview.TotalDebit = preview.JournalLines.Sum(l => l.Debit);
            preview.TotalCredit = preview.JournalLines.Sum(l => l.Credit);
            return preview;
        }

        private async Task<List<string>> GetInactiveAccountsWithBalanceAsync(DateTime start, DateTime end)
        {
            try
            {
                var rows = await _connection.QueryAsync<(string Code, string Name, int Level, decimal Net)>(
                    @"SELECT a.Code, a.Name, a.Level, SUM(jl.Debit - jl.Credit) AS Net
                      FROM dbo.JournalLines jl
                      INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                      INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                      WHERE j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end)
                        AND a.IsActive = 0 AND a.Level IN (3, 4)
                      GROUP BY a.AccountId, a.Code, a.Name, a.Level
                      HAVING ABS(SUM(jl.Debit - jl.Credit)) > 0.01
                      ORDER BY a.Code",
                    new { start, end });
                return rows.Select(r => $"{r.Code} – {r.Name} ({r.Net:N0})").ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<int> ExecuteClosingAsync(int fiscalYearId, string createdBy)
        {
            var fy = await _repo.GetByIdAsync(fiscalYearId);
            if (fy == null) throw new InvalidOperationException("سال مالی یافت نشد");
            if (fy.IsClosed) throw new InvalidOperationException("سال مالی قبلاً بسته شده است");

            var preview = await BuildClosingPreviewAsync(fiscalYearId);
            if (Math.Abs(preview.TotalDebit - preview.TotalCredit) > 0.01m)
            {
                var inactive = preview.InactiveAccountsWithBalance.Count > 0
                    ? preview.InactiveAccountsWithBalance
                    : await GetInactiveAccountsWithBalanceAsync(fy.StartDate, fy.EndDate);
                var hint = inactive.Count == 0
                    ? string.Empty
                    : " حساب‌های غیرفعالِ دارای مانده در این بازه: " + string.Join("، ", inactive) +
                      " — این حساب‌ها در محاسبهٔ مانده لحاظ نمی‌شوند و باعث نامتوازن شدن سند می‌شوند. ابتدا ماندهٔ این حساب‌ها را با انتقال به حسابِ فعال صفر کنید.";
                throw new InvalidOperationException("سند افتتاحیه نامتوازن است." + hint);
            }
            if (preview.UnfinalizedJournalCount > 0)
                throw new InvalidOperationException($"تعداد {preview.UnfinalizedJournalCount} سند قطعی نشده وجود دارد. ابتدا تمام اسناد را قطعی کنید.");
            if (preview.JournalLines.Count == 0)
                throw new InvalidOperationException("هیچ مانده‌ای برای انتقال وجود ندارد.");
            if (preview.NextFiscalYear == null)
                throw new InvalidOperationException($"سال مالی {fy.YearCode + 1} وجود ندارد.");

            var nextYear = preview.NextFiscalYear;

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var occurredAt = nextYear.StartDate;
                var notes = BuildTransferNotes(fy.YearCode, nextYear.YearCode);
                var journalId = await _connection.QuerySingleAsync<long>(
                    @"INSERT INTO dbo.Journals (FiscalYearId, OccurredAt, CreatedAt, CreatedBy, Notes, Status)
                      VALUES (@FiscalYearId, @OccurredAt, GETUTCDATE(), @CreatedBy, @Notes, 3);
                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    new { FiscalYearId = nextYear.FiscalYearId, OccurredAt = occurredAt, CreatedBy = createdBy, Notes = notes }, tx);

                foreach (var line in preview.JournalLines)
                {
                    await _connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, ParentAccountId, Debit, Credit, Description)
                          VALUES (@JournalId, @LineNo, @AccountId, @ParentAccountId, @Debit, @Credit, @Description)",
                        new
                        {
                            JournalId = journalId,
                            LineNo = line.LineNo,
                            AccountId = line.AccountId,
                            ParentAccountId = line.Level == 4 && line.MoeenId != line.AccountId ? line.MoeenId : (long?)null,
                            line.Debit,
                            line.Credit,
                            Description = notes
                        }, tx);
                }

                await _connection.ExecuteAsync(
                    @"INSERT INTO dbo.HouseholdReceipts
                        (TargetHouseholdId, DocumentType, TransactionType, CounterpartyName,
                         AmountTotal, PayChannel, OccurredAt, CreatedBy, Status, Notes, ExternalRef)
                      VALUES
                        (NULL, 2, '2-5', NULL,
                         @AmountTotal, 3, @OccurredAt, @CreatedBy, 1, @Notes, @ExternalRef)",
                    new
                    {
                        AmountTotal = preview.TotalDebit,
                        OccurredAt = occurredAt,
                        CreatedBy = createdBy,
                        Notes = notes,
                        ExternalRef = $"J:{journalId}"
                    }, tx);

                var actualTotals = await _connection.QuerySingleAsync<(decimal Debit, decimal Credit)>(
                    "SELECT ISNULL(SUM(Debit),0) AS Debit, ISNULL(SUM(Credit),0) AS Credit FROM dbo.JournalLines WHERE JournalId = @journalId",
                    new { journalId }, tx);
                if (Math.Abs(actualTotals.Debit - actualTotals.Credit) > 0.01m)
                    throw new InvalidOperationException($"سند افتتاحیه بعد از درج نامتوازن شد (بدهکار {actualTotals.Debit:N0} ≠ بستانکار {actualTotals.Credit:N0}). تراکنش برگشت خورد.");

                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var balances = preview.JournalLines
                    .GroupBy(l => l.AccountId)
                    .Select(g => new FiscalYearBalance
                    {
                        AccountId = g.Key,
                        OpeningDebit = g.Sum(x => x.Debit),
                        OpeningCredit = g.Sum(x => x.Credit)
                    })
                    .ToList();
                await _repo.SaveBalancesAsync(nextYear.FiscalYearId, balances, tx);

                await _journalsRepo.EnsureSerialCounterAsync(nextYear.FiscalYearId, tx);
                var docNum = await _journalsRepo.GetNextDocumentNumberAsync(nextYear.FiscalYearId, tx);
                await _journalsRepo.AssignDocumentNumberAsync(journalId, docNum, tx);

                fy.IsClosed = true;
                fy.IsOpen = false;
                fy.ClosedAt = DateTime.UtcNow;
                fy.ClosedBy = createdBy;
                await _repo.UpdateAsync(fy, tx);

                nextYear.IsOpen = true;
                nextYear.IsClosed = false;
                await _repo.UpdateAsync(nextYear, tx);

                var verified = await _connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.FiscalYears
                      WHERE (FiscalYearId = @srcId AND IsOpen = 0 AND IsClosed = 1)
                         OR (FiscalYearId = @dstId AND IsOpen = 1 AND IsClosed = 0)",
                    new { srcId = fy.FiscalYearId, dstId = nextYear.FiscalYearId }, tx);
                if (verified != 2)
                    throw new InvalidOperationException(
                        $"وضعیت سال‌های مالی ثبت نشد (سال {fy.YearCode} بسته / سال {nextYear.YearCode} افتتاح). تراکنش برگشت خورد.");

                tx.Commit();
                _rangeProvider.Invalidate();
                return docNum;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                await EnsureTriggersEnabledAsync();
                throw;
            }
        }

        private async Task EnsureTriggersEnabledAsync()
        {
            try
            {
                var wasClosed = _connection.State != ConnectionState.Open;
                if (wasClosed) _connection.Open();
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines");
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals");
            }
            catch { }
        }

        public async Task<List<AccountDetailRow>> GetAccountDetailAsync(long accountId, int fiscalYearId)
        {
            var fy = await _repo.GetByIdAsync(fiscalYearId);
            if (fy == null) throw new InvalidOperationException("سال مالی یافت نشد");

            var parentLevel = await _connection.QuerySingleOrDefaultAsync<int>(
                "SELECT Level FROM dbo.Accounts WHERE AccountId = @accountId", new { accountId });

            if (parentLevel < 2 || parentLevel > 3)
                return new List<AccountDetailRow>();

            if (parentLevel == 2)
            {
                var rows = (await _connection.QueryAsync<(long AccountId, string Code, string Name, int Level, decimal TotalDebit, decimal TotalCredit)>(
                    @"SELECT child.AccountId, child.Code, child.Name, child.Level,
                          ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Debit ELSE 0 END), 0)
                        + ISNULL(SUM(CASE WHEN j2.OccurredAt >= @start AND j2.OccurredAt < DATEADD(DAY, 1, @end) THEN jl2.Debit ELSE 0 END), 0) AS TotalDebit,
                          ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Credit ELSE 0 END), 0)
                        + ISNULL(SUM(CASE WHEN j2.OccurredAt >= @start AND j2.OccurredAt < DATEADD(DAY, 1, @end) THEN jl2.Credit ELSE 0 END), 0) AS TotalCredit
                      FROM dbo.Accounts child
                      LEFT JOIN dbo.JournalLines jl ON child.AccountId = jl.AccountId
                      LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                      LEFT JOIN dbo.JournalLines jl2 ON jl2.ParentAccountId = child.AccountId
                      LEFT JOIN dbo.vw_ApprovedJournals j2 ON jl2.JournalId = j2.JournalId
                      WHERE child.ParentAccountId = @accountId AND child.Level = 3 AND child.IsActive = 1
                      GROUP BY child.AccountId, child.Code, child.Name, child.Level
                      HAVING ABS(
                          ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Debit ELSE 0 END), 0)
                        + ISNULL(SUM(CASE WHEN j2.OccurredAt >= @start AND j2.OccurredAt < DATEADD(DAY, 1, @end) THEN jl2.Debit ELSE 0 END), 0)
                        - ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Credit ELSE 0 END), 0)
                        - ISNULL(SUM(CASE WHEN j2.OccurredAt >= @start AND j2.OccurredAt < DATEADD(DAY, 1, @end) THEN jl2.Credit ELSE 0 END), 0)
                      ) > 0.01
                      ORDER BY child.Code",
                    new { accountId, start = fy.StartDate, end = fy.EndDate })).ToList();

                return rows.Select(r => new AccountDetailRow
                {
                    AccountId = r.AccountId,
                    Code = r.Code,
                    Name = r.Name,
                    Level = r.Level,
                    TotalDebit = r.TotalDebit,
                    TotalCredit = r.TotalCredit
                }).ToList();
            }

            var tafsiliRows = (await _connection.QueryAsync<(long AccountId, string Code, string Name, int Level, decimal TotalDebit, decimal TotalCredit)>(
                @"SELECT DISTINCT a.AccountId, a.Code, a.Name, a.Level,
                      ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Debit ELSE 0 END), 0) AS TotalDebit,
                      ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Credit ELSE 0 END), 0) AS TotalCredit
                  FROM dbo.JournalLines jl
                  INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                  INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                  WHERE jl.ParentAccountId = @accountId AND a.Level = 4 AND a.IsActive = 1
                  GROUP BY a.AccountId, a.Code, a.Name, a.Level
                  HAVING ABS(
                      ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Debit ELSE 0 END), 0)
                    - ISNULL(SUM(CASE WHEN j.OccurredAt >= @start AND j.OccurredAt < DATEADD(DAY, 1, @end) THEN jl.Credit ELSE 0 END), 0)
                  ) > 0.01
                  ORDER BY a.Code",
                new { accountId, start = fy.StartDate, end = fy.EndDate })).ToList();

            return tafsiliRows.Select(r => new AccountDetailRow
            {
                AccountId = r.AccountId,
                Code = r.Code,
                Name = r.Name,
                Level = r.Level,
                TotalDebit = r.TotalDebit,
                TotalCredit = r.TotalCredit
            }).ToList();
        }

        public async Task<OpeningPreview> BuildOpeningPreviewAsync(int sourceYearId, int targetYearId)
        {
            EnsureSeparateOpeningAllowed();

            var source = await _repo.GetByIdAsync(sourceYearId);
            var target = await _repo.GetByIdAsync(targetYearId);
            if (source == null) throw new InvalidOperationException("سال مبدأ یافت نشد");
            if (target == null) throw new InvalidOperationException("سال مقصد یافت نشد");

            var balances = await _repo.GetBalancesAsync(sourceYearId);
            if (balances.Any())
            {
                var preview = new OpeningPreview
                {
                    SourceFiscalYear = source,
                    TargetFiscalYear = target,
                    JournalLines = balances.Select((b, i) => new OpeningJournalLine
                    {
                        LineNo = i + 1,
                        AccountId = b.AccountId,
                        Debit = b.OpeningDebit,
                        Credit = b.OpeningCredit
                    }).ToList()
                };
                preview.TotalDebit = preview.JournalLines.Sum(l => l.Debit);
                preview.TotalCredit = preview.JournalLines.Sum(l => l.Credit);
                return preview;
            }

            var rows = (await _connection.QueryAsync<(long AccountId, string Code, string Name, decimal NetBalance)>(
                @"SELECT a.AccountId, a.Code, a.Name, SUM(jl.Debit - jl.Credit) AS NetBalance
                  FROM dbo.JournalLines jl
                  JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
                  JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
                  WHERE j.OccurredAt < DATEADD(DAY, 1, @endDate)
                    AND a.AccountType IN (1, 2, 3)
                  GROUP BY a.AccountId, a.Code, a.Name
                  HAVING ABS(SUM(jl.Debit - jl.Credit)) > 0.01
                  ORDER BY a.Code",
                new { endDate = source.EndDate })).ToList();

            var lines = new List<OpeningJournalLine>();
            var lineNo = 1;
            foreach (var r in rows)
            {
                lines.Add(new OpeningJournalLine
                {
                    LineNo = lineNo++,
                    AccountId = r.AccountId,
                    AccountCode = r.Code,
                    AccountName = r.Name,
                    Debit = r.NetBalance > 0 ? r.NetBalance : 0m,
                    Credit = r.NetBalance < 0 ? -r.NetBalance : 0m
                });
            }

            var preview2 = new OpeningPreview
            {
                SourceFiscalYear = source,
                TargetFiscalYear = target,
                JournalLines = lines
            };
            preview2.TotalDebit = preview2.JournalLines.Sum(l => l.Debit);
            preview2.TotalCredit = preview2.JournalLines.Sum(l => l.Credit);
            return preview2;
        }

        public async Task<int> RevertClosingAsync(int fiscalYearId)
        {
            var fy = await _repo.GetByIdAsync(fiscalYearId);
            if (fy == null) throw new InvalidOperationException("سال مالی یافت نشد");
            if (!fy.IsClosed) throw new InvalidOperationException("سال مالی بسته نیست");

            var nextYear = await _repo.GetByYearCodeAsync(fy.YearCode + 1);
            if (nextYear == null)
                throw new InvalidOperationException($"سال مالی {fy.YearCode + 1} یافت نشد.");

            // B3: اگر سال مقصد خودش بسته شده باشد، برگشتِ این سال وضعیت حساب‌ها را خراب می‌کند.
            if (nextYear.IsClosed)
                throw new InvalidOperationException(
                    $"سال مالی {nextYear.YearCode} بسته است. برگشت انتقال مجاز نیست. " +
                    $"ابتدا سال {nextYear.YearCode} را بازگشایی کنید، سپس این عملیات را انجام دهید.");

            // B1: بدون سند انتقال، برگشت فقط پرچم‌ها را جابه‌جا می‌کند و پیام «موفق» گمراه‌کننده است.
            var openingId = await FindOpeningJournalIdAsync(nextYear.FiscalYearId, nextYear.YearCode);
            if (!openingId.HasValue)
                throw new InvalidOperationException(
                    $"سند انتقال (افتتاحیه) سال مالی {nextYear.YearCode} یافت نشد. " +
                    "برگشت انجام نشد تا وضعیت سال‌های مالی بدون حذفِ سند تغییر نکند.");

            var nextYearHasOtherJournals = await _connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM dbo.Journals j
                  WHERE j.FiscalYearId = @nextYearId AND j.IsDeleted = 0
                    AND j.JournalId <> @openingId",
                new { nextYearId = nextYear.FiscalYearId, openingId = openingId.Value });
            if (nextYearHasOtherJournals > 0)
                throw new InvalidOperationException($"سال مالی {nextYear.YearCode} دارای اسناد دیگری است. ابتدا آن اسناد را برگردانید.");

            var docNum = await _connection.QuerySingleAsync<int>(
                "SELECT ISNULL(DocumentNumber, 0) FROM dbo.Journals WHERE JournalId = @id",
                new { id = openingId.Value });

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync("UPDATE dbo.Journals SET IsDeleted = 1 WHERE JournalId = @id", new { id = openingId.Value }, tx);
                await _connection.ExecuteAsync(
                    @"DELETE hr FROM dbo.HouseholdReceipts hr
                      WHERE hr.ExternalRef = CONCAT('J:', @id)
                        AND NOT EXISTS (SELECT 1 FROM dbo.HouseholdReceiptAllocations WHERE HouseholdReceiptId = hr.HouseholdReceiptId)
                        AND NOT EXISTS (SELECT 1 FROM dbo.Transactions WHERE ParentReceiptId = hr.HouseholdReceiptId)",
                    new { id = openingId.Value }, tx);

                // B2: هم‌راستاسازی شمارندهٔ اسناد سال مقصد با همان فرمولی که
                // EnsureSerialCounterAsync برای ساخت شمارنده استفاده می‌کند (MAX روی همهٔ اسناد،
                // از جمله حذف‌شده‌ها) تا شمارهٔ سند هرگز دوباره استفاده نشود و شمارنده از حداکثرِ
                // موجود بالاتر نماند. اگر شمارنده پایین‌تر باشد دست نمی‌خورد.
                await _connection.ExecuteAsync(
                    @"UPDATE sc SET sc.LastDocumentNumber = x.MaxDoc
                      FROM dbo.SerialCounters sc
                      CROSS JOIN (
                          SELECT ISNULL(MAX(j.DocumentNumber), 0) AS MaxDoc
                          FROM dbo.Journals j WHERE j.FiscalYearId = @nextYearId
                      ) x
                      WHERE sc.FiscalYearId = @nextYearId AND sc.LastDocumentNumber > x.MaxDoc",
                    new { nextYearId = nextYear.FiscalYearId }, tx);

                await _repo.SaveBalancesAsync(nextYear.FiscalYearId, Enumerable.Empty<FiscalYearBalance>(), tx);

                fy.IsClosed = false;
                fy.IsOpen = true;
                fy.ClosedAt = null;
                fy.ClosedBy = null;
                await _repo.UpdateAsync(fy, tx);

                nextYear.IsOpen = false;
                await _repo.UpdateAsync(nextYear, tx);

                tx.Commit();
                _rangeProvider.Invalidate();
                return docNum;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        private static string BuildTransferNotes(int sourceYearCode, int targetYearCode)
            => OpeningJournal.Build(sourceYearCode, targetYearCode);

        private static void EnsureSeparateOpeningAllowed()
        {
            throw new InvalidOperationException(
                "ثبت جداگانه‌ی سند افتتاحیه مجاز نیست. " +
                "افتتاح سال مالی جدید فقط از طریق «تأیید و انتقال» در صفحه‌ی اختتامیه انجام می‌شود " +
                "و تنها یک سند انتقال برای سال مقصد ثبت می‌شود.");
        }

        private async Task<long?> FindOpeningJournalIdAsync(int fiscalYearId, int yearCode)
        {
            return await _connection.QueryFirstOrDefaultAsync<long?>(
                @"SELECT TOP 1 j.JournalId
                  FROM dbo.Journals j
                  WHERE j.FiscalYearId = @fiscalYearId AND j.IsDeleted = 0
                    AND (j.Notes LIKE @pattern
                         OR j.Notes = @legacy
                         OR j.Notes LIKE @legacyPattern
                         OR EXISTS (SELECT 1 FROM dbo.HouseholdReceipts hr
                                    WHERE hr.ExternalRef = CONCAT('J:', j.JournalId)
                                      AND hr.TransactionType = '2-5'))
                  ORDER BY CASE WHEN EXISTS (
                               SELECT 1 FROM dbo.HouseholdReceipts hr
                               WHERE hr.ExternalRef = CONCAT('J:', j.JournalId)
                                 AND hr.TransactionType = '2-5') THEN 0 ELSE 1 END,
                           j.JournalId DESC",
                new
                {
                    fiscalYearId,
                    pattern = OpeningJournal.PatternFor(yearCode),
                    legacy = yearCode.ToString(),
                    legacyPattern = $"سند افتتاحیه سال مالی {yearCode} |%"
                });
        }

        public async Task<int> ExecuteOpeningAsync(int sourceYearId, int targetYearId, string createdBy)
        {
            EnsureSeparateOpeningAllowed();

            var preview = await BuildOpeningPreviewAsync(sourceYearId, targetYearId);
            if (Math.Abs(preview.TotalDebit - preview.TotalCredit) > 0.01m)
                throw new InvalidOperationException("سند افتتاحیه نامتوازن است");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var occurredAt = preview.TargetFiscalYear.StartDate;
                var notes = BuildTransferNotes(preview.SourceFiscalYear.YearCode, preview.TargetFiscalYear.YearCode);
                var journalId = await _connection.QuerySingleAsync<long>(
                    @"INSERT INTO dbo.Journals (FiscalYearId, OccurredAt, CreatedAt, CreatedBy, Notes, Status)
                      VALUES (@FiscalYearId, @OccurredAt, GETUTCDATE(), @CreatedBy, @Notes, 3);
                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    new { FiscalYearId = targetYearId, OccurredAt = occurredAt, CreatedBy = createdBy, Notes = notes }, tx);

                foreach (var line in preview.JournalLines)
                {
                    await _connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, Debit, Credit, Description)
                          VALUES (@JournalId, @LineNo, @AccountId, @Debit, @Credit, @Description)",
                        new
                        {
                            JournalId = journalId,
                            LineNo = line.LineNo,
                            AccountId = line.AccountId,
                            line.Debit,
                            line.Credit,
                            Description = notes
                        }, tx);
                }

                await _connection.ExecuteAsync(
                    @"INSERT INTO dbo.HouseholdReceipts
                        (TargetHouseholdId, DocumentType, TransactionType, CounterpartyName,
                         AmountTotal, PayChannel, OccurredAt, CreatedBy, Status, Notes, ExternalRef)
                      VALUES
                        (NULL, 2, '2-5', NULL,
                         @AmountTotal, 3, @OccurredAt, @CreatedBy, 1, @Notes, @ExternalRef)",
                    new
                    {
                        AmountTotal = preview.TotalDebit,
                        OccurredAt = occurredAt,
                        CreatedBy = createdBy,
                        Notes = notes,
                        ExternalRef = $"J:{journalId}"
                    }, tx);

                var actualTotals = await _connection.QuerySingleAsync<(decimal Debit, decimal Credit)>(
                    "SELECT ISNULL(SUM(Debit),0) AS Debit, ISNULL(SUM(Credit),0) AS Credit FROM dbo.JournalLines WHERE JournalId = @journalId",
                    new { journalId }, tx);
                if (Math.Abs(actualTotals.Debit - actualTotals.Credit) > 0.01m)
                    throw new InvalidOperationException($"سند افتتاحیه بعد از درج نامتوازن شد (بدهکار {actualTotals.Debit:N0} ≠ بستانکار {actualTotals.Credit:N0}). تراکنش برگشت خورد.");

                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var balances = preview.JournalLines
                    .GroupBy(l => l.AccountId)
                    .Select(g => new FiscalYearBalance
                    {
                        AccountId = g.Key,
                        OpeningDebit = g.Sum(x => x.Debit),
                        OpeningCredit = g.Sum(x => x.Credit)
                    })
                    .ToList();
                await _repo.SaveBalancesAsync(targetYearId, balances, tx);

                var targetFiscalYearId = preview.TargetFiscalYear.FiscalYearId;
                await _journalsRepo.EnsureSerialCounterAsync(targetFiscalYearId, tx);
                var docNum = await _journalsRepo.GetNextDocumentNumberAsync(targetFiscalYearId, tx);
                await _journalsRepo.AssignDocumentNumberAsync(journalId, docNum, tx);

                tx.Commit();
                _rangeProvider.Invalidate();
                return docNum;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                await EnsureTriggersEnabledAsync();
                throw;
            }
        }

        public class ClosingPreview
        {
            public FiscalYear? FiscalYear { get; set; }
            public FiscalYear? NextFiscalYear { get; set; }
            public List<ClosingSummaryRow> SummaryRows { get; set; } = new();
            public List<ClosingJournalLine> JournalLines { get; set; } = new();
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
            public int UnfinalizedJournalCount { get; set; }
            public List<string> InactiveAccountsWithBalance { get; set; } = new();
        }

        public class ClosingSummaryRow
        {
            public int AccountType { get; set; }
            public long AccountId { get; set; }
            public string AccountCode { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public string TypeName { get; set; } = string.Empty;
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
            public decimal NetBalance { get; set; }
        }

        public class ClosingJournalLine
        {
            public int LineNo { get; set; }
            public long MoeenId { get; set; }
            public string MoeenCode { get; set; } = string.Empty;
            public string MoeenName { get; set; } = string.Empty;
            public long AccountId { get; set; }
            public string AccountCode { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public int Level { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
        }

        public class AccountDetailRow
        {
            public long AccountId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Level { get; set; }
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
        }

        public class OpeningPreview
        {
            public FiscalYear? SourceFiscalYear { get; set; }
            public FiscalYear? TargetFiscalYear { get; set; }
            public List<OpeningJournalLine> JournalLines { get; set; } = new();
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
        }

        public class OpeningJournalLine
        {
            public int LineNo { get; set; }
            public long AccountId { get; set; }
            public string AccountCode { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
        }
    }
}
