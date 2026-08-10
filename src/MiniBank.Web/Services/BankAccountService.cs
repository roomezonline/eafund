using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    public class BankAccountService
    {
        private readonly BankAccountRepository _repo;
        private readonly ILogger<BankAccountService> _logger;

        public BankAccountService(BankAccountRepository repo, ILogger<BankAccountService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BankAccount>> GetAsync(string? keyword = null, bool? isActive = null, int top = 500)
        {
            try
            {
                var items = await _repo.GetAllAsync(keyword, isActive, top);
                return items.ToList().AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bank accounts");
                throw;
            }
        }

        public async Task<BankAccount?> GetByIdAsync(long id)
        {
            try { return await _repo.GetByIdAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Error getting bank account {Id}", id); throw; }
        }

        public async Task<long> CreateAsync(BankAccount model)
        {
            try { return await _repo.CreateAsync(model); }
            catch (Exception ex) { _logger.LogError(ex, "Error creating bank account"); throw; }
        }

        public async Task UpdateAsync(BankAccount model)
        {
            try { await _repo.UpdateAsync(model); }
            catch (Exception ex) { _logger.LogError(ex, "Error updating bank account {Id}", model.AccountId); throw; }
        }

        public async Task DeleteAsync(long id)
        {
            try { await _repo.DeleteAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Error deleting bank account {Id}", id); throw; }
        }

        public async Task ToggleActiveAsync(long id, bool isActive)
        {
            var item = await GetByIdAsync(id) ?? throw new InvalidOperationException($"Bank account {id} not found");
            item.IsActive = isActive;
            await UpdateAsync(item);
        }
    }
}
