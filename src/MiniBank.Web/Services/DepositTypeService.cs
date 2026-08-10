using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    public class DepositTypeService
    {
        private readonly DepositTypeRepository _types;
        private readonly DepositInterestRateRepository _rates;
        private readonly ILogger<DepositTypeService> _logger;

        public DepositTypeService(DepositTypeRepository types, DepositInterestRateRepository rates, ILogger<DepositTypeService> logger)
        {
            _types = types;
            _rates = rates;
            _logger = logger;
        }

        public Task<IEnumerable<DepositType>> GetAsync(string? keyword = null, bool? isActive = null, int limit = 500)
            => _types.GetAllAsync(keyword, isActive, limit);

        public Task<DepositType?> GetByIdAsync(long id) => _types.GetByIdAsync(id);

        public Task<long> CreateAsync(DepositType model) => _types.CreateAsync(model);

        public Task UpdateAsync(DepositType model) => _types.UpdateAsync(model);

        public Task DeleteAsync(long id) => _types.DeleteAsync(id);

        // Rates
        public Task<IEnumerable<DepositInterestRate>> GetRatesAsync(long depositTypeId) => _rates.GetByDepositTypeAsync(depositTypeId);

        // Bulk load rates for multiple deposit types (solves N+1 problem)
        public async Task<Dictionary<long, List<DepositInterestRate>>> GetRatesForTypesAsync(IEnumerable<long> depositTypeIds)
        {
            var allRates = await _rates.GetByDepositTypesAsync(depositTypeIds);
            return allRates.GroupBy(r => r.DepositTypeId)
                          .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EffectiveFrom).ToList());
        }

        public Task<long> AddRateAsync(DepositInterestRate rate) => _rates.CreateAsync(rate);

        public Task UpdateRateAsync(DepositInterestRate rate) => _rates.UpdateAsync(rate);

        public Task DeleteRateAsync(long rateId) => _rates.DeleteAsync(rateId);

        public async Task ToggleActiveAsync(long id, bool isActive)
        {
            var item = await GetByIdAsync(id) ?? throw new InvalidOperationException($"DepositType {id} not found");
            item.IsActive = isActive;
            await UpdateAsync(item);
        }

        public async Task ToggleEligibleForLoanAsync(long id, bool eligible)
        {
            var item = await GetByIdAsync(id) ?? throw new InvalidOperationException($"DepositType {id} not found");
            item.EligibleForLoan = eligible;
            await UpdateAsync(item);
        }

        // Rate selection helpers
        // Returns the rate active on targetDate: EffectiveFrom <= targetDate <= EffectiveTo (or open-ended)
        public async Task<DepositInterestRate?> GetActiveRateAsync(long depositTypeId, DateTime? targetDate = null)
        {
            var date = (targetDate ?? DateTime.UtcNow).Date;
            var rates = (await _rates.GetByDepositTypeAsync(depositTypeId)).ToList();
            return rates
                .Where(r => r.EffectiveFrom.Date <= date && (!r.EffectiveTo.HasValue || r.EffectiveTo.Value.Date >= date))
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefault();
        }

        // Returns the next planned rate strictly after targetDate (based on EffectiveFrom)
        public async Task<DepositInterestRate?> GetNextRateAsync(long depositTypeId, DateTime? targetDate = null)
        {
            var date = (targetDate ?? DateTime.UtcNow).Date;
            var rates = (await _rates.GetByDepositTypeAsync(depositTypeId)).ToList();
            return rates
                .Where(r => r.EffectiveFrom.Date > date)
                .OrderBy(r => r.EffectiveFrom)
                .FirstOrDefault();
        }

        // Business rules: prevent overlapping periods; auto-close previous open-ended period when adding a new later period
        public async Task<long> AddOrUpdateRateWithRulesAsync(DepositInterestRate model, bool isNew)
        {
            if (model.EffectiveTo.HasValue && model.EffectiveTo.Value.Date < model.EffectiveFrom.Date)
                throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد");

            var existing = (await _rates.GetByDepositTypeAsync(model.DepositTypeId)).OrderBy(r => r.EffectiveFrom).ToList();

            // Helper for overlap detection (inclusive ranges)
            static bool Overlaps(DateTime fromA, DateTime? toA, DateTime fromB, DateTime? toB)
            {
                var aStart = fromA.Date;
                var aEnd = (toA ?? DateTime.MaxValue.Date);
                var bStart = fromB.Date;
                var bEnd = (toB ?? DateTime.MaxValue.Date);
                return aStart <= bEnd && bStart <= aEnd;
            }

            if (isNew)
            {
                // If there is an open-ended last period and new starts later, close the last period to day before new
                var last = existing.LastOrDefault();
                if (last != null && !last.EffectiveTo.HasValue && last.EffectiveFrom.Date <= model.EffectiveFrom.Date)
                {
                    // Close last day before new starts
                    last.EffectiveTo = model.EffectiveFrom.Date.AddDays(-1);
                    await _rates.UpdateAsync(last);
                    // refresh existing for overlap check
                    existing = (await _rates.GetByDepositTypeAsync(model.DepositTypeId)).OrderBy(r => r.EffectiveFrom).ToList();
                }

                // Now ensure no overlaps remain
                foreach (var r in existing)
                {
                    if (Overlaps(model.EffectiveFrom, model.EffectiveTo, r.EffectiveFrom, r.EffectiveTo))
                        throw new InvalidOperationException("بازه زمانی نرخ با بازه‌های موجود هم‌پوشانی دارد");
                }

                return await _rates.CreateAsync(model);
            }
            else
            {
                // Update mode: ensure no overlap with others (exclude itself)
                foreach (var r in existing)
                {
                    if (r.RateId == model.RateId) continue;
                    if (Overlaps(model.EffectiveFrom, model.EffectiveTo, r.EffectiveFrom, r.EffectiveTo))
                        throw new InvalidOperationException("بازه زمانی نرخ با بازه‌های موجود هم‌پوشانی دارد");
                }
                await _rates.UpdateAsync(model);
                return model.RateId;
            }
        }
    }
}
