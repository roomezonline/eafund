using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MiniBank.Web.Services
{
    public sealed class MemberFinancialSnapshotService
    {
        private readonly TransactionReportRepository _txReports;
        private readonly CreditHoldRepository _holds;
        private readonly LoanRepository _loans;
        private readonly LoanInstallmentRepository _installments;

        public MemberFinancialSnapshotService(
            TransactionReportRepository txReports,
            CreditHoldRepository holds,
            LoanRepository loans,
            LoanInstallmentRepository installments)
        {
            _txReports = txReports;
            _holds = holds;
            _loans = loans;
            _installments = installments;
        }

        public async Task<MemberFinancialSnapshot> GetSnapshotByMemberCodeAsync(string memberCode, IDbTransaction? tx = null)
        {
            if (string.IsNullOrWhiteSpace(memberCode))
                return new MemberFinancialSnapshot { MemberCode = string.Empty };

            var agg = await _txReports.GetMemberDepositAggBySubsidiaryCodeAsync(memberCode, tx);
            var lockDetails = (await _holds.GetActiveDetailsByMemberCodeAsync(memberCode, tx)).ToList();
            var locked = lockDetails.Sum(x => x.LockedAmount);
            var activeLoan = await GetActiveLoanSnapshotAsync(memberCode, tx);

            return new MemberFinancialSnapshot
            {
                MemberCode = memberCode,
                NetPrincipal = agg.PrincipalCredit - agg.PrincipalDebit,
                PrincipalWithdrawals = agg.PrincipalDebit,
                NetInterest = agg.InterestCredit - agg.InterestDebit,
                InterestWithdrawals = agg.InterestDebit,
                LockedAmount = locked,
                LockedItems = lockDetails.Select(x => new MemberFinancialSnapshot.LockedItem
                {
                    HoldId = x.HoldId,
                    LoanId = x.LoanId,
                    Role = x.Role,
                    LockedAmount = x.LockedAmount,
                    BorrowerMemberId = x.BorrowerMemberId,
                    BorrowerName = (x.BorrowerName ?? string.Empty).Trim()
                }).ToList(),
                ActiveLoan = activeLoan
            };
        }

        public async Task<IReadOnlyDictionary<string, MemberFinancialSnapshot>> GetSnapshotsByMemberCodesAsync(IEnumerable<string> memberCodes, IDbTransaction? tx = null)
        {
            if (memberCodes == null)
                return new Dictionary<string, MemberFinancialSnapshot>(StringComparer.OrdinalIgnoreCase);

            var codes = memberCodes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codes.Count == 0)
                return new Dictionary<string, MemberFinancialSnapshot>(StringComparer.OrdinalIgnoreCase);

            var aggs = await _txReports.GetMemberDepositAggBySubsidiaryCodesAsync(codes, tx);
            var lockedMap = await _holds.SumActiveLockedAmountByMemberCodesAsync(codes, tx);

            var dict = new Dictionary<string, MemberFinancialSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in codes)
            {
                aggs.TryGetValue(code, out var agg);
                lockedMap.TryGetValue(code, out var locked);

                agg ??= new TransactionReportRepository.MemberDepositAggDto();

                var activeLoan = await GetActiveLoanSnapshotAsync(code, tx);

                dict[code] = new MemberFinancialSnapshot
                {
                    MemberCode = code,
                    NetPrincipal = agg.PrincipalCredit - agg.PrincipalDebit,
                    PrincipalWithdrawals = agg.PrincipalDebit,
                    NetInterest = agg.InterestCredit - agg.InterestDebit,
                    InterestWithdrawals = agg.InterestDebit,
                    LockedAmount = locked,
                    ActiveLoan = activeLoan
                };
            }

            return dict;
        }

        public async Task<decimal> GetNetPrincipalAsync(string memberCode, IDbTransaction? tx = null)
            => (await GetSnapshotByMemberCodeAsync(memberCode, tx)).NetPrincipal;

        public async Task<decimal> GetNetInterestAsync(string memberCode, IDbTransaction? tx = null)
            => (await GetSnapshotByMemberCodeAsync(memberCode, tx)).NetInterest;

        public async Task<decimal> GetLockedAmountAsync(string memberCode, IDbTransaction? tx = null)
            => (await GetSnapshotByMemberCodeAsync(memberCode, tx)).LockedAmount;

        public async Task<decimal> GetAvailableCapitalAsync(string memberCode, IDbTransaction? tx = null)
            => (await GetSnapshotByMemberCodeAsync(memberCode, tx)).AvailableCapital;

        private async Task<MemberFinancialSnapshot.ActiveLoanSnapshot?> GetActiveLoanSnapshotAsync(string memberCode, IDbTransaction? tx)
        {
            if (string.IsNullOrWhiteSpace(memberCode))
                return null;

            var loan = await _loans.GetLatestActiveByMemberCodeAsync(memberCode, tx);
            if (loan == null || loan.LoanId <= 0)
                return null;

            var installments = (await _installments.GetByLoanIdAsync(loan.LoanId, tx)).ToList();
            if (installments.Count == 0)
            {
                return new MemberFinancialSnapshot.ActiveLoanSnapshot
                {
                    LoanId = loan.LoanId,
                    Principal = loan.Principal,
                    TotalInterest = 0m,
                    TotalPayable = loan.Principal,
                    RemainingPrincipal = loan.OutstandingPrincipal,
                    RemainingInterest = 0m,
                    TermMonths = loan.TermMonths,
                    EmiAmount = loan.EmiAmount
                };
            }

            var totalInterest = installments.Sum(i => i.InterestAmount);
            var totalPayable = loan.Principal + totalInterest;

            // Remaining is derived from installments (more reliable than OutstandingPrincipal alone)
            var remainingPrincipal = installments.Sum(i => Math.Max(0m, i.PrincipalAmount - i.PaidPrincipal));
            var remainingInterest = installments.Sum(i => Math.Max(0m, i.InterestAmount - i.PaidInterest));

            var totalInstallments = installments.Count;
            var paidInstallments = installments.Count(i => i.Status == LoanInstallmentStatus.Paid);
            var remainingInstallments = Math.Max(0, totalInstallments - paidInstallments);

            var firstDue = installments.Count > 0 ? installments[0].DueDate : (DateTime?)null;
            var lastDue = installments.Count > 0 ? installments[^1].DueDate : (DateTime?)null;
            var firstUnpaidDue = installments.FirstOrDefault(i => i.Status != LoanInstallmentStatus.Paid)?.DueDate;

            var current = installments
                .Where(i => (i.PrincipalAmount - i.PaidPrincipal) > 0m || (i.InterestAmount - i.PaidInterest) > 0m)
                .OrderBy(i => i.InstallmentNo)
                .FirstOrDefault();

            var overdueDays = 0;
            if (current?.DueDate != null)
            {
                var today = DateTime.Today;
                var due = current.DueDate.Date;
                overdueDays = Math.Max(0, (today - due).Days);
            }

            // Late fee is calculated on remaining amount of the current installment.
            var currentRemainingPrincipal = current != null ? Math.Max(0m, current.PrincipalAmount - current.PaidPrincipal) : 0m;
            var currentRemainingInterest = current != null ? Math.Max(0m, current.InterestAmount - current.PaidInterest) : 0m;
            var currentRemaining = currentRemainingPrincipal + currentRemainingInterest;
            var penaltyDailyRate = (loan.PenaltyAnnualRatePercent / 100m) / 365m;
            var penaltyDailyAmount = currentRemaining > 0m ? (currentRemaining * penaltyDailyRate) : 0m;
            var penaltyAmount = overdueDays > 0 && currentRemaining > 0m ? (penaltyDailyAmount * overdueDays) : 0m;

            var lastPaid = installments
                .Where(i => i.Status == LoanInstallmentStatus.Paid)
                .OrderByDescending(i => i.InstallmentNo)
                .FirstOrDefault();

            return new MemberFinancialSnapshot.ActiveLoanSnapshot
            {
                LoanId = loan.LoanId,
                DisbursedAt = loan.DisbursedAt,
                Principal = loan.Principal,
                TotalInterest = totalInterest,
                TotalPayable = totalPayable,

                RemainingPrincipal = remainingPrincipal,
                RemainingInterest = remainingInterest,

                TermMonths = loan.TermMonths,
                EmiAmount = loan.EmiAmount,

                TotalInstallments = totalInstallments,
                PaidInstallments = paidInstallments,
                RemainingInstallments = remainingInstallments,
                FirstDueDate = firstDue,
                LastDueDate = lastDue,
                FirstUnpaidDueDate = firstUnpaidDue,

                CurrentInstallmentNo = current?.InstallmentNo,
                CurrentInstallmentDueDate = current?.DueDate,
                CurrentInstallmentPrincipal = current?.PrincipalAmount ?? 0m,
                CurrentInstallmentInterest = current?.InterestAmount ?? 0m,
                CurrentInstallmentTotal = current != null ? (current.PrincipalAmount + current.InterestAmount) : 0m,

                CurrentInstallmentRemainingPrincipal = currentRemainingPrincipal,
                CurrentInstallmentRemainingInterest = currentRemainingInterest,

                CurrentInstallmentOverdueDays = overdueDays,
                PenaltyDailyRate = penaltyDailyRate,
                CurrentInstallmentPenaltyDailyAmount = penaltyDailyAmount,
                CurrentInstallmentPenaltyAmount = penaltyAmount,

                CurrentInstallmentAchievedPrincipal = current?.PaidPrincipal ?? 0m,
                CurrentInstallmentAchievedInterest = current?.PaidInterest ?? 0m,

                LastPaidInstallmentDueDate = lastPaid?.DueDate
            };
        }
    }
}
