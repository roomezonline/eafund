using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    public class DepositRateService
    {
        private readonly DepositRatePlanRepository _plans;
        private readonly DepositRateTierRepository _tiers;

        public DepositRateService(DepositRatePlanRepository plans, DepositRateTierRepository tiers)
        {
            _plans = plans;
            _tiers = tiers;
        }

        public Task<IEnumerable<DepositRatePlan>> GetPlansAsync(bool? isActive = null)
            => _plans.GetAllAsync(isActive);

        public async Task<(DepositRatePlan? Plan, List<DepositRateTier> Tiers)> GetPlanWithTiersAsync(long planId)
        {
            var plan = await _plans.GetByIdAsync(planId);
            var tiers = plan == null ? new List<DepositRateTier>() : (await _tiers.GetByPlanIdAsync(planId)).ToList();
            return (plan, tiers);
        }

        public Task DeleteAsync(long planId)
            => _plans.DeleteAsync(planId);

        public async Task<(DepositRatePlan Plan, List<DepositRateTier> Tiers)?> GetActivePlanAsync(DateTime targetDate)
        {
            var date = targetDate.Date;
            var plans = (await _plans.GetAllAsync(isActive: true)).ToList();

            var plan = plans
                .Where(p => p.IsActive
                    && p.EffectiveFrom.Date <= date
                    && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value.Date >= date))
                .OrderByDescending(p => p.EffectiveFrom)
                .ThenByDescending(p => p.PlanId)
                .FirstOrDefault();

            if (plan == null || plan.PlanId <= 0)
                return null;

            var tiers = plan.Mode == DepositRateMode.Tiered
                ? (await _tiers.GetByPlanIdAsync(plan.PlanId)).ToList()
                : new List<DepositRateTier>();

            return (plan, tiers);
        }

        public decimal ComputeRatePercentForAmount(DepositRatePlan plan, IReadOnlyList<DepositRateTier> tiers, decimal amount)
        {
            if (plan.Mode == DepositRateMode.Single)
                return plan.SingleRatePercent ?? 0m;

            if (plan.Mode != DepositRateMode.Tiered)
                return 0m;

            if (tiers == null || tiers.Count == 0)
                return 0m;

            var a = Math.Max(0m, amount);
            foreach (var t in tiers.OrderBy(x => x.OrderNo).ThenBy(x => x.FromAmount))
            {
                var to = t.ToAmount ?? decimal.MaxValue;
                if (a >= t.FromAmount && a <= to)
                    return t.RatePercent;
            }

            return tiers.OrderByDescending(x => x.OrderNo).ThenByDescending(x => x.FromAmount).First().RatePercent;
        }

        public async Task<long> SaveAsync(DepositRatePlan plan, IEnumerable<DepositRateTier>? tiers)
        {
            ValidatePlan(plan);

            if (plan.IsActive)
                await AutoCloseOverlappingActivePlansAsync(plan);

            if (plan.Mode == DepositRateMode.Tiered)
            {
                var normalized = NormalizeAndValidateTiers(tiers);
                await EnsureNoDateOverlapAsync(plan);

                if (plan.PlanId <= 0)
                {
                    var id = await _plans.CreateAsync(plan);
                    await _tiers.ReplaceForPlanAsync(id, normalized);
                    return id;
                }

                await _plans.UpdateAsync(plan);
                await _tiers.ReplaceForPlanAsync(plan.PlanId, normalized);
                return plan.PlanId;
            }

            plan.SingleRatePercent = plan.SingleRatePercent ?? throw new InvalidOperationException("نرخ تک‌نرخی الزامی است");
            if (plan.SingleRatePercent <= 0) throw new InvalidOperationException("نرخ باید بزرگ‌تر از صفر باشد");

            await EnsureNoDateOverlapAsync(plan);

            if (plan.PlanId <= 0)
                return await _plans.CreateAsync(plan);

            await _plans.UpdateAsync(plan);
            return plan.PlanId;
        }

        private async Task AutoCloseOverlappingActivePlansAsync(DepositRatePlan plan)
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

                // If the existing plan starts before the new plan, we can close it.
                if (e.EffectiveFrom.Date < plan.EffectiveFrom.Date)
                {
                    var newEnd = plan.EffectiveFrom.Date.AddDays(-1);
                    if (!e.EffectiveTo.HasValue || e.EffectiveTo.Value.Date > newEnd)
                    {
                        e.EffectiveTo = newEnd;

                        // If closing would invert the range, deactivate the existing plan instead.
                        if (e.EffectiveTo.Value.Date < e.EffectiveFrom.Date)
                        {
                            e.IsActive = false;
                            e.EffectiveTo = null;
                        }

                        await _plans.UpdateAsync(e);
                    }

                    continue;
                }

                // Existing active plan starts after (or same day). This would imply multiple active plans in the same window.
                throw new InvalidOperationException("برای بازه زمانی انتخاب‌شده یک نرخ فعال دیگر وجود دارد");
            }
        }

        private static void ValidatePlan(DepositRatePlan plan)
        {
            if (plan.EffectiveFrom == default) throw new InvalidOperationException("تاریخ شروع الزامی است");
            if (plan.EffectiveTo.HasValue && plan.EffectiveTo.Value.Date < plan.EffectiveFrom.Date)
                throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد");

            if (plan.Mode != DepositRateMode.Single && plan.Mode != DepositRateMode.Tiered)
                throw new InvalidOperationException("نوع نرخ معتبر نیست");
        }

        private static List<DepositRateTier> NormalizeAndValidateTiers(IEnumerable<DepositRateTier>? tiers)
        {
            var list = (tiers ?? Enumerable.Empty<DepositRateTier>()).Select((t, i) => new DepositRateTier
            {
                TierId = t.TierId,
                PlanId = t.PlanId,
                OrderNo = t.OrderNo > 0 ? t.OrderNo : (i + 1),
                FromAmount = t.FromAmount,
                ToAmount = t.ToAmount,
                RatePercent = t.RatePercent
            }).OrderBy(t => t.OrderNo).ThenBy(t => t.FromAmount).ToList();

            if (list.Count == 0)
                throw new InvalidOperationException("حداقل یک پله نرخ باید تعریف شود");

            foreach (var t in list)
            {
                if (t.FromAmount < 0) throw new InvalidOperationException("مبلغ شروع پله نمی‌تواند منفی باشد");
                if (t.ToAmount.HasValue && t.ToAmount.Value <= t.FromAmount)
                    throw new InvalidOperationException("مبلغ پایان باید بزرگ‌تر از مبلغ شروع باشد");
                if (t.RatePercent <= 0) throw new InvalidOperationException("نرخ پله باید بزرگ‌تر از صفر باشد");
            }

            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var aEnd = a.ToAmount ?? decimal.MaxValue;

                for (var j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    var bEnd = b.ToAmount ?? decimal.MaxValue;

                    var overlaps = a.FromAmount < bEnd && b.FromAmount < aEnd;
                    if (overlaps)
                        throw new InvalidOperationException("پله‌های مبلغ نباید هم‌پوشانی داشته باشند");
                }
            }

            return list;
        }

        private async Task EnsureNoDateOverlapAsync(DepositRatePlan plan)
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
