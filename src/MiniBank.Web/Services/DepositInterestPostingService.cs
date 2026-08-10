using Dapper;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using System.Data;
using System.Data.Common;

namespace MiniBank.Web.Services
{
    public sealed class DepositInterestPostingService
    {
        private readonly IDbConnection _db;
        private readonly DepositInterestRunRepository _runsRepo;
        private readonly JournalsRepository _journalsRepo;
        private readonly JournalLinesRepository _journalLinesRepo;
        private readonly AccountsRepository _accountsRepo;

        public DepositInterestPostingService(
            IDbConnection db,
            DepositInterestRunRepository runsRepo,
            JournalsRepository journalsRepo,
            JournalLinesRepository journalLinesRepo,
            AccountsRepository accountsRepo)
        {
            _db = db;
            _runsRepo = runsRepo;
            _journalsRepo = journalsRepo;
            _journalLinesRepo = journalLinesRepo;
            _accountsRepo = accountsRepo;
        }

        public sealed record PostRequest(
            DateTime PeriodStartDate,
            DateTime PostingOccurredAt,
            decimal AnnualRatePercent,
            int DaysInMonth,
            DateTime CreditsCutoffToExclusive,
            DateTime DebitsCutoffToExclusive,
            IReadOnlyList<DepositInterestRunItem> Items,
            string Notes,
            string CreatedBy);

        public async Task<long> PostAsync(PostRequest req)
        {
            await EnsureOpenAsync();

            var existing = await _runsRepo.GetByPeriodStartDateAsync(req.PeriodStartDate.Date);
            if (existing != null)
                throw new UserFriendlyException("برای این ماه قبلاً ثبت انجام شده است. ابتدا حذف کنید سپس دوباره ثبت کنید.");

            var debitAccountId = await GetAccountIdByCodeAsync("5101");
            if (debitAccountId <= 0)
                throw new UserFriendlyException("حساب بدهکار 5101 یافت نشد.");

            var debitSubsidiaryAccountId = await GetAccountIdByCodeAsync("555555");
            if (debitSubsidiaryAccountId <= 0)
                throw new UserFriendlyException("حساب تفصیلی بدهکار با کد 555555 یافت نشد.");

            var creditParentAccountId = await GetAccountIdByCodeAsync("2202");
            if (creditParentAccountId <= 0)
                throw new UserFriendlyException("حساب بستانکار 2202 یافت نشد.");

            var memberCount = req.Items.Count;
            var neutralCount = req.Items.Count(x => x.IsNeutral);
            var sumP = req.Items.Sum(x => x.NetPrincipal);
            var sumI = req.Items.Sum(x => x.NetInterest);
            var sumN = req.Items.Sum(x => x.TotalNet);
            var sumProfit = req.Items.Sum(x => x.MonthProfit);

            using var tx = _db.BeginTransaction();
            try
            {
                await _db.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _db.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                // re-resolve account ids inside the transaction to avoid implicit commands without tx
                debitAccountId = await GetAccountIdByCodeAsync("5101", tx);
                if (debitAccountId <= 0)
                    throw new UserFriendlyException("حساب بدهکار 5101 یافت نشد.");

                debitSubsidiaryAccountId = await GetAccountIdByCodeAsync("555555", tx);
                if (debitSubsidiaryAccountId <= 0)
                    throw new UserFriendlyException("حساب تفصیلی بدهکار با کد 555555 یافت نشد.");

                creditParentAccountId = await GetAccountIdByCodeAsync("2202", tx);
                if (creditParentAccountId <= 0)
                    throw new UserFriendlyException("حساب بستانکار 2202 یافت نشد.");

                var run = new DepositInterestRun
                {
                    PeriodStartDate = req.PeriodStartDate.Date,
                    PostingOccurredAt = req.PostingOccurredAt,
                    AnnualRatePercent = req.AnnualRatePercent,
                    DaysInMonth = req.DaysInMonth,
                    CreditsCutoffToExclusive = req.CreditsCutoffToExclusive,
                    DebitsCutoffToExclusive = req.DebitsCutoffToExclusive,
                    SumPrincipal = sumP,
                    SumInterest = sumI,
                    SumNet = sumN,
                    SumProfit = sumProfit,
                    MemberCount = memberCount,
                    NeutralCount = neutralCount,
                    Notes = req.Notes,
                    CreatedBy = req.CreatedBy
                };

                var runId = await _runsRepo.CreateRunAsync(run, tx);

                var itemsToInsert = req.Items.Select(x => new DepositInterestRunItem
                {
                    RunId = runId,
                    RunItemId = 0,
                    MemberId = x.MemberId,
                    MemberCode = x.MemberCode,
                    MemberName = x.MemberName,
                    NetPrincipal = x.NetPrincipal,
                    NetInterest = x.NetInterest,
                    TotalNet = x.TotalNet,
                    MonthProfit = x.MonthProfit,
                    IsNeutral = x.IsNeutral,
                    JournalId = null
                }).ToList();

                // محاسبه کل مبلغ سود برای خط بدهکار
                var totalProfit = itemsToInsert.Sum(x => Math.Round(x.MonthProfit, 0, MidpointRounding.AwayFromZero));

                // ایجاد یک سند واحد برای کل عملیات
                var journalNotes = $"{req.Notes} | Run:{runId}";
                var journalId = await _journalsRepo.CreateManualJournalAsync(req.PostingOccurredAt, req.CreatedBy, journalNotes, tx);

                // خط ۱: بدهکار کل مبلغ به 5101/555555
                if (totalProfit > 0m)
                {
                    await _journalLinesRepo.AddLineAsync(
                        journalId: journalId,
                        lineNo: 1,
                        accountId: debitSubsidiaryAccountId,
                        parentAccountId: debitAccountId,
                        memberId: null,
                        debit: totalProfit,
                        credit: 0m,
                        description: req.Notes,
                        tx: tx);
                }

                var lineNo = 2;
                foreach (var it in itemsToInsert)
                {
                    var amount = Math.Round(it.MonthProfit, 0, MidpointRounding.AwayFromZero);
                    if (amount <= 0m)
                    {
                        it.JournalId = null;
                        continue;
                    }

                    var memberSubsidiaryAccountId = await _db.QuerySingleOrDefaultAsync<long?>(
                        @"SELECT TOP 1 AccountId FROM dbo.Accounts WHERE MemberId = @memberId AND IsActive = 1 ORDER BY AccountId",
                        new { memberId = it.MemberId }, tx);
                    if (!memberSubsidiaryAccountId.HasValue || memberSubsidiaryAccountId.Value <= 0)
                        throw new UserFriendlyException($"برای عضو {it.MemberName} کد تفضیلی فعال یافت نشد.");

                    await _journalLinesRepo.AddLineAsync(
                        journalId: journalId,
                        lineNo: lineNo,
                        accountId: memberSubsidiaryAccountId.Value,
                        parentAccountId: creditParentAccountId,
                        memberId: it.MemberId,
                        debit: 0m,
                        credit: amount,
                        description: $"{req.Notes} - {it.MemberName}",
                        tx: tx);

                    it.JournalId = journalId;
                    lineNo++;
                }

                await _runsRepo.InsertItemsAsync(runId, itemsToInsert, tx);

                await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                tx.Commit();
                return runId;
            }
            catch
            {
                try
                {
                    await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                    await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);
                }
                catch { }

                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public Task<IReadOnlyList<DepositInterestRun>> GetRunsAsync(int take = 24) => _runsRepo.GetRunsAsync(take);

        public Task<IReadOnlyList<DepositInterestRunItem>> GetItemsAsync(long runId) => _runsRepo.GetItemsAsync(runId);

        public async Task DeleteRunAsync(long runId)
        {
            await EnsureOpenAsync();

            using var tx = _db.BeginTransaction();
            try
            {
                await _db.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _db.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var journalIds = (await _db.QueryAsync<long?>(
                    "SELECT JournalId FROM dbo.DepositInterestRunItems WHERE RunId=@runId AND JournalId IS NOT NULL",
                    new { runId }, tx)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();

                foreach (var jid in journalIds)
                {
                    await _db.ExecuteAsync("DELETE FROM dbo.JournalLines WHERE JournalId=@jid", new { jid }, tx);
                    await _db.ExecuteAsync("DELETE FROM dbo.Journals WHERE JournalId=@jid", new { jid }, tx);
                }

                await _runsRepo.DeleteRunAsync(runId, tx);

                await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                tx.Commit();
            }
            catch
            {
                try
                {
                    await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                    await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);
                }
                catch { }

                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        private async Task<long> GetAccountIdByCodeAsync(string code)
        {
            const string sql = "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code=@code AND IsActive=1";
            var id = await _db.QuerySingleOrDefaultAsync<long?>(sql, new { code });
            return id ?? 0;
        }

        private async Task<long> GetAccountIdByCodeAsync(string code, IDbTransaction tx)
        {
            const string sql = "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code=@code AND IsActive=1";
            var id = await _db.QuerySingleOrDefaultAsync<long?>(sql, new { code }, tx);
            return id ?? 0;
        }

        private async Task EnsureOpenAsync()
        {
            if (_db.State != ConnectionState.Open)
            {
                if (_db is DbConnection dbc)
                    await dbc.OpenAsync();
                else
                    _db.Open();
            }
        }
    }
}
