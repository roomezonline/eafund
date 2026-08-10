using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    public class LoanRateService
    {
        private readonly LoanRatePlanRepository _plans;

        public LoanRateService(LoanRatePlanRepository plans)
        {
            _plans = plans;
        }

        public Task<IEnumerable<LoanRatePlan>> GetPlansAsync(bool? isActive = null)
            => _plans.GetAllAsync(isActive);

        public Task<LoanRatePlan?> GetByIdAsync(long planId)
            => _plans.GetByIdAsync(planId);

        public Task<LoanRatePlan?> GetActiveForDateAsync(DateTime date)
            => _plans.GetActiveForDateAsync(date);

        public async Task<LoanRatePlan?> GetActiveForDateOrLatestAsync(DateTime date)
        {
            var plan = await _plans.GetActiveForDateAsync(date);
            if (plan != null) return plan;
            return await _plans.GetLatestActiveAsync();
        }

        public Task DeleteAsync(long planId)
            => _plans.DeleteAsync(planId);

        public async Task<long> SaveAsync(LoanRatePlan plan)
        {
            ValidatePlan(plan);

            if (plan.IsActive)
                await AutoCloseOverlappingActivePlansAsync(plan);

            await EnsureNoDateOverlapAsync(plan);

            if (plan.PlanId <= 0)
                return await _plans.CreateAsync(plan);

            await _plans.UpdateAsync(plan);
            return plan.PlanId;
        }

        private static void ValidatePlan(LoanRatePlan plan)
        {
            if (plan.EffectiveFrom == default) throw new InvalidOperationException("تاریخ شروع الزامی است");
            if (plan.EffectiveTo.HasValue && plan.EffectiveTo.Value.Date < plan.EffectiveFrom.Date)
                throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد");
            if (plan.AnnualRatePercent <= 0) throw new InvalidOperationException("نرخ سود معتبر وارد کنید");
            if (plan.PenaltyAnnualRatePercent < 0) throw new InvalidOperationException("نرخ دیرکرد معتبر وارد کنید");
        }

        private async Task AutoCloseOverlappingActivePlansAsync(LoanRatePlan plan)
        {
            var existing = (await _plans.GetAllAsync(isActive: true)).ToList();

            static bool Overlaps(DateTime fromA, DateTime? toA, DateTime fromB, DateTime? toB)
            {
                var aStart = fromA.Date;
                var aEnd = (toA ?? DateTime.MaxValue).Date;
                var bStart = fromB.Date;
                var bEnd = (toB ?? DateTime.MaxValue).Date;
                return aStart <= bEnd && bStart <= aEnd;
            }

            foreach (var e in existing)
            {
                if (e.PlanId == plan.PlanId) continue;
                if (!Overlaps(plan.EffectiveFrom, plan.EffectiveTo, e.EffectiveFrom, e.EffectiveTo)) continue;

                if (e.EffectiveFrom.Date < plan.EffectiveFrom.Date)
                {
                    var newEnd = plan.EffectiveFrom.Date.AddDays(-1);
                    if (!e.EffectiveTo.HasValue || e.EffectiveTo.Value.Date > newEnd)
                    {
                        e.EffectiveTo = newEnd;
                        if (e.EffectiveTo.Value.Date < e.EffectiveFrom.Date)
                        {
                            e.IsActive = false;
                            e.EffectiveTo = null;
                        }
                        await _plans.UpdateAsync(e);
                    }
                    continue;
                }

                throw new InvalidOperationException("برای بازه زمانی انتخاب‌شده یک نرخ فعال دیگر وجود دارد");
            }
        }

        private async Task EnsureNoDateOverlapAsync(LoanRatePlan plan)
        {
            if (!plan.IsActive)
                return;

            var existing = (await _plans.GetAllAsync(isActive: true)).ToList();

            static bool Overlaps(DateTime fromA, DateTime? toA, DateTime fromB, DateTime? toB)
            {
                var aStart = fromA.Date;
                var aEnd = (toA ?? DateTime.MaxValue).Date;
                var bStart = fromB.Date;
                var bEnd = (toB ?? DateTime.MaxValue).Date;
                return aStart <= bEnd && bStart <= aEnd;
            }

            foreach (var e in existing)
            {
                if (e.PlanId == plan.PlanId) continue;
                if (Overlaps(plan.EffectiveFrom, plan.EffectiveTo, e.EffectiveFrom, e.EffectiveTo))
                    throw new InvalidOperationException("بازه زمانی نرخ با بازه‌های موجود هم‌پوشانی دارد");
            }
        }
    }
}
