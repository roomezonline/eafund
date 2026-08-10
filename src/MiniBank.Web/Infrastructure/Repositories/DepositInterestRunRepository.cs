using Dapper;
using System.Data;
using System.Data.Common;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public sealed class DepositInterestRunRepository
    {
        private readonly IDbConnection _db;
        public DepositInterestRunRepository(IDbConnection db) { _db = db; }

        public async Task<IReadOnlyList<DepositInterestRun>> GetRunsAsync(int take = 24)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT TOP (@take)
  RunId, PeriodStartDate, PostingOccurredAt, AnnualRatePercent, DaysInMonth,
  CreditsCutoffToExclusive, DebitsCutoffToExclusive,
  SumPrincipal, SumInterest, SumNet, SumProfit,
  MemberCount, NeutralCount, Notes, CreatedAt, CreatedBy
FROM dbo.DepositInterestRuns
ORDER BY PeriodStartDate DESC, RunId DESC";
            var rows = await _db.QueryAsync<DepositInterestRun>(sql, new { take });
            return rows.ToList();
        }

        public async Task<DepositInterestRun?> GetByPeriodStartDateAsync(DateTime periodStartDate)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT TOP 1
  RunId, PeriodStartDate, PostingOccurredAt, AnnualRatePercent, DaysInMonth,
  CreditsCutoffToExclusive, DebitsCutoffToExclusive,
  SumPrincipal, SumInterest, SumNet, SumProfit,
  MemberCount, NeutralCount, Notes, CreatedAt, CreatedBy
FROM dbo.DepositInterestRuns
WHERE PeriodStartDate = @periodStartDate";
            return await _db.QuerySingleOrDefaultAsync<DepositInterestRun>(sql, new { periodStartDate });
        }

        public async Task<IReadOnlyList<DepositInterestRunItem>> GetItemsAsync(long runId)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT
  ri.RunItemId, ri.RunId, ri.MemberId, ri.MemberCode, ri.MemberName,
  ri.NetPrincipal, ri.NetInterest, ri.TotalNet, ri.MonthProfit,
  ri.IsNeutral, ri.JournalId, j.DocumentNumber
FROM dbo.DepositInterestRunItems ri
LEFT JOIN dbo.vw_ApprovedJournals j ON j.JournalId = ri.JournalId
WHERE ri.RunId = @runId
ORDER BY ri.MemberCode ASC, ri.MemberId ASC";
            var rows = await _db.QueryAsync<DepositInterestRunItem>(sql, new { runId });
            return rows.ToList();
        }

        public async Task<long> CreateRunAsync(DepositInterestRun run, IDbTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.DepositInterestRuns
(
  PeriodStartDate, PostingOccurredAt, AnnualRatePercent, DaysInMonth,
  CreditsCutoffToExclusive, DebitsCutoffToExclusive,
  SumPrincipal, SumInterest, SumNet, SumProfit,
  MemberCount, NeutralCount,
  Notes, CreatedAt, CreatedBy
)
OUTPUT INSERTED.RunId
VALUES
(
  @PeriodStartDate, @PostingOccurredAt, @AnnualRatePercent, @DaysInMonth,
  @CreditsCutoffToExclusive, @DebitsCutoffToExclusive,
  @SumPrincipal, @SumInterest, @SumNet, @SumProfit,
  @MemberCount, @NeutralCount,
  @Notes, GETUTCDATE(), @CreatedBy
)";
            return await _db.QuerySingleAsync<long>(sql, new
            {
                PeriodStartDate = run.PeriodStartDate.Date,
                run.PostingOccurredAt,
                run.AnnualRatePercent,
                run.DaysInMonth,
                run.CreditsCutoffToExclusive,
                run.DebitsCutoffToExclusive,
                run.SumPrincipal,
                run.SumInterest,
                run.SumNet,
                run.SumProfit,
                run.MemberCount,
                run.NeutralCount,
                run.Notes,
                run.CreatedBy
            }, tx);
        }

        public async Task InsertItemsAsync(long runId, IEnumerable<DepositInterestRunItem> items, IDbTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.DepositInterestRunItems
(
  RunId, MemberId, MemberCode, MemberName,
  NetPrincipal, NetInterest, TotalNet, MonthProfit,
  IsNeutral, JournalId
)
VALUES
(
  @RunId, @MemberId, @MemberCode, @MemberName,
  @NetPrincipal, @NetInterest, @TotalNet, @MonthProfit,
  @IsNeutral, @JournalId
)";

            foreach (var it in items)
            {
                await _db.ExecuteAsync(sql, new
                {
                    RunId = runId,
                    it.MemberId,
                    it.MemberCode,
                    it.MemberName,
                    it.NetPrincipal,
                    it.NetInterest,
                    it.TotalNet,
                    it.MonthProfit,
                    it.IsNeutral,
                    it.JournalId
                }, tx);
            }
        }

        public async Task DeleteRunAsync(long runId, IDbTransaction tx)
        {
            await _db.ExecuteAsync("DELETE FROM dbo.DepositInterestRuns WHERE RunId=@runId", new { runId }, tx);
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
