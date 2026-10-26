using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    public class BankCardService
    {
        private readonly BankCardRepository _repo;
        private readonly ILogger<BankCardService> _logger;

        public BankCardService(BankCardRepository repo, ILogger<BankCardService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BankCard>> GetAsync(bool? isActive = null, int top = 200)
        {
            try
            {
                return await _repo.GetAllAsync(isActive, top);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bank cards");
                throw;
            }
        }

        public async Task<BankCard?> GetByIdAsync(long id)
        {
            try { return await _repo.GetByIdAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Error getting bank card {Id}", id); throw; }
        }

        public async Task<long> CreateAsync(BankCard model)
        {
            try { return await _repo.CreateAsync(model); }
            catch (Exception ex) { _logger.LogError(ex, "Error creating bank card"); throw; }
        }

        public async Task UpdateAsync(BankCard model)
        {
            try { await _repo.UpdateAsync(model); }
            catch (Exception ex) { _logger.LogError(ex, "Error updating bank card {Id}", model.CardId); throw; }
        }

        public async Task DeleteAsync(long id)
        {
            try { await _repo.DeleteAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Error deleting bank card {Id}", id); throw; }
        }

        public async Task SetActiveAsync(long id, bool isActive)
        {
            try { await _repo.SetActiveAsync(id, isActive); }
            catch (Exception ex) { _logger.LogError(ex, "Error changing bank card state {Id}", id); throw; }
        }
    }
}
