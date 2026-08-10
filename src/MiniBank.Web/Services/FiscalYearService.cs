using Dapper;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using System.Data;
using System.Globalization;

namespace MiniBank.Web.Services
{
    public class FiscalYearService
    {
        private readonly FiscalYearRepository _repo;
        private readonly JournalsRepository _journalsRepo;
        private readonly IDbConnection _connection;

        public FiscalYearService(FiscalYearRepository repo, JournalsRepository journalsRepo, IDbConnection connection)
        {
            _repo = repo;
            _journalsRepo = journalsRepo;
            _connection = connection;
        }

        public Task<IEnumerable<FiscalYear>> GetAllAsync() => _repo.GetAllAsync();
        public Task<FiscalYear?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
        public Task<FiscalYear?> GetByYearCodeAsync(int yearCode) => _repo.GetByYearCodeAsync(yearCode);

        public async Task<int> CreateAsync(FiscalYear model)
        {
            var existing = await _repo.GetByYearCodeAsync(model.YearCode);
            if (existing != null)
                throw new InvalidOperationException($"سال مالی {model.YearCode} قبلاً ثبت شده است");
            return await _repo.CreateAsync(model);
        }

        public async Task UpdateAsync(FiscalYear model)
        {
            var existing = await _repo.GetByIdAsync(model.FiscalYearId);
            if (existing == null)
                throw new InvalidOperationException("سال مالی یافت نشد");
            if (existing.IsClosed)
                throw new InvalidOperationException("سال مالی بسته شده قابل ویرایش نیست");
            await _repo.UpdateAsync(model);
        }

        public async Task DeleteAsync(int id)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new InvalidOperationException("سال مالی یافت نشد");
            await _repo.DeleteAsync(id);
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

            var accountRows = (await _connection.QueryAsync<(long AccountId, int AccountType, string TypeName, decimal TotalDebit, decimal TotalCredit)>(
                @"SELECT l1.AccountId, l1.AccountType, at.Name AS TypeName,
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
                  WHERE l1.Level = 1 AND l1.IsActive = 1
                  GROUP BY l1.AccountId, l1.AccountType, at.Name
                  ORDER BY l1.AccountType",
                new { start = fy.StartDate, end = fy.EndDate })).ToList();

            var typeGroups = accountRows
                .GroupBy(r => new { r.AccountType, r.TypeName })
                .Select(g =>
                {
                    var sumDebit = g.Sum(r => r.TotalDebit);
                    var sumCredit = g.Sum(r => r.TotalCredit);
                    var rawNet = sumDebit - sumCredit;
                    var balance = (g.Key.AccountType == 2 || g.Key.AccountType == 3 || g.Key.AccountType == 4) ? -rawNet : rawNet;
                    return new
                    {
                        g.Key.AccountType,
                        g.Key.TypeName,
                        TotalDebit = sumDebit,
                        TotalCredit = sumCredit,
                        NetBalance = balance
                    };
                })
                .OrderBy(x => x.AccountType)
                .ToList();

            foreach (var g in typeGroups)
            {
                preview.SummaryRows.Add(new ClosingSummaryRow
                {
                    AccountType = g.AccountType,
                    TypeName = g.TypeName,
                    TotalDebit = g.TotalDebit,
                    TotalCredit = g.TotalCredit,
                    NetBalance = g.NetBalance
                });
            }

            decimal incomeNet = typeGroups.Where(g => g.AccountType == 4).Sum(g => g.NetBalance);
            decimal expenseNet = typeGroups.Where(g => g.AccountType == 5).Sum(g => g.NetBalance);
            var profitLoss = incomeNet + expenseNet;
            var lineNo = 1;

            if (incomeNet != 0m)
            {
                preview.JournalLines.Add(new ClosingJournalLine
                {
                    LineNo = lineNo++,
                    AccountCode = "4101",
                    AccountName = "درآمد سود وام",
                    Description = "بستن حساب درآمدی",
                    Debit = incomeNet > 0m ? incomeNet : 0m,
                    Credit = incomeNet < 0m ? -incomeNet : 0m
                });
            }

            if (expenseNet != 0m)
            {
                preview.JournalLines.Add(new ClosingJournalLine
                {
                    LineNo = lineNo++,
                    AccountCode = "5101",
                    AccountName = "هزینه سود سپرده",
                    Description = "بستن حساب هزینه",
                    Debit = expenseNet > 0m ? expenseNet : 0m,
                    Credit = expenseNet < 0m ? -expenseNet : 0m
                });
            }

            if (profitLoss != 0m)
            {
                preview.JournalLines.Add(new ClosingJournalLine
                {
                    LineNo = lineNo++,
                    AccountCode = "3302",
                    AccountName = "سود (زیان) انباشته",
                    Description = "انتقال سود/زیان خالص به سود انباشته",
                    Debit = profitLoss < 0 ? -profitLoss : 0m,
                    Credit = profitLoss > 0 ? profitLoss : 0m
                });
            }

            preview.TotalDebit = preview.JournalLines.Sum(l => l.Debit);
            preview.TotalCredit = preview.JournalLines.Sum(l => l.Credit);
            return preview;
        }

        public async Task<int> ExecuteClosingAsync(int fiscalYearId, string createdBy)
        {
            var fy = await _repo.GetByIdAsync(fiscalYearId);
            if (fy == null) throw new InvalidOperationException("سال مالی یافت نشد");
            if (fy.IsClosed) throw new InvalidOperationException("سال مالی قبلاً بسته شده است");

            var preview = await BuildClosingPreviewAsync(fiscalYearId);
            if (preview.TotalDebit != preview.TotalCredit)
                throw new InvalidOperationException("سند اختتامیه نامتوازن است");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var occurredAt = fy.EndDate;
                var notes = $"اختتامیه سال مالی {fy.YearCode} | {fy.Title}";
                var journalId = await _connection.QuerySingleAsync<long>(
                    @"INSERT INTO dbo.Journals (OccurredAt, CreatedAt, CreatedBy, Notes)
                      VALUES (@OccurredAt, GETUTCDATE(), @CreatedBy, @Notes);
                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    new { OccurredAt = occurredAt, CreatedBy = createdBy, Notes = notes }, tx);

                foreach (var line in preview.JournalLines)
                {
                    var accountId = await _connection.QuerySingleOrDefaultAsync<long?>(
                        "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code = @code AND IsActive = 1 ORDER BY AccountId",
                        new { code = line.AccountCode }, tx);

                    if (!accountId.HasValue)
                    {
                        var parentId = await _connection.QuerySingleOrDefaultAsync<long?>(
                            "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code = LEFT(@code, 4) AND IsActive = 1 ORDER BY AccountId",
                            new { code = line.AccountCode }, tx);
                        accountId = parentId;
                    }

                    if (!accountId.HasValue) continue;

                    await _connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalLines (JournalId, LineNo, AccountId, Debit, Credit, Description)
                          VALUES (@JournalId, @LineNo, @AccountId, @Debit, @Credit, @Description)",
                        new
                        {
                            JournalId = journalId,
                            LineNo = line.LineNo,
                            AccountId = accountId.Value,
                            line.Debit,
                            line.Credit,
                            line.Description
                        }, tx);
                }

                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var docNum = await _journalsRepo.GetNextDocumentNumberAsync(fiscalYearId, tx);
                await _journalsRepo.AssignDocumentNumberAsync(journalId, docNum, tx);

                fy.IsClosed = true;
                fy.IsOpen = false;
                fy.ClosedAt = DateTime.UtcNow;
                fy.ClosedBy = createdBy;
                await _repo.UpdateAsync(fy);

                tx.Commit();
                return docNum;
            }
            catch
            {
                try { await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx); } catch { }
                try { await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx); } catch { }
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<OpeningPreview> BuildOpeningPreviewAsync(int sourceYearId, int targetYearId)
        {
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

            var nextYearBalances = await _connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(1) FROM dbo.FiscalYearBalances fyb JOIN dbo.FiscalYears fy ON fy.FiscalYearId = fyb.FiscalYearId WHERE fy.YearCode = @nextCode",
                new { nextCode = fy.YearCode + 1 });
            if (nextYearBalances > 0)
                throw new InvalidOperationException("سال مالی بعدی افتتاحیه خورده است. ابتدا افتتاحیه سال بعد را برگردانید.");

            var journal = await _connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT JournalId, DocumentNumber FROM dbo.vw_ApprovedJournals WHERE Notes LIKE @pattern ORDER BY JournalId DESC",
                new { pattern = $"اختتامیه سال مالی {fy.YearCode} |%" });

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                if (journal != null)
                {
                    long journalId = journal.JournalId;
                    await _connection.ExecuteAsync("UPDATE dbo.Journals SET IsDeleted = 1 WHERE JournalId = @id", new { id = journalId }, tx);
                }

                fy.IsClosed = false;
                fy.IsOpen = true;
                fy.ClosedAt = null;
                fy.ClosedBy = null;
                await _repo.UpdateAsync(fy);

                tx.Commit();
                return journal?.DocumentNumber ?? 0;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<int> ExecuteOpeningAsync(int sourceYearId, int targetYearId, string createdBy)
        {
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
                var notes = $"سند افتتاحیه سال مالی {preview.TargetFiscalYear.YearCode} | انتقال از سال {preview.SourceFiscalYear.YearCode}";
                var journalId = await _connection.QuerySingleAsync<long>(
                    @"INSERT INTO dbo.Journals (OccurredAt, CreatedAt, CreatedBy, Notes)
                      VALUES (@OccurredAt, GETUTCDATE(), @CreatedBy, @Notes);
                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    new { OccurredAt = occurredAt, CreatedBy = createdBy, Notes = notes }, tx);

                foreach (var line in preview.JournalLines)
                {
                    await _connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalLines (JournalId, LineNo, AccountId, Debit, Credit, Description)
                          VALUES (@JournalId, @LineNo, @AccountId, @Debit, @Credit, @Description)",
                        new
                        {
                            JournalId = journalId,
                            LineNo = line.LineNo,
                            AccountId = line.AccountId,
                            line.Debit,
                            line.Credit,
                            Description = $"انتقال مانده {line.AccountName}"
                        }, tx);
                }

                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var balances = preview.JournalLines.Select(l => new FiscalYearBalance
                {
                    AccountId = l.AccountId,
                    OpeningDebit = l.Debit,
                    OpeningCredit = l.Credit
                });
                await _repo.SaveBalancesAsync(targetYearId, balances);

                var targetFiscalYearId = preview.TargetFiscalYear.FiscalYearId;
                await _journalsRepo.EnsureSerialCounterAsync(targetFiscalYearId, tx);
                var docNum = await _journalsRepo.GetNextDocumentNumberAsync(targetFiscalYearId, tx);
                await _journalsRepo.AssignDocumentNumberAsync(journalId, docNum, tx);

                tx.Commit();
                return docNum;
            }
            catch
            {
                try { await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx); } catch { }
                try { await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx); } catch { }
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public class ClosingPreview
        {
            public FiscalYear? FiscalYear { get; set; }
            public List<ClosingSummaryRow> SummaryRows { get; set; } = new();
            public List<ClosingJournalLine> JournalLines { get; set; } = new();
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
        }

        public class ClosingSummaryRow
        {
            public int AccountType { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
            public decimal NetBalance { get; set; }
        }

        public class ClosingJournalLine
        {
            public int LineNo { get; set; }
            public string AccountCode { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
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
