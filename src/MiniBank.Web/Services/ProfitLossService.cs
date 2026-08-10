using Dapper;
using MiniBank.Web.Infrastructure;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Services
{
    public class ProfitLossService
    {
        private readonly IDbConnectionFactory _connFactory;
        private readonly TrialBalanceRepository _trialBalanceRepo;

        public ProfitLossService(
            IDbConnectionFactory connFactory,
            TrialBalanceRepository trialBalanceRepo)
        {
            _connFactory = connFactory;
            _trialBalanceRepo = trialBalanceRepo;
        }

        public class TafsiliChild
        {
            public long MoeenId { get; set; }
            public long AccountId { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
        }

        public class ProfitLossLineItem
        {
            public int LineNo { get; set; }
            public long AccountId { get; set; }
            public string AccountCode { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string ParentCode { get; set; } = "";
            public string ParentName { get; set; } = "";
            public string DisplaySubsidiary { get; set; } = "-";
            public string Description { get; set; } = "";
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public List<TafsiliChild>? TafsiliChildren { get; set; }
        }

        public class ProfitLossPreview
        {
            public DateTime FromDate { get; set; }
            public DateTime ToDate { get; set; }
            public List<ProfitLossLineItem> IncomeLines { get; set; } = new();
            public List<ProfitLossLineItem> ExpenseLines { get; set; } = new();
            public decimal TotalIncome { get; set; }
            public decimal TotalExpense { get; set; }
            public decimal NetProfitLoss { get; set; }
            public ProfitLossLineItem? TransferLine { get; set; }
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
            public bool IsValid => Math.Abs(TotalDebit - TotalCredit) <= 0.01m;
            public List<ProfitLossLineItem> AllLines { get; set; } = new();
        }

        public async Task<ProfitLossPreview> BuildPreviewAsync(DateTime from, DateTime to)
        {
            var allLevel3 = await _trialBalanceRepo.GetByLevelAsync(3, null, true, from, to);
            var incomeAccounts = allLevel3.Where(n => n.Code.StartsWith("4")).OrderBy(n => n.Code).ToList();
            var expenseAccounts = allLevel3.Where(n => n.Code.StartsWith("5")).OrderBy(n => n.Code).ToList();

            var preview = new ProfitLossPreview { FromDate = from, ToDate = to };
            var lineNo = 1;

            var parentIds = new HashSet<long>();
            var lineParentMap = new Dictionary<ProfitLossLineItem, long>();

            foreach (var acc in incomeAccounts)
            {
                var balance = acc.CreditBalance - acc.DebitBalance;
                if (balance <= 0) continue;
                var line = new ProfitLossLineItem
                {
                    LineNo = lineNo++,
                    AccountId = acc.AccountId,
                    AccountCode = acc.Code,
                    AccountName = acc.Name,
                    Debit = balance,
                    Description = $"بستن {acc.Name}"
                };
                if (acc.ParentAccountId.HasValue)
                {
                    parentIds.Add(acc.ParentAccountId.Value);
                    lineParentMap[line] = acc.ParentAccountId.Value;
                }
                preview.IncomeLines.Add(line);
                preview.TotalDebit += balance;
            }

            foreach (var acc in expenseAccounts)
            {
                var balance = acc.DebitBalance - acc.CreditBalance;
                if (balance <= 0) continue;
                var line = new ProfitLossLineItem
                {
                    LineNo = lineNo++,
                    AccountId = acc.AccountId,
                    AccountCode = acc.Code,
                    AccountName = acc.Name,
                    Credit = balance,
                    Description = $"بستن {acc.Name}"
                };
                if (acc.ParentAccountId.HasValue)
                {
                    parentIds.Add(acc.ParentAccountId.Value);
                    lineParentMap[line] = acc.ParentAccountId.Value;
                }
                preview.ExpenseLines.Add(line);
                preview.TotalCredit += balance;
            }

            preview.TotalIncome = preview.IncomeLines.Sum(l => l.Debit);
            preview.TotalExpense = preview.ExpenseLines.Sum(l => l.Credit);
            preview.NetProfitLoss = preview.TotalIncome - preview.TotalExpense;

            if (preview.NetProfitLoss != 0)
            {
                var desc = preview.NetProfitLoss > 0 ? "انتقال سود خالص" : "انتقال زیان خالص";
                preview.TransferLine = new ProfitLossLineItem
                {
                    LineNo = lineNo,
                    AccountCode = "3102",
                    AccountName = "سود (زیان) انباشته",
                    ParentCode = "3102",
                    ParentName = "سود (زیان) انباشته",
                    Description = desc,
                    Debit = preview.NetProfitLoss < 0 ? -preview.NetProfitLoss : 0,
                    Credit = preview.NetProfitLoss > 0 ? preview.NetProfitLoss : 0
                };
                preview.TotalDebit += preview.TransferLine.Debit;
                preview.TotalCredit += preview.TransferLine.Credit;
            }

            if (parentIds.Count > 0)
            {
                using var db = await _connFactory.CreateConnectionAsync();
                var parentLookup = (await db.QueryAsync<(long AccountId, string Code, string Name)>(
                    "SELECT AccountId, Code, Name FROM dbo.Accounts WHERE AccountId IN @ids",
                    new { ids = parentIds.ToList() }))
                    .ToDictionary(p => p.AccountId);

                foreach (var (line, pid) in lineParentMap)
                {
                    if (parentLookup.TryGetValue(pid, out var parent))
                    {
                        line.ParentCode = parent.Code;
                        line.ParentName = parent.Name;
                    }
                }
            }

            var level3Ids = allLevel3.Select(a => a.AccountId).ToHashSet();

            // Find the 3102 account ID and include it in the ID set for tafsili lookup
            long? transferAccountId = null;
            if (preview.TransferLine != null)
            {
                var plAccount = allLevel3.FirstOrDefault(a => a.Code == "3102");
                if (plAccount != null)
                    transferAccountId = plAccount.AccountId;
                else
                {
                    using var db = await _connFactory.CreateConnectionAsync();
                    transferAccountId = await db.QuerySingleOrDefaultAsync<long?>(
                        "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code = '3102' AND IsActive = 1 ORDER BY AccountId");
                }
                if (transferAccountId.HasValue)
                {
                    preview.TransferLine.AccountId = transferAccountId.Value;
                    level3Ids.Add(transferAccountId.Value);
                }
            }

            if (level3Ids.Count > 0)
            {
                using var db = await _connFactory.CreateConnectionAsync();
                var tafsiliRows = await db.QueryAsync<TafsiliChild>(
                    @"SELECT jl.ParentAccountId AS MoeenId,
                             a.AccountId, a.Code, a.Name,
                             ISNULL(SUM(jl.Debit), 0) AS Debit,
                             ISNULL(SUM(jl.Credit), 0) AS Credit
                      FROM dbo.JournalLines jl
                      INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                      INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                      WHERE jl.ParentAccountId IN @ids
                          AND a.Level = 4
                          AND a.IsActive = 1
                          AND j.OccurredAt >= @from
                          AND j.OccurredAt < DATEADD(DAY, 1, @to)
                      GROUP BY jl.ParentAccountId, a.AccountId, a.Code, a.Name
                      ORDER BY a.Code",
                    new { ids = level3Ids.ToList(), from, to });

                var level4ByParent = tafsiliRows
                    .GroupBy(r => r.MoeenId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var line in preview.IncomeLines.Concat(preview.ExpenseLines))
                {
                    if (level4ByParent.TryGetValue(line.AccountId, out var children) && children.Count > 0)
                    {
                        line.DisplaySubsidiary = string.Join(" ، ", children.Select(c => $"{c.Code} - {c.Name}"));
                        line.TafsiliChildren = children;
                    }
                }

                if (preview.TransferLine != null && transferAccountId.HasValue)
                {
                    if (level4ByParent.TryGetValue(transferAccountId.Value, out var transferChildren) && transferChildren.Count > 0)
                    {
                        preview.TransferLine.DisplaySubsidiary = string.Join(" ، ", transferChildren.Select(c => $"{c.Code} - {c.Name}"));
                        preview.TransferLine.TafsiliChildren = transferChildren;
                    }
                    else
                    {
                        // Fallback: query Level 4 children from Accounts hierarchy
                        var transferLevel4 = await db.QueryAsync<TafsiliChild>(
                            @"SELECT @parentId AS MoeenId,
                                     a.AccountId, a.Code, a.Name,
                                     ISNULL(SUM(jl.Debit), 0) AS Debit,
                                     ISNULL(SUM(jl.Credit), 0) AS Credit
                              FROM dbo.Accounts a
                              LEFT JOIN dbo.JournalLines jl ON jl.AccountId = a.AccountId
                              LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                                  AND j.OccurredAt >= @from AND j.OccurredAt < DATEADD(DAY, 1, @to)
                              WHERE a.ParentAccountId = @parentId AND a.Level = 4 AND a.IsActive = 1
                              GROUP BY a.AccountId, a.Code, a.Name
                              ORDER BY a.Code",
                            new { parentId = transferAccountId.Value, from, to });

                        var level4List = transferLevel4.ToList();
                        if (level4List.Count > 0)
                        {
                            preview.TransferLine.DisplaySubsidiary = string.Join(" ، ", level4List.Select(c => $"{c.Code} - {c.Name}"));
                            preview.TransferLine.TafsiliChildren = level4List;
                        }
                    }
                }
            }

            var allLines = new List<ProfitLossLineItem>();
            allLines.AddRange(preview.IncomeLines);
            allLines.AddRange(preview.ExpenseLines);
            if (preview.TransferLine != null)
                allLines.Add(preview.TransferLine);
            preview.AllLines = allLines;

            return preview;
        }

        public async Task<(bool Success, string Message, int? DocumentNumber)> PostClosingJournalAsync(
            DateTime from, DateTime to, string createdBy, bool isTemporary = true, string? notes = null)
        {
            var preview = await BuildPreviewAsync(from, to);
            if (!preview.IsValid)
                return (false, "سند سود و زیان نامتوازن است", null);

            var desc = notes ?? $"سند سود و زیان {from:yyyy/MM/dd} تا {to:yyyy/MM/dd}";
            var status = isTemporary ? (byte)1 : (byte)2;

            using var db = await _connFactory.CreateConnectionAsync();
            using var tx = db.BeginTransaction();

            try
            {
                var fy = await db.QuerySingleOrDefaultAsync<FiscalYear>(
                    "SELECT * FROM FiscalYears WHERE StartDate <= @date AND DATEADD(DAY, 1, EndDate) > @date AND IsOpen = 1",
                    new { date = to }, transaction: tx);
                if (fy == null)
                {
                    tx.Rollback();
                    return (false, $"برای تاریخ {to:yyyy/MM/dd} سال مالی بازی وجود ندارد", null);
                }

                await db.ExecuteAsync(sql: "DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await db.ExecuteAsync(sql: "DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var journalId = await db.QuerySingleAsync<long>(
                    @"INSERT INTO dbo.Journals (TransactionId, OccurredAt, CreatedAt, CreatedBy, Notes, Status)
                      VALUES (NULL, @occurredAt, GETUTCDATE(), @createdBy, @notes, @status);
                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    new { occurredAt = to, createdBy, notes = desc, status }, transaction: tx);

                async Task InsertJournalLineAsync(long accId, long? parentAccId, int ln, decimal debitAmt, decimal creditAmt, string dsc)
                {
                    if (parentAccId.HasValue)
                        await db.ExecuteAsync(
                            @"INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, ParentAccountId, Debit, Credit, Description)
                              VALUES (@JournalId, @LineNo, @AccountId, @ParentAccountId, @Debit, @Credit, @Description)",
                            new { JournalId = journalId, LineNo = ln, AccountId = accId, ParentAccountId = parentAccId.Value, Debit = debitAmt, Credit = creditAmt, Description = dsc },
                            transaction: tx);
                    else
                        await db.ExecuteAsync(
                            @"INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, Debit, Credit, Description)
                              VALUES (@JournalId, @LineNo, @AccountId, @Debit, @Credit, @Description)",
                            new { JournalId = journalId, LineNo = ln, AccountId = accId, Debit = debitAmt, Credit = creditAmt, Description = dsc },
                            transaction: tx);
                }

                foreach (var line in preview.IncomeLines.Concat(preview.ExpenseLines))
                {
                    if (line.TafsiliChildren != null && line.TafsiliChildren.Count > 0)
                    {
                        decimal allocatedChild = 0;
                        foreach (var child in line.TafsiliChildren)
                        {
                            if (line.Debit > 0)
                            {
                                var childAmt = child.Credit - child.Debit;
                                if (childAmt > 0.01m)
                                {
                                    await InsertJournalLineAsync(child.AccountId, line.AccountId, line.LineNo, childAmt, 0,
                                        $"بستن {child.Code}-{child.Name}");
                                    allocatedChild += childAmt;
                                }
                            }
                            else if (line.Credit > 0)
                            {
                                var childAmt = child.Debit - child.Credit;
                                if (childAmt > 0.01m)
                                {
                                    await InsertJournalLineAsync(child.AccountId, line.AccountId, line.LineNo, 0, childAmt,
                                        $"بستن {child.Code}-{child.Name}");
                                    allocatedChild += childAmt;
                                }
                            }
                        }
                        var remaining = (line.Debit > 0 ? line.Debit : line.Credit) - allocatedChild;
                        if (remaining > 0.01m)
                        {
                            await InsertJournalLineAsync(line.AccountId, null, line.LineNo,
                                line.Debit > 0 ? remaining : 0,
                                line.Credit > 0 ? remaining : 0,
                                $"بستن {line.AccountCode}-{line.AccountName}");
                        }
                    }
                    else
                    {
                        await InsertJournalLineAsync(line.AccountId, null, line.LineNo, line.Debit, line.Credit,
                            $"بستن {line.AccountCode}-{line.AccountName}");
                    }
                }

                if (preview.TransferLine != null)
                {
                    var plId = preview.TransferLine.AccountId;
                    if (plId == 0)
                    {
                        plId = await db.QuerySingleOrDefaultAsync<long?>(
                            "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code = '3102' AND IsActive = 1 ORDER BY AccountId",
                            transaction: tx) ?? 0;
                    }

                    if (plId == 0)
                    {
                        try { await db.ExecuteAsync(sql: "ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx); } catch { }
                        try { await db.ExecuteAsync(sql: "ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx); } catch { }
                        tx.Rollback();
                        return (false, "حساب ۳۱۰۲ یافت نشد", null);
                    }

                    if (preview.TransferLine.TafsiliChildren != null && preview.TransferLine.TafsiliChildren.Count > 0)
                    {
                        decimal allocatedChild = 0;
                        var xferDesc = preview.NetProfitLoss > 0 ? "انتقال سود خالص" : "انتقال زیان خالص";
                        var totalTransfer = preview.TransferLine.Debit > 0 ? preview.TransferLine.Debit : preview.TransferLine.Credit;
                        foreach (var child in preview.TransferLine.TafsiliChildren)
                        {
                            if (preview.TransferLine.Debit > 0)
                            {
                                var childAmt = child.Debit - child.Credit;
                                if (childAmt > 0.01m)
                                {
                                    await InsertJournalLineAsync(child.AccountId, plId, preview.TransferLine.LineNo, childAmt, 0,
                                        $"{xferDesc} ({child.Code}-{child.Name})");
                                    allocatedChild += childAmt;
                                }
                            }
                            else if (preview.TransferLine.Credit > 0)
                            {
                                var childAmt = child.Credit - child.Debit;
                                if (childAmt > 0.01m)
                                {
                                    await InsertJournalLineAsync(child.AccountId, plId, preview.TransferLine.LineNo, 0, childAmt,
                                        $"{xferDesc} ({child.Code}-{child.Name})");
                                    allocatedChild += childAmt;
                                }
                            }
                        }
                        if (allocatedChild < 0.01m)
                        {
                            var each = Math.Round(totalTransfer / preview.TransferLine.TafsiliChildren.Count, 2);
                            var remainderFromEqual = totalTransfer;
                            for (var i = 0; i < preview.TransferLine.TafsiliChildren.Count; i++)
                            {
                                var child = preview.TransferLine.TafsiliChildren[i];
                                var amt = i == preview.TransferLine.TafsiliChildren.Count - 1 ? remainderFromEqual : each;
                                if (amt > 0.01m)
                                {
                                    await InsertJournalLineAsync(child.AccountId, plId, preview.TransferLine.LineNo,
                                        preview.TransferLine.Debit > 0 ? amt : 0,
                                        preview.TransferLine.Credit > 0 ? amt : 0,
                                        $"{xferDesc} ({child.Code}-{child.Name})");
                                    remainderFromEqual -= amt;
                                }
                            }
                        }
                        else
                        {
                            var remaining = totalTransfer - allocatedChild;
                            if (remaining > 0.01m)
                            {
                                await InsertJournalLineAsync(plId, null, preview.TransferLine.LineNo,
                                    preview.TransferLine.Debit > 0 ? remaining : 0,
                                    preview.TransferLine.Credit > 0 ? remaining : 0, xferDesc);
                            }
                        }
                    }
                    else
                    {
                        await InsertJournalLineAsync(plId, null, preview.TransferLine.LineNo,
                            preview.TransferLine.Debit, preview.TransferLine.Credit,
                            preview.NetProfitLoss > 0 ? "انتقال سود خالص" : "انتقال زیان خالص");
                    }
                }

                await db.ExecuteAsync(sql: "ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await db.ExecuteAsync(sql: "ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                int? docNum = null;
                if (!isTemporary)
                {
                    var exists = await db.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM dbo.SerialCounters WHERE FiscalYearId = @id",
                        new { id = fy.FiscalYearId }, transaction: tx);

                    if (exists == 0)
                    {
                        var maxDoc = await db.QuerySingleAsync<int>(
                            "SELECT ISNULL(MAX(DocumentNumber), 0) FROM dbo.Journals WHERE FiscalYearId = @id AND DocumentNumber IS NOT NULL",
                            new { id = fy.FiscalYearId }, transaction: tx);
                        await db.ExecuteAsync(
                            "INSERT INTO dbo.SerialCounters (FiscalYearId, LastDocumentNumber) VALUES (@id, @max)",
                            new { id = fy.FiscalYearId, max = maxDoc }, transaction: tx);
                    }

                    docNum = await db.QuerySingleAsync<int>(
                        @"UPDATE dbo.SerialCounters
                          SET LastDocumentNumber = LastDocumentNumber + 1
                          OUTPUT INSERTED.LastDocumentNumber
                          WHERE FiscalYearId = @id",
                        new { id = fy.FiscalYearId }, transaction: tx);

                    await db.ExecuteAsync(
                        "UPDATE dbo.Journals SET DocumentNumber = @docNum WHERE JournalId = @journalId",
                        new { docNum, journalId }, transaction: tx);
                }

                tx.Commit();

                var msg = isTemporary
                    ? "سند سود و زیان به صورت موقت ثبت شد"
                    : $"سند سود و زیان با شماره {docNum} ثبت شد";
                return (true, msg, docNum);
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                return (false, $"خطا در ثبت سند: {ex.Message}", null);
            }
        }
    }
}
