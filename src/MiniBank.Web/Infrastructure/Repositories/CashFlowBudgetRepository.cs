using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class CashFlowBudgetRepository
    {
        private readonly IDbConnection _connection;

        public CashFlowBudgetRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<List<CashFlowBudgetItem>> GetAllAsync()
        {
            var rows = await _connection.QueryAsync<CashFlowBudgetItem>(
                "SELECT * FROM dbo.CashFlowBudgetItems WHERE IsActive = 1 ORDER BY SortOrder");
            return rows.ToList();
        }

        public async Task UpdatePercentageAsync(int id, decimal percentage)
        {
            await _connection.ExecuteAsync(
                "UPDATE dbo.CashFlowBudgetItems SET Percentage = @percentage, UpdatedAt = GETUTCDATE() WHERE Id = @id",
                new { id, percentage });
        }

        public async Task UpdateAllAsync(List<CashFlowBudgetItem> items)
        {
            foreach (var item in items)
            {
                await _connection.ExecuteAsync(
                    "UPDATE dbo.CashFlowBudgetItems SET Percentage = @Percentage, Title = @Title, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
                    new { item.Id, item.Title, item.Percentage });
            }
        }
    }
}
