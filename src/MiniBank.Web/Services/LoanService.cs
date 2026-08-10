using Dapper;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using MiniBank.Web.Components.Extensions;
using System.Data;
using System.Globalization;

namespace MiniBank.Web.Services
{
    public class LoanService
    {
        private readonly IDbConnection _connection;
        private readonly LoanRepository _loans;
        private readonly LoanInstallmentRepository _installments;
        private readonly LoanGuaranteeRepository _guarantees;
        private readonly CreditHoldRepository _holds;
        private readonly LoanPaymentRepository _payments;
        private readonly LoanRatePlanRepository _loanRates;
        private readonly LoanCalculatorService _calculator;
        private readonly TransactionReportRepository _txReports;
        private readonly MemberFinancialSnapshotService _memberFinancial;
        private readonly JournalsRepository _journalsRepo;
        private readonly JournalLinesRepository _journalLinesRepo;
        private readonly AccountsRepository _accountsRepo;

        private static (decimal RemainingPrincipal, decimal RemainingInterest, decimal DueInterest, decimal Penalty) ComputePrepayRequiredAmounts(
            Loan loan,
            IReadOnlyList<LoanInstallment> installments,
            DateTime today)
        {
            var list = (installments ?? Array.Empty<LoanInstallment>()).OrderBy(x => x.InstallmentNo).ToList();

            var remainingPrincipal = list.Sum(x => Math.Max(0m, x.PrincipalAmount - x.PaidPrincipal));
            var remainingInterest = list.Sum(x => Math.Max(0m, x.InterestAmount - x.PaidInterest));

            var pc = new PersianCalendar();
            var currentPy = pc.GetYear(today);
            var currentPm = pc.GetMonth(today);
            var dueInterest = list
                .Where(x => {
                    var iy = pc.GetYear(x.DueDate);
                    var im = pc.GetMonth(x.DueDate);
                    return iy < currentPy || (iy == currentPy && im <= currentPm);
                })
                .Sum(x => Math.Max(0m, x.InterestAmount - x.PaidInterest));

            var penaltyDailyRate = (loan.PenaltyAnnualRatePercent / 100m) / 365m;
            var penalty = 0m;
            foreach (var it in list)
            {
                var remP = Math.Max(0m, it.PrincipalAmount - it.PaidPrincipal);
                var remI = Math.Max(0m, it.InterestAmount - it.PaidInterest);
                var remainingBase = remP + remI;
                if (remainingBase <= 0m)
                    continue;

                var due = it.DueDate.Date;
                if (due >= today.Date)
                    continue;

                var overdueDays = Math.Max(0, (today.Date - due).Days);
                if (overdueDays <= 0)
                    continue;

                penalty += remainingBase * penaltyDailyRate * overdueDays;
            }

            remainingPrincipal = Math.Round(remainingPrincipal, 0, MidpointRounding.AwayFromZero);
            remainingInterest = Math.Round(remainingInterest, 0, MidpointRounding.AwayFromZero);
            dueInterest = Math.Round(dueInterest, 0, MidpointRounding.AwayFromZero);
            penalty = Math.Round(penalty, 0, MidpointRounding.AwayFromZero);

            return (remainingPrincipal, remainingInterest, dueInterest, penalty);
        }

        public LoanService(
            IDbConnection connection,
            LoanRepository loans,
            LoanInstallmentRepository installments,
            LoanGuaranteeRepository guarantees,
            CreditHoldRepository holds,
            LoanPaymentRepository payments,
            LoanRatePlanRepository loanRates,
            LoanCalculatorService calculator,
            TransactionReportRepository txReports,
            MemberFinancialSnapshotService memberFinancial,
            JournalsRepository journalsRepo,
            JournalLinesRepository journalLinesRepo,
            AccountsRepository accountsRepo)
        {
            _connection = connection;
            _loans = loans;
            _installments = installments;
            _guarantees = guarantees;
            _holds = holds;
            _payments = payments;
            _loanRates = loanRates;
            _calculator = calculator;
            _txReports = txReports;
            _memberFinancial = memberFinancial;
            _journalsRepo = journalsRepo;
            _journalLinesRepo = journalLinesRepo;
            _accountsRepo = accountsRepo;
        }

        public sealed class DisbursementJournalPreview
        {
            public long LoanId { get; set; }
            public long MemberId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public decimal Principal { get; set; }
            public decimal Interest { get; set; }
            public decimal Total => Principal + Interest;

            public sealed class Line
            {
                public int LineNo { get; set; }
                public string ParentCode { get; set; } = string.Empty;
                public string ParentName { get; set; } = string.Empty;
                public string AccountCode { get; set; } = string.Empty;
                public string AccountName { get; set; } = string.Empty;
                public string DisplaySubsidiary { get; set; } = string.Empty;
                public string Title { get; set; } = string.Empty;
                public decimal Debit { get; set; }
                public decimal Credit { get; set; }
            }

            public List<Line> Lines { get; set; } = new();
        }

        public sealed class PrepayCashJournalPreview
        {
            public long LoanId { get; set; }
            public long MemberId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public decimal TotalPayable { get; set; }
            public decimal RemainingPrincipal { get; set; }
            public decimal CurrentInstallmentInterest { get; set; }

            public List<DisbursementJournalPreview.Line> Lines { get; set; } = new();
        }

        public async Task<DisbursementJournalPreview> BuildDisbursementJournalPreviewAsync(long loanId)
        {
            if (loanId <= 0)
                throw new InvalidOperationException("شناسه وام معتبر نیست");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var loan = await _loans.GetByIdAsync(loanId);
            if (loan == null)
                throw new InvalidOperationException("وام یافت نشد");

            var interest = await ComputeLoanInterestAsync(loan, null);
            var memberName = await ResolveMemberNameAsync(loan.MemberId, null);

            var memberSubsidiary = await _accountsRepo.GetSubsidiaryByMemberIdAsync(loan.MemberId);
            if (memberSubsidiary == null || memberSubsidiary.AccountId <= 0 || string.IsNullOrWhiteSpace(memberSubsidiary.Code))
                throw new InvalidOperationException("کد تفصیلی عضو وام‌گیرنده یافت نشد");

            var parent1301 = await GetAccountInfoByCodeAsync("1301", null);
            var parent1302 = await GetAccountInfoByCodeAsync("1302", null);
            var parent2301 = await GetAccountInfoByCodeAsync("2301", null);
            var acc230101 = await GetAccountInfoByCodeAsync("230101", null);
            var parent1201 = await GetAccountInfoByCodeAsync("1201", null);
            var acc120101 = await GetAccountInfoByCodeAsync("120101", null);

            if (parent1301.AccountId <= 0) throw new InvalidOperationException("حساب 1301 یافت نشد");
            if (parent1302.AccountId <= 0) throw new InvalidOperationException("حساب 1302 یافت نشد");
            if (parent2301.AccountId <= 0) throw new InvalidOperationException("حساب 2301 یافت نشد");
            if (acc230101.AccountId <= 0) throw new InvalidOperationException("حساب 230101 یافت نشد");
            if (parent1201.AccountId <= 0) throw new InvalidOperationException("حساب 1201 یافت نشد");
            if (acc120101.AccountId <= 0) throw new InvalidOperationException("حساب 120101 یافت نشد");

            var preview = new DisbursementJournalPreview
            {
                LoanId = loan.LoanId,
                MemberId = loan.MemberId,
                MemberName = memberName,
                Principal = Math.Round(loan.Principal, 0, MidpointRounding.AwayFromZero),
                Interest = Math.Round(interest, 0, MidpointRounding.AwayFromZero)
            };

            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 1,
                ParentCode = parent1301.Code,
                ParentName = parent1301.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "اصل وام (بدهکار)",
                Debit = preview.Principal,
                Credit = 0m
            });
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 2,
                ParentCode = parent1302.Code,
                ParentName = parent1302.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "سود وام (بدهکار)",
                Debit = preview.Interest,
                Credit = 0m
            });
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 3,
                ParentCode = parent2301.Code,
                ParentName = parent2301.Name,
                AccountCode = acc230101.Code,
                AccountName = acc230101.Name,
                DisplaySubsidiary = $"{acc230101.Code} - {acc230101.Name}",
                Title = "سود وام (بستانکار)",
                Debit = 0m,
                Credit = preview.Interest
            });
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 4,
                ParentCode = parent1201.Code,
                ParentName = parent1201.Name,
                AccountCode = acc120101.Code,
                AccountName = acc120101.Name,
                DisplaySubsidiary = $"{acc120101.Code} - {acc120101.Name}",
                Title = "اصل وام (بستانکار)",
                Debit = 0m,
                Credit = preview.Principal
            });

            return preview;
        }

        public async Task<PrepayCashJournalPreview> BuildPrepayCashJournalPreviewAsync(long loanId, decimal totalPayable)
        {
            if (loanId <= 0)
                throw new InvalidOperationException("شناسه وام معتبر نیست");
            if (totalPayable <= 0)
                throw new InvalidOperationException("مبلغ قابل پرداخت معتبر نیست");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var loan = await _loans.GetByIdAsync(loanId);
            if (loan == null)
                throw new InvalidOperationException("وام یافت نشد");

            var inst = (await _installments.GetByLoanIdAsync(loanId)).OrderBy(x => x.InstallmentNo).ToList();
            var (remainingPrincipal, remainingInterest, dueInterest, penalty) = ComputePrepayRequiredAmounts(loan, inst, DateTime.Today);

            var required = Math.Round(remainingPrincipal + dueInterest, 0, MidpointRounding.AwayFromZero);
            totalPayable = Math.Round(totalPayable, 0, MidpointRounding.AwayFromZero);
            if (totalPayable != required)
                throw new InvalidOperationException($"مبلغ قابل پرداخت باید برابر {required:N0} باشد (اصل مانده + سود اقساط سررسیدشده)");

            var parent1201 = await GetAccountInfoByCodeAsync("1201", null);
            var acc120101 = await GetAccountInfoByCodeAsync("120101", null);
            var parent2202 = await GetAccountInfoByCodeAsync("2202", null);
            var parent2301 = await GetAccountInfoByCodeAsync("2301", null);
            var acc230101 = await GetAccountInfoByCodeAsync("230101", null);
            var parent1301 = await GetAccountInfoByCodeAsync("1301", null);
            var parent1302 = await GetAccountInfoByCodeAsync("1302", null);
            var parent4101 = await GetAccountInfoByCodeAsync("4101", null);
            var parent4102 = await GetAccountInfoByCodeAsync("4102", null);
            var acc444444 = await GetAccountInfoByCodeAsync("444444", null);

            if (parent1201.AccountId <= 0) throw new InvalidOperationException("حساب 1201 یافت نشد");
            if (acc120101.AccountId <= 0) throw new InvalidOperationException("حساب 120101 یافت نشد");
            if (parent2202.AccountId <= 0) throw new InvalidOperationException("حساب 2202 یافت نشد");
            if (parent2301.AccountId <= 0) throw new InvalidOperationException("حساب 2301 یافت نشد");
            if (acc230101.AccountId <= 0) throw new InvalidOperationException("حساب 230101 یافت نشد");
            if (parent1301.AccountId <= 0) throw new InvalidOperationException("حساب 1301 یافت نشد");
            if (parent1302.AccountId <= 0) throw new InvalidOperationException("حساب 1302 یافت نشد");
            if (parent4101.AccountId <= 0) throw new InvalidOperationException("حساب 4101 یافت نشد");
            if (parent4102.AccountId <= 0) throw new InvalidOperationException("حساب 4102 یافت نشد");
            if (acc444444.AccountId <= 0) throw new InvalidOperationException("حساب 444444 یافت نشد");

            var memberName = await ResolveMemberNameAsync(loan.MemberId, null);
            var memberSubsidiary = await _accountsRepo.GetSubsidiaryByMemberIdAsync(loan.MemberId);
            if (memberSubsidiary == null || memberSubsidiary.AccountId <= 0 || string.IsNullOrWhiteSpace(memberSubsidiary.Code))
                throw new InvalidOperationException("کد تفصیلی عضو وام‌گیرنده یافت نشد");
            var preview = new PrepayCashJournalPreview
            {
                LoanId = loan.LoanId,
                MemberId = loan.MemberId,
                MemberName = memberName,
                TotalPayable = totalPayable,
                RemainingPrincipal = Math.Round(remainingPrincipal, 0, MidpointRounding.AwayFromZero),
                CurrentInstallmentInterest = Math.Round(dueInterest, 0, MidpointRounding.AwayFromZero)
            };

            var waivedInterest = Math.Max(0m, remainingInterest - dueInterest);

            // 1) 1201 / 120101 : Debit total payable
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 1,
                ParentCode = parent1201.Code,
                ParentName = parent1201.Name,
                AccountCode = acc120101.Code,
                AccountName = acc120101.Name,
                DisplaySubsidiary = $"{acc120101.Code} - {acc120101.Name}",
                Title = "تسویه پیش از موعد - وجه دریافتی",
                Debit = preview.TotalPayable,
                Credit = 0m
            });

            // 2) 2301 / 230101 : Debit current installment interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 2,
                ParentCode = parent2301.Code,
                ParentName = parent2301.Name,
                AccountCode = acc230101.Code,
                AccountName = acc230101.Name,
                DisplaySubsidiary = $"{acc230101.Code} - {acc230101.Name}",
                Title = "تسویه پیش از موعد - سود محقق شده",
                Debit = preview.CurrentInstallmentInterest,
                Credit = 0m
            });

            // 3) 2301 / 230101 : Debit waived interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 3,
                ParentCode = parent2301.Code,
                ParentName = parent2301.Name,
                AccountCode = acc230101.Code,
                AccountName = acc230101.Name,
                DisplaySubsidiary = $"{acc230101.Code} - {acc230101.Name}",
                Title = "تسویه پیش از موعد - سود تخفیف داده",
                Debit = Math.Round(waivedInterest, 0, MidpointRounding.AwayFromZero),
                Credit = 0m
            });

            if (penalty > 0m)
            {
                preview.Lines.Add(new DisbursementJournalPreview.Line
                {
                    LineNo = 4,
                    ParentCode = parent2202.Code,
                    ParentName = parent2202.Name,
                    AccountCode = acc120101.Code,
                    AccountName = acc120101.Name,
                    DisplaySubsidiary = $"{acc120101.Code} - {acc120101.Name}",
                    Title = "تسویه پیش از موعد - کسر جریمه تاخیر",
                    Debit = Math.Round(penalty, 0, MidpointRounding.AwayFromZero),
                    Credit = 0m
                });
            }

            var lineNoOffset = penalty > 0m ? 1 : 0;

            // 5/4) 1301 / member subsidiary : Credit remaining principal
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 4 + lineNoOffset,
                ParentCode = parent1301.Code,
                ParentName = parent1301.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - مانده اصل وام",
                Debit = 0m,
                Credit = preview.RemainingPrincipal
            });

            // 6/5) 1302 / member subsidiary : Credit current installment interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 5 + lineNoOffset,
                ParentCode = parent1302.Code,
                ParentName = parent1302.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - سود محقق شده",
                Debit = 0m,
                Credit = preview.CurrentInstallmentInterest
            });

            // 7/6) 1302 / member subsidiary : Credit waived interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 6 + lineNoOffset,
                ParentCode = parent1302.Code,
                ParentName = parent1302.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - سود تخفیف داده",
                Debit = 0m,
                Credit = Math.Round(waivedInterest, 0, MidpointRounding.AwayFromZero)
            });

            // 8/7) 4101 / 444444 : Credit current installment interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 7 + lineNoOffset,
                ParentCode = parent4101.Code,
                ParentName = parent4101.Name,
                AccountCode = acc444444.Code,
                AccountName = acc444444.Name,
                DisplaySubsidiary = $"{acc444444.Code} - {acc444444.Name}",
                Title = "تسویه پیش از موعد - سود تحقق یافته",
                Debit = 0m,
                Credit = preview.CurrentInstallmentInterest
            });

            if (penalty > 0m)
            {
                preview.Lines.Add(new DisbursementJournalPreview.Line
                {
                    LineNo = 8 + lineNoOffset,
                    ParentCode = parent4102.Code,
                    ParentName = parent4102.Name,
                    AccountCode = acc444444.Code,
                    AccountName = acc444444.Name,
                    DisplaySubsidiary = $"{acc444444.Code} - {acc444444.Name}",
                    Title = "تسویه پیش از موعد - جریمه تاخیر",
                    Debit = 0m,
                    Credit = Math.Round(penalty, 0, MidpointRounding.AwayFromZero)
                });
            }

            return preview;
        }

        public async Task<PrepayCashJournalPreview> BuildPrepayMemberAccountJournalPreviewAsync(long loanId, decimal totalPayable)
        {
            if (loanId <= 0)
                throw new InvalidOperationException("شناسه وام معتبر نیست");
            if (totalPayable <= 0)
                throw new InvalidOperationException("مبلغ قابل پرداخت معتبر نیست");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var loan = await _loans.GetByIdAsync(loanId);
            if (loan == null)
                throw new InvalidOperationException("وام یافت نشد");

            var memberName = await ResolveMemberNameAsync(loan.MemberId, null);
            var memberSubsidiary = await _accountsRepo.GetSubsidiaryByMemberIdAsync(loan.MemberId);
            if (memberSubsidiary == null || memberSubsidiary.AccountId <= 0 || string.IsNullOrWhiteSpace(memberSubsidiary.Code))
                throw new InvalidOperationException("کد تفصیلی عضو وام‌گیرنده یافت نشد");

            var inst = (await _installments.GetByLoanIdAsync(loanId)).OrderBy(x => x.InstallmentNo).ToList();
            var (remainingPrincipal, remainingInterest, dueInterest, penalty) = ComputePrepayRequiredAmounts(loan, inst, DateTime.Today);

            var required = Math.Round(remainingPrincipal + dueInterest, 0, MidpointRounding.AwayFromZero);
            totalPayable = Math.Round(totalPayable, 0, MidpointRounding.AwayFromZero);
            if (totalPayable != required)
                throw new InvalidOperationException($"مبلغ قابل پرداخت باید برابر {required:N0} باشد (اصل مانده + سود اقساط سررسیدشده)");

            var parent2202 = await GetAccountInfoByCodeAsync("2202", null);
            var parent2301 = await GetAccountInfoByCodeAsync("2301", null);
            var acc230101 = await GetAccountInfoByCodeAsync("230101", null);
            var parent1301 = await GetAccountInfoByCodeAsync("1301", null);
            var parent1302 = await GetAccountInfoByCodeAsync("1302", null);
            var parent4101 = await GetAccountInfoByCodeAsync("4101", null);
            var parent4102 = await GetAccountInfoByCodeAsync("4102", null);
            var acc444444 = await GetAccountInfoByCodeAsync("444444", null);
            var parent2201 = await GetAccountInfoByCodeAsync("2201", null);
            var memberSubsidiaryAccountId = await GetAccountInfoByCodeAsync(memberSubsidiary.Code, null);

            if (parent2201.AccountId <= 0) throw new InvalidOperationException("حساب 2201 یافت نشد");
            if (memberSubsidiaryAccountId.AccountId <= 0) throw new InvalidOperationException("کد تفصیلی عضو یافت نشد");
            if (parent2202.AccountId <= 0) throw new InvalidOperationException("حساب 2202 یافت نشد");
            if (parent2301.AccountId <= 0) throw new InvalidOperationException("حساب 2301 یافت نشد");
            if (acc230101.AccountId <= 0) throw new InvalidOperationException("حساب 230101 یافت نشد");
            if (parent1301.AccountId <= 0) throw new InvalidOperationException("حساب 1301 یافت نشد");
            if (parent1302.AccountId <= 0) throw new InvalidOperationException("حساب 1302 یافت نشد");
            if (parent4101.AccountId <= 0) throw new InvalidOperationException("حساب 4101 یافت نشد");
            if (parent4102.AccountId <= 0) throw new InvalidOperationException("حساب 4102 یافت نشد");
            if (acc444444.AccountId <= 0) throw new InvalidOperationException("حساب 444444 یافت نشد");

            var preview = new PrepayCashJournalPreview
            {
                LoanId = loan.LoanId,
                MemberId = loan.MemberId,
                MemberName = memberName,
                TotalPayable = totalPayable,
                RemainingPrincipal = Math.Round(remainingPrincipal, 0, MidpointRounding.AwayFromZero),
                CurrentInstallmentInterest = Math.Round(dueInterest, 0, MidpointRounding.AwayFromZero)
            };

            var waivedInterest = Math.Max(0m, remainingInterest - dueInterest);

            // 1) 2201 / member subsidiary : Debit total payable
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 1,
                ParentCode = parent2201.Code,
                ParentName = parent2201.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - وجه دریافتی",
                Debit = preview.TotalPayable,
                Credit = 0m
            });

            // 2) 2301 / 230101 : Debit current installment interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 2,
                ParentCode = parent2301.Code,
                ParentName = parent2301.Name,
                AccountCode = acc230101.Code,
                AccountName = acc230101.Name,
                DisplaySubsidiary = $"{acc230101.Code} - {acc230101.Name}",
                Title = "تسویه پیش از موعد - سود محقق شده",
                Debit = preview.CurrentInstallmentInterest,
                Credit = 0m
            });

            // 3) 2301 / 230101 : Debit waived interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 3,
                ParentCode = parent2301.Code,
                ParentName = parent2301.Name,
                AccountCode = acc230101.Code,
                AccountName = acc230101.Name,
                DisplaySubsidiary = $"{acc230101.Code} - {acc230101.Name}",
                Title = "تسویه پیش از موعد - سود تخفیف داده",
                Debit = Math.Round(waivedInterest, 0, MidpointRounding.AwayFromZero),
                Credit = 0m
            });

            if (penalty > 0m)
            {
                preview.Lines.Add(new DisbursementJournalPreview.Line
                {
                    LineNo = 4,
                    ParentCode = parent2202.Code,
                    ParentName = parent2202.Name,
                    AccountCode = memberSubsidiary.Code,
                    AccountName = memberName,
                    DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                    Title = "تسویه پیش از موعد - کسر جریمه تاخیر",
                    Debit = Math.Round(penalty, 0, MidpointRounding.AwayFromZero),
                    Credit = 0m
                });
            }

            var lineNoOffset = penalty > 0m ? 1 : 0;

            // 5/4) 1301 / member subsidiary : Credit remaining principal
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 4 + lineNoOffset,
                ParentCode = parent1301.Code,
                ParentName = parent1301.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - مانده اصل وام",
                Debit = 0m,
                Credit = preview.RemainingPrincipal
            });

            // 6/5) 1302 / member subsidiary : Credit current installment interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 5 + lineNoOffset,
                ParentCode = parent1302.Code,
                ParentName = parent1302.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - سود محقق شده",
                Debit = 0m,
                Credit = preview.CurrentInstallmentInterest
            });

            // 7/6) 1302 / member subsidiary : Credit waived interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 6 + lineNoOffset,
                ParentCode = parent1302.Code,
                ParentName = parent1302.Name,
                AccountCode = memberSubsidiary.Code,
                AccountName = memberName,
                DisplaySubsidiary = $"{memberSubsidiary.Code} - {memberName}",
                Title = "تسویه پیش از موعد - سود تخفیف داده",
                Debit = 0m,
                Credit = Math.Round(waivedInterest, 0, MidpointRounding.AwayFromZero)
            });

            // 8/7) 4101 / 444444 : Credit current installment interest
            preview.Lines.Add(new DisbursementJournalPreview.Line
            {
                LineNo = 7 + lineNoOffset,
                ParentCode = parent4101.Code,
                ParentName = parent4101.Name,
                AccountCode = acc444444.Code,
                AccountName = acc444444.Name,
                DisplaySubsidiary = $"{acc444444.Code} - {acc444444.Name}",
                Title = "تسویه پیش از موعد - سود تحقق یافته",
                Debit = 0m,
                Credit = preview.CurrentInstallmentInterest
            });

            if (penalty > 0m)
            {
                preview.Lines.Add(new DisbursementJournalPreview.Line
                {
                    LineNo = 8 + lineNoOffset,
                    ParentCode = parent4102.Code,
                    ParentName = parent4102.Name,
                    AccountCode = acc444444.Code,
                    AccountName = acc444444.Name,
                    DisplaySubsidiary = $"{acc444444.Code} - {acc444444.Name}",
                    Title = "تسویه پیش از موعد - جریمه تاخیر",
                    Debit = 0m,
                    Credit = Math.Round(penalty, 0, MidpointRounding.AwayFromZero)
                });
            }

            return preview;
        }

        public async Task<long> PostDisbursementJournalAsync(long loanId, string? notes, string createdBy)
        {
            if (loanId <= 0)
                throw new InvalidOperationException("شناسه وام معتبر نیست");

            if (string.IsNullOrWhiteSpace(createdBy))
                createdBy = "admin";

            notes = (notes ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(notes))
                notes = $"صدور سند پرداخت وام #{loanId}";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var loan = await _loans.GetByIdAsync(loanId, tx);
                if (loan == null)
                    throw new InvalidOperationException("وام یافت نشد");

                if (loan.Status != LoanStatus.Approved)
                    throw new InvalidOperationException("صدور سند فقط برای وام‌های آماده پرداخت مجاز است");

                var hasPayments = await _payments.HasAnyForLoanAsync(loanId, tx);
                if (hasPayments)
                    throw new InvalidOperationException("برای این وام پرداخت/تراکنش ثبت شده و صدور سند مجدد مجاز نیست");

                var principal = Math.Round(loan.Principal, 0, MidpointRounding.AwayFromZero);
                var interest = Math.Round(await ComputeLoanInterestAsync(loan, tx), 0, MidpointRounding.AwayFromZero);

                // IMPORTANT: while a local transaction is pending on this connection, every command must use the same transaction.
                // AccountsRepository does not accept a tx, so resolve subsidiary account directly with this tx.
                var memberSubsidiaryAccountId = await _connection.QuerySingleOrDefaultAsync<long?>(
                    @"SELECT TOP 1 AccountId
                      FROM dbo.Accounts
                      WHERE MemberId = @memberId AND IsActive = 1
                      ORDER BY AccountId",
                    new { memberId = loan.MemberId },
                    tx);

                if (!memberSubsidiaryAccountId.HasValue || memberSubsidiaryAccountId.Value <= 0)
                    throw new InvalidOperationException("کد تفصیلی عضو وام‌گیرنده یافت نشد");

                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var parent1301 = await GetAccountIdByCodeAsync("1301", tx);
                var parent1302 = await GetAccountIdByCodeAsync("1302", tx);
                var parent2301 = await GetAccountIdByCodeAsync("2301", tx);
                var acc230101 = await GetAccountIdByCodeAsync("230101", tx);
                var parent1201 = await GetAccountIdByCodeAsync("1201", tx);
                var acc120101 = await GetAccountIdByCodeAsync("120101", tx);

                if (parent1301 <= 0) throw new InvalidOperationException("حساب 1301 یافت نشد");
                if (parent1302 <= 0) throw new InvalidOperationException("حساب 1302 یافت نشد");
                if (parent2301 <= 0) throw new InvalidOperationException("حساب 2301 یافت نشد");
                if (acc230101 <= 0) throw new InvalidOperationException("حساب 230101 یافت نشد");
                if (parent1201 <= 0) throw new InvalidOperationException("حساب 1201 یافت نشد");
                if (acc120101 <= 0) throw new InvalidOperationException("حساب 120101 یافت نشد");

                var occurredAt = loan.DisbursedAt;
                var journalNotes = $"{notes} | LoanDisburse | Loan:{loanId} | Member:{loan.MemberId}";
                var journalId = await _journalsRepo.CreateManualJournalAsync(occurredAt, createdBy, journalNotes, tx);

                // 1) 1301 / member subsidiary : Principal debit
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 1,
                    accountId: memberSubsidiaryAccountId.Value,
                    parentAccountId: parent1301,
                    memberId: loan.MemberId,
                    debit: principal,
                    credit: 0m,
                    description: notes,
                    tx: tx);

                // 2) 1302 / member subsidiary : Interest debit
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 2,
                    accountId: memberSubsidiaryAccountId.Value,
                    parentAccountId: parent1302,
                    memberId: loan.MemberId,
                    debit: interest,
                    credit: 0m,
                    description: notes,
                    tx: tx);

                // 3) 2301 / 230101 : Interest credit
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 3,
                    accountId: acc230101,
                    parentAccountId: parent2301,
                    memberId: null,
                    debit: 0m,
                    credit: interest,
                    description: notes,
                    tx: tx);

                // 4) 1201 / 120101 : Principal credit
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 4,
                    accountId: acc120101,
                    parentAccountId: parent1201,
                    memberId: null,
                    debit: 0m,
                    credit: principal,
                    description: notes,
                    tx: tx);

                // Update status to Disbursed
                await _connection.ExecuteAsync(
                    "UPDATE dbo.Loans SET Status = @st, UpdatedAt = GETUTCDATE() WHERE LoanId = @loanId",
                    new { st = (byte)LoanStatus.Disbursed, loanId },
                    tx);

                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                tx.Commit();
                return journalId;
            }
            catch
            {
                try
                {
                    await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                    await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);
                }
                catch { }

                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<MemberFinancialSnapshot> GetMemberFinancialSnapshotByMemberIdAsync(long memberId)
        {
            if (memberId <= 0)
                return new MemberFinancialSnapshot { MemberCode = string.Empty };

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var code = await _connection.QuerySingleOrDefaultAsync<string?>(
                "SELECT TOP 1 Code FROM dbo.Accounts WHERE MemberId = @memberId AND IsActive = 1 ORDER BY AccountId",
                new { memberId });

            if (string.IsNullOrWhiteSpace(code))
                return new MemberFinancialSnapshot { MemberCode = string.Empty };

            return await _memberFinancial.GetSnapshotByMemberCodeAsync(code.Trim(), null);
        }

        public async Task<long> PostPrepayJournalAsync(long loanId, decimal totalPayable, string paymentMethod, string? notes, string createdBy)
        {
            if (loanId <= 0)
                throw new InvalidOperationException("شناسه وام معتبر نیست");
            if (totalPayable <= 0)
                throw new InvalidOperationException("مبلغ قابل پرداخت معتبر نیست");

            paymentMethod = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
            if (paymentMethod != "cash" && paymentMethod != "member")
                paymentMethod = "cash";

            if (string.IsNullOrWhiteSpace(createdBy))
                createdBy = "admin";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var loan = await _loans.GetByIdAsync(loanId, tx);
                if (loan == null)
                    throw new InvalidOperationException("وام یافت نشد");

                if (loan.Status != LoanStatus.Disbursed && loan.Status != LoanStatus.Active)
                    throw new InvalidOperationException("ثبت سند تسویه فقط برای وام‌های پرداخت‌شده مجاز است");
                var memberSubsidiaryAccountId = await _connection.QuerySingleOrDefaultAsync<long?>(
                    @"SELECT TOP 1 AccountId FROM dbo.Accounts WHERE MemberId = @memberId AND IsActive = 1 ORDER BY AccountId",
                    new { memberId = loan.MemberId },
                    tx);

                if (!memberSubsidiaryAccountId.HasValue || memberSubsidiaryAccountId.Value <= 0)
                    throw new InvalidOperationException("کد تفصیلی عضو وام‌گیرنده یافت نشد");

                var inst = (await _installments.GetByLoanIdAsync(loanId, tx)).OrderBy(x => x.InstallmentNo).ToList();
                var (remainingPrincipal, remainingInterest, dueInterest, penalty) = ComputePrepayRequiredAmounts(loan, inst, DateTime.Today);

                var required = Math.Round(remainingPrincipal + dueInterest, 0, MidpointRounding.AwayFromZero);
                var payable = Math.Round(totalPayable, 0, MidpointRounding.AwayFromZero);
                if (payable != required)
                    throw new InvalidOperationException($"مبلغ قابل پرداخت باید برابر {required:N0} باشد (اصل مانده + سود اقساط سررسیدشده)");

                var rp = remainingPrincipal;
                var ci = dueInterest;
                var pen = penalty;
                var waived = Math.Max(0m, remainingInterest - dueInterest);

                if (paymentMethod == "member")
                {
                    var memberSubsidiaryCode = await _connection.QuerySingleOrDefaultAsync<string?>(
                        "SELECT TOP 1 Code FROM dbo.Accounts WHERE AccountId = @id",
                        new { id = memberSubsidiaryAccountId.Value },
                        tx);

                    if (string.IsNullOrWhiteSpace(memberSubsidiaryCode))
                        throw new InvalidOperationException("کد تفصیلی عضو وام‌گیرنده یافت نشد");

                    var snap = await _memberFinancial.GetSnapshotByMemberCodeAsync(memberSubsidiaryCode.Trim(), tx);
                    var lockedSameLoan = snap.LockedItems
                        .Where(x => x.LoanId == loanId)
                        .Sum(x => x.LockedAmount);
                    var effectiveLocked = snap.LockedAmount - lockedSameLoan;
                    if (effectiveLocked < 0) effectiveLocked = 0;
                    var available = snap.TotalNet - effectiveLocked;
                    if (available < 0) available = 0;
                    if (available < (payable + pen))
                        throw new InvalidOperationException("موجودی قابل برداشت عضو کافی نیست (با لحاظ مبالغ بلوکه‌شده)");
                }

                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                var parentLine1 = await GetAccountIdByCodeAsync(paymentMethod == "member" ? "2201" : "1201", tx);
                var acc120101 = await GetAccountIdByCodeAsync("120101", tx);
                var parent2301 = await GetAccountIdByCodeAsync("2301", tx);
                var acc230101 = await GetAccountIdByCodeAsync("230101", tx);
                var parent2202 = await GetAccountIdByCodeAsync("2202", tx);
                var parent1301 = await GetAccountIdByCodeAsync("1301", tx);
                var parent1302 = await GetAccountIdByCodeAsync("1302", tx);
                var parent4101 = await GetAccountIdByCodeAsync("4101", tx);
                var parent4102 = await GetAccountIdByCodeAsync("4102", tx);
                var acc444444 = await GetAccountIdByCodeAsync("444444", tx);

                if (parentLine1 <= 0) throw new InvalidOperationException(paymentMethod == "member" ? "حساب 2201 یافت نشد" : "حساب 1201 یافت نشد");
                if (acc120101 <= 0) throw new InvalidOperationException("حساب 120101 یافت نشد");
                if (parent2301 <= 0) throw new InvalidOperationException("حساب 2301 یافت نشد");
                if (acc230101 <= 0) throw new InvalidOperationException("حساب 230101 یافت نشد");
                if (parent2202 <= 0) throw new InvalidOperationException("حساب 2202 یافت نشد");
                if (parent1301 <= 0) throw new InvalidOperationException("حساب 1301 یافت نشد");
                if (parent1302 <= 0) throw new InvalidOperationException("حساب 1302 یافت نشد");
                if (parent4101 <= 0) throw new InvalidOperationException("حساب 4101 یافت نشد");
                if (parent4102 <= 0) throw new InvalidOperationException("حساب 4102 یافت نشد");
                if (acc444444 <= 0) throw new InvalidOperationException("حساب 444444 یافت نشد");

                var occurredAt = DateTime.UtcNow;
                var memberName = await ResolveMemberNameAsync(loan.MemberId, tx);
                var journalNotes = $"تسویه پیش از موعد وام شماره {loanId} {memberName} | LoanPrepay | Method:{paymentMethod} | Loan:{loanId} | Member:{loan.MemberId}";
                var journalId = await _journalsRepo.CreateManualJournalAsync(occurredAt, createdBy, journalNotes, tx);

                var line1AccountId = paymentMethod == "member" ? memberSubsidiaryAccountId.Value : acc120101;
                var line1MemberId = paymentMethod == "member" ? loan.MemberId : (long?)null;
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 1,
                    accountId: line1AccountId,
                    parentAccountId: parentLine1,
                    memberId: line1MemberId,
                    debit: payable,
                    credit: 0m,
                    description: "تسویه پیش از موعد - وجه دریافتی",
                    tx: tx);
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 2,
                    accountId: acc230101,
                    parentAccountId: parent2301,
                    memberId: null,
                    debit: ci,
                    credit: 0m,
                    description: "تسویه پیش از موعد - سود محقق شده",
                    tx: tx);
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: 3,
                    accountId: acc230101,
                    parentAccountId: parent2301,
                    memberId: null,
                    debit: waived,
                    credit: 0m,
                    description: "تسویه پیش از موعد - سود تخفیف داده",
                    tx: tx);

                var nextLineNo = 4;
                if (pen > 0m)
                {
                    await _journalLinesRepo.AddLineAsync(
                        journalId: journalId,
                        lineNo: nextLineNo,
                        accountId: memberSubsidiaryAccountId.Value,
                        parentAccountId: parent2202,
                        memberId: loan.MemberId,
                        debit: pen,
                        credit: 0m,
                        description: "تسویه پیش از موعد - کسر جریمه تاخیر",
                        tx: tx);
                    nextLineNo++;
                }
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: nextLineNo,
                    accountId: memberSubsidiaryAccountId.Value,
                    parentAccountId: parent1301,
                    memberId: loan.MemberId,
                    debit: 0m,
                    credit: rp,
                    description: "تسویه پیش از موعد - مانده اصل وام",
                    tx: tx);
                nextLineNo++;
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: nextLineNo,
                    accountId: memberSubsidiaryAccountId.Value,
                    parentAccountId: parent1302,
                    memberId: loan.MemberId,
                    debit: 0m,
                    credit: ci,
                    description: "تسویه پیش از موعد - سود محقق شده",
                    tx: tx);
                nextLineNo++;
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: nextLineNo,
                    accountId: memberSubsidiaryAccountId.Value,
                    parentAccountId: parent1302,
                    memberId: loan.MemberId,
                    debit: 0m,
                    credit: waived,
                    description: "تسویه پیش از موعد - سود تخفیف داده",
                    tx: tx);
                nextLineNo++;
                await _journalLinesRepo.AddLineAsync(
                    journalId: journalId,
                    lineNo: nextLineNo,
                    accountId: acc444444,
                    parentAccountId: parent4101,
                    memberId: null,
                    debit: 0m,
                    credit: ci,
                    description: "تسویه پیش از موعد - سود تحقق یافته",
                    tx: tx);
                nextLineNo++;

                if (pen > 0m)
                {
                    await _journalLinesRepo.AddLineAsync(
                        journalId: journalId,
                        lineNo: nextLineNo,
                        accountId: acc444444,
                        parentAccountId: parent4102,
                        memberId: null,
                        debit: 0m,
                        credit: pen,
                        description: "تسویه پیش از موعد - جریمه تاخیر",
                        tx: tx);
                }

                var totalDebit = payable + ci + waived + (pen > 0m ? pen : 0m);
                var totalCredit = rp + ci + waived + ci + (pen > 0m ? pen : 0m);
                totalDebit = Math.Round(totalDebit, 0, MidpointRounding.AwayFromZero);
                totalCredit = Math.Round(totalCredit, 0, MidpointRounding.AwayFromZero);
                if (totalDebit != totalCredit)
                    throw new InvalidOperationException($"سند تسویه پیش از موعد نامتوازن است (بدهکار: {totalDebit:N0} / بستانکار: {totalCredit:N0})");

                const string sqlInsertPayment = @"
INSERT INTO dbo.LoanPayments (LoanId, MemberId, PaidAt, Amount, IsApproved, ApprovedAt, ApprovedBy, Notes, CreatedAt)
OUTPUT INSERTED.PaymentId
VALUES (@LoanId, @MemberId, @PaidAt, @Amount, 1, @ApprovedAt, @ApprovedBy, @Notes, GETUTCDATE());";

                var paymentId = await _connection.QuerySingleAsync<long>(sqlInsertPayment, new
                {
                    LoanId = loanId,
                    MemberId = loan.MemberId,
                    PaidAt = occurredAt,
                    Amount = payable,
                    ApprovedAt = occurredAt,
                    ApprovedBy = createdBy,
                    Notes = $"ManualJournal:{journalId}"
                }, tx);

                const string sqlInsertAlloc = @"
INSERT INTO dbo.LoanPaymentAllocations (PaymentId, InstallmentId, Type, Amount, CreatedAt)
VALUES (@PaymentId, @InstallmentId, @Type, @Amount, GETUTCDATE());";

                const string sqlUpdateInstallment = @"
UPDATE dbo.LoanInstallments
SET
  PaidPrincipal = @PaidPrincipal,
  PaidInterest = @PaidInterest,
  PaidPenalty = @PaidPenalty,
  Status = @Status
WHERE InstallmentId = @InstallmentId;";

                // Allocate full settlement across installments so UI can show journal refs per installment.
                // Types: 1=Penalty, 2=Interest, 3=Principal
                var today = DateTime.Today;
                var penaltyDailyRate = (loan.PenaltyAnnualRatePercent / 100m) / 365m;
                var penalties = new List<(long InstallmentId, decimal Penalty)>();
                foreach (var it in inst)
                {
                    var remP = Math.Max(0m, it.PrincipalAmount - it.PaidPrincipal);
                    var remI = Math.Max(0m, it.InterestAmount - it.PaidInterest);
                    var remainingBase = remP + remI;
                    if (remainingBase <= 0m)
                    {
                        penalties.Add((it.InstallmentId, 0m));
                        continue;
                    }
                    var due = it.DueDate.Date;
                    if (due >= today)
                    {
                        penalties.Add((it.InstallmentId, 0m));
                        continue;
                    }
                    var overdueDays = Math.Max(0, (today - due).Days);
                    var p = overdueDays > 0 ? (remainingBase * penaltyDailyRate * overdueDays) : 0m;
                    penalties.Add((it.InstallmentId, Math.Round(p, 0, MidpointRounding.AwayFromZero)));
                }

                // Ensure penalty allocations sum to the journal penalty (pen) after rounding.
                var penaltySum = penalties.Sum(x => x.Penalty);
                var delta = Math.Round(pen - penaltySum, 0, MidpointRounding.AwayFromZero);
                if (delta != 0m)
                {
                    for (var i = penalties.Count - 1; i >= 0; i--)
                    {
                        if (penalties[i].Penalty <= 0m && delta > 0m) continue;
                        var newVal = penalties[i].Penalty + delta;
                        if (newVal < 0m) continue;
                        penalties[i] = (penalties[i].InstallmentId, newVal);
                        break;
                    }
                }

                foreach (var it in inst)
                {
                    var allocP = Math.Max(0m, it.PrincipalAmount - it.PaidPrincipal);
                    var allocI = Math.Max(0m, it.InterestAmount - it.PaidInterest);
                    var allocPen = penalties.FirstOrDefault(x => x.InstallmentId == it.InstallmentId).Penalty;

                    if (allocPen > 0m)
                    {
                        await _connection.ExecuteAsync(sqlInsertAlloc, new
                        {
                            PaymentId = paymentId,
                            InstallmentId = it.InstallmentId,
                            Type = 1,
                            Amount = allocPen
                        }, tx);
                    }

                    if (allocI > 0m)
                    {
                        await _connection.ExecuteAsync(sqlInsertAlloc, new
                        {
                            PaymentId = paymentId,
                            InstallmentId = it.InstallmentId,
                            Type = 2,
                            Amount = Math.Round(allocI, 0, MidpointRounding.AwayFromZero)
                        }, tx);
                    }

                    if (allocP > 0m)
                    {
                        await _connection.ExecuteAsync(sqlInsertAlloc, new
                        {
                            PaymentId = paymentId,
                            InstallmentId = it.InstallmentId,
                            Type = 3,
                            Amount = Math.Round(allocP, 0, MidpointRounding.AwayFromZero)
                        }, tx);
                    }

                    var newPaidP = it.PaidPrincipal + allocP;
                    var newPaidI = it.PaidInterest + allocI;
                    var newPaidPen = it.PaidPenalty + allocPen;
                    await _connection.ExecuteAsync(sqlUpdateInstallment, new
                    {
                        InstallmentId = it.InstallmentId,
                        PaidPrincipal = Math.Round(newPaidP, 0, MidpointRounding.AwayFromZero),
                        PaidInterest = Math.Round(newPaidI, 0, MidpointRounding.AwayFromZero),
                        PaidPenalty = Math.Round(newPaidPen, 0, MidpointRounding.AwayFromZero),
                        Status = (byte)LoanInstallmentStatus.Paid
                    }, tx);
                }

                await _connection.ExecuteAsync(
                    "UPDATE dbo.Loans SET Status = @st, UpdatedAt = GETUTCDATE() WHERE LoanId = @loanId",
                    new { st = (byte)LoanStatus.Settled, loanId },
                    tx);

                await _holds.ReleaseByLoanIdAsync(loanId, tx);

                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);

                tx.Commit();
                return journalId;
            }
            catch
            {
                try
                {
                    await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines", transaction: tx);
                    await _connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals", transaction: tx);
                }
                catch { }

                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        private async Task<long> GetAccountIdByCodeAsync(string code, IDbTransaction? tx)
        {
            if (string.IsNullOrWhiteSpace(code)) return 0;
            var id = await _connection.QuerySingleOrDefaultAsync<long?>(
                "SELECT TOP 1 AccountId FROM dbo.Accounts WHERE Code = @code",
                new { code = code.Trim() },
                tx);
            return id ?? 0;
        }

        private async Task<(long AccountId, string Code, string Name)> GetAccountInfoByCodeAsync(string code, IDbTransaction? tx)
        {
            if (string.IsNullOrWhiteSpace(code))
                return (0, string.Empty, string.Empty);

            var row = await _connection.QuerySingleOrDefaultAsync<(long AccountId, string Code, string Name)?>(
                "SELECT TOP 1 AccountId, Code, Name FROM dbo.Accounts WHERE Code = @code",
                new { code = code.Trim() },
                tx);

            return row ?? (0, string.Empty, string.Empty);
        }

        private async Task<decimal> ComputeLoanInterestAsync(Loan loan, IDbTransaction? tx)
        {
            // Prefer stored schedule
            var inst = (await _installments.GetByLoanIdAsync(loan.LoanId, tx)).ToList();
            if (inst.Count > 0)
                return inst.Sum(x => x.InterestAmount);

            // Fallback: EMI * Term - Principal
            return Math.Max(0m, (loan.EmiAmount * loan.TermMonths) - loan.Principal);
        }

        private async Task<string> ResolveMemberNameAsync(long memberId, IDbTransaction? tx)
        {
            if (memberId <= 0) return $"#{memberId}";
            var name = await _connection.QuerySingleOrDefaultAsync<string?>(
                "SELECT TOP 1 (FirstName + N' ' + LastName) FROM dbo.Members WHERE MemberId = @memberId",
                new { memberId },
                tx);
            return string.IsNullOrWhiteSpace(name) ? $"#{memberId}" : name.Trim();
        }

        public async Task<decimal> GetMemberNetCapitalByCodeAsync(string memberCode)
        {
            if (string.IsNullOrWhiteSpace(memberCode)) return 0m;

            // Canonical: snapshot-based balances keyed by subsidiary code (e.g., M-11008)
            var snap = await _memberFinancial.GetSnapshotByMemberCodeAsync(memberCode);
            if (!string.IsNullOrWhiteSpace(snap.MemberCode))
                return snap.TotalNet;

            // Fallback: legacy memberId-based aggregation (older data paths)
            var memberId = await ResolveMemberIdFromCodeAsync(memberCode, null);
            if (memberId <= 0) return 0m;
            return await _txReports.GetMemberNetCapitalByMemberIdAsync(memberId);
        }

        public Task<decimal> GetMemberActiveLockedAmountByCodeAsync(string memberCode)
        {
            if (string.IsNullOrWhiteSpace(memberCode)) return Task.FromResult(0m);
            return _memberFinancial.GetLockedAmountAsync(memberCode);
        }

        public async Task<decimal> GetMemberAvailableCapitalByCodeAsync(string memberCode)
        {
            if (string.IsNullOrWhiteSpace(memberCode)) return 0m;
            return await _memberFinancial.GetAvailableCapitalAsync(memberCode);
        }

        public Task<Loan?> GetByIdAsync(long loanId) => _loans.GetByIdAsync(loanId);
        public Task<IEnumerable<Loan>> GetAllAsync(LoanStatus? status = null, int limit = 500) => _loans.GetAllAsync(status, limit);
        public Task<IEnumerable<Loan>> GetAllPagedAsync(int offset, int pageSize, LoanStatus? status = null) => _loans.GetAllPagedAsync(offset, pageSize, status);
        public Task<IEnumerable<Loan>> GetByMemberAsync(long memberId, LoanStatus? status = null) => _loans.GetByMemberAsync(memberId, status);
        public Task<IEnumerable<Loan>> GetByMemberCodeAsync(string memberCode, LoanStatus? status = null) => _loans.GetByMemberCodeAsync(memberCode, status);
        public Task ForceUpdateLoanStatusAsync(long loanId, LoanStatus newStatus) => _loans.UpdateStatusAsync(loanId, newStatus);
        public Task<IEnumerable<LoanInstallment>> GetInstallmentsAsync(long loanId) => _installments.GetByLoanIdAsync(loanId);
        public Task<IReadOnlyDictionary<long, decimal>> GetInterestSumsByLoanIdsAsync(IEnumerable<long> loanIds) => _installments.GetInterestSumsByLoanIdsAsync(loanIds);
        public Task<IReadOnlyDictionary<long, LoanInstallmentRepository.LoanRemainingAggRow>> GetRemainingSumsByLoanIdsAsync(IEnumerable<long> loanIds)
            => _installments.GetRemainingSumsByLoanIdsAsync(loanIds);
        public Task<IEnumerable<LoanGuarantee>> GetGuaranteesAsync(long loanId) => _guarantees.GetByLoanIdAsync(loanId);

        public async Task<IReadOnlyDictionary<long, decimal>> GetWaivedPenaltySumsByInstallmentIdAsync(long loanId)
        {
            if (loanId <= 0) return new Dictionary<long, decimal>();

            const string sql = @"
SELECT a.InstallmentId, SUM(a.Amount) AS SumAmount
FROM dbo.LoanPaymentAllocations a
INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
WHERE p.LoanId = @loanId
  AND a.Type = 4
GROUP BY a.InstallmentId";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var rows = (await _connection.QueryAsync<(long InstallmentId, decimal SumAmount)>(sql, new { loanId })).ToList();
            return rows.ToDictionary(x => x.InstallmentId, x => x.SumAmount);
        }

        public async Task<long> GetNextLoanNumberAsync()
        {
            const string sql = "SELECT ISNULL(MAX(LoanId), 0) + 1 FROM dbo.Loans";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return await _connection.QuerySingleAsync<long>(sql);
        }

        public sealed class GuaranteedLoanRow
        {
            public long LoanId { get; set; }
            public long MemberId { get; set; }
            public string? MemberCode { get; set; }
            public string BorrowerName { get; set; } = string.Empty;
            public DateTime DisbursedAt { get; set; }
            public decimal Principal { get; set; }
            public int TermMonths { get; set; }
            public decimal EmiAmount { get; set; }
            public decimal OutstandingPrincipal { get; set; }
        }

        public async Task<IReadOnlyList<GuaranteedLoanRow>> GetActiveGuaranteedLoansAsync(long guarantorMemberId, string? guarantorMemberCode)
        {
            if (guarantorMemberId <= 0 && string.IsNullOrWhiteSpace(guarantorMemberCode))
                return Array.Empty<GuaranteedLoanRow>();

            const string sql = @"
SELECT
    l.LoanId,
    l.MemberId,
    l.MemberCode,
    (m.FirstName + N' ' + m.LastName) AS BorrowerName,
    l.DisbursedAt,
    l.Principal,
    l.TermMonths,
    l.EmiAmount,
    l.OutstandingPrincipal
FROM dbo.LoanGuarantees lg
INNER JOIN dbo.Loans l ON l.LoanId = lg.LoanId
LEFT JOIN dbo.Members m ON m.MemberId = l.MemberId
WHERE lg.IsActive = 1
  AND lg.Type = 1
  AND (
        (lg.GuarantorMemberId IS NOT NULL AND lg.GuarantorMemberId = @guarantorMemberId)
        OR (@guarantorMemberCode IS NOT NULL AND lg.GuarantorMemberCode = @guarantorMemberCode)
      )
  AND l.Status IN (2,3,4)
ORDER BY l.LoanId DESC;";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var rows = (await _connection.QueryAsync<GuaranteedLoanRow>(sql, new { guarantorMemberId, guarantorMemberCode })).ToList();
            return rows;
        }

        public sealed class HouseholdLoanRiskRow
        {
            public long MemberId { get; set; }
            public string? MemberCode { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public int ActiveLoanCount { get; set; }
            public int ActiveGuarantorCount { get; set; }
        }

        public async Task<IReadOnlyList<HouseholdLoanRiskRow>> GetHouseholdLoanRiskAsync(string householdCode)
        {
            if (string.IsNullOrWhiteSpace(householdCode))
                return Array.Empty<HouseholdLoanRiskRow>();

            const string sql = @"
;WITH mem AS (
    SELECT m.MemberId,
           m.FirstName + N' ' + m.LastName AS MemberName,
           m.HouseholdCode
    FROM dbo.Members m
    WHERE m.IsActive = 1 AND m.HouseholdCode = @householdCode
), sub AS (
    SELECT mem.MemberId,
           mem.MemberName,
           mem.HouseholdCode,
           s.Code AS MemberCode
    FROM mem
    OUTER APPLY (
        SELECT TOP 1 a.Code
        FROM dbo.Accounts a
        WHERE a.MemberId = mem.MemberId
          AND a.IsActive = 1
          AND a.Code LIKE N'M-%'
        ORDER BY a.AccountId DESC
    ) s
)
SELECT
    s.MemberId,
    s.MemberCode,
    s.MemberName,
    COUNT(DISTINCT l.LoanId) AS ActiveLoanCount,
    COUNT(DISTINCT gl.LoanId) AS ActiveGuarantorCount
FROM sub s
LEFT JOIN dbo.Loans l
    ON (l.MemberId = s.MemberId OR (s.MemberCode IS NOT NULL AND l.MemberCode = s.MemberCode))
   AND l.Status IN (2,3,4)
LEFT JOIN dbo.LoanGuarantees lg
    ON (lg.GuarantorMemberId = s.MemberId OR (s.MemberCode IS NOT NULL AND lg.GuarantorMemberCode = s.MemberCode))
LEFT JOIN dbo.Loans gl
    ON gl.LoanId = lg.LoanId
   AND gl.Status IN (2,3,4)
GROUP BY s.MemberId, s.MemberCode, s.MemberName
ORDER BY s.MemberId ASC;";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var rows = (await _connection.QueryAsync<HouseholdLoanRiskRow>(sql, new { householdCode })).ToList();
            return rows;
        }

        private async Task<long> ResolveMemberIdFromCodeAsync(string memberCode, IDbTransaction? tx = null)
        {
            if (string.IsNullOrWhiteSpace(memberCode)) return 0;
            const string sql = @"SELECT TOP 1 MemberId
FROM dbo.Accounts
WHERE Code = @memberCode AND MemberId IS NOT NULL
ORDER BY IsActive DESC, AccountId DESC";

            var id = await _connection.QuerySingleOrDefaultAsync<long?>(sql, new { memberCode }, tx);
            return id ?? 0;
        }

        public async Task<long> CreateLoanWithScheduleAsync(
            string memberCode,
            long? loanRatePlanId,
            decimal principal,
            int termMonths,
            DateTime disbursedAt,
            decimal? fixedEmiAmount = null,
            DateTime? firstDueDate = null,
            string? notes = null,
            IEnumerable<LoanGuarantee>? guarantees = null,
            bool ignoreGuarantorCoverage = false,
            decimal? annualRatePercentOverride = null,
            decimal? penaltyAnnualRatePercentOverride = null)
        {
            if (string.IsNullOrWhiteSpace(memberCode)) throw new InvalidOperationException("عضو معتبر نیست");
            if (principal <= 0) throw new InvalidOperationException("مبلغ وام معتبر نیست");
            if (termMonths <= 0) throw new InvalidOperationException("تعداد اقساط معتبر نیست");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var memberId = await ResolveMemberIdFromCodeAsync(memberCode, tx);
                if (memberId <= 0) throw new InvalidOperationException("کد تفصیلی عضو معتبر نیست");

                var loanId = await CreateLoanWithScheduleInternalAsync(
                    memberId,
                    memberCode,
                    loanRatePlanId,
                    principal,
                    termMonths,
                    disbursedAt,
                    fixedEmiAmount,
                    firstDueDate,
                    notes,
                    guarantees,
                    ignoreGuarantorCoverage,
                    annualRatePercentOverride,
                    penaltyAnnualRatePercentOverride,
                    tx);
                tx.Commit();
                return loanId;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        private sealed class PaymentSplitRow
        {
            public long PaymentId { get; set; }
            public DateTime PaidAt { get; set; }
            public string? Notes { get; set; }
            public long? JournalId { get; set; }
            public decimal Amount { get; set; }
            public decimal SumInterest { get; set; }
            public decimal SumPrincipal { get; set; }
        }

        private sealed class InstallmentReallocRow
        {
            public long InstallmentId { get; set; }
            public int InstallmentNo { get; set; }
            public decimal InterestAmount { get; set; }
            public decimal PrincipalAmount { get; set; }
        }

        private async Task ReallocatePrincipalAndInterestAllocationsAsync(long loanId, IDbTransaction tx)
        {
            if (loanId <= 0)
                return;

            const string sqlInstallments = @"
SELECT InstallmentId, InstallmentNo, InterestAmount, PrincipalAmount
FROM dbo.LoanInstallments
WHERE LoanId = @loanId
ORDER BY InstallmentNo ASC";

            var installments = (await _connection.QueryAsync<InstallmentReallocRow>(sqlInstallments, new { loanId }, tx)).ToList();
            if (installments.Count == 0)
                return;

            // Load payments (keep reference for traceability)
            const string sqlPayments = @"
SELECT
  p.PaymentId,
  p.PaidAt,
  p.Notes,
  p.Amount,
  TRY_CONVERT(BIGINT,
    CASE
      WHEN p.Notes LIKE 'ManualJournal:%' THEN SUBSTRING(p.Notes, LEN('ManualJournal:') + 1, 50)
      ELSE NULL
    END
  ) AS JournalId,
  0 AS SumInterest,
  0 AS SumPrincipal
FROM dbo.LoanPayments p
WHERE p.LoanId = @loanId
ORDER BY p.PaidAt ASC, p.PaymentId ASC";

            var payments = (await _connection.QueryAsync<PaymentSplitRow>(sqlPayments, new { loanId }, tx)).ToList();
            if (payments.Count == 0)
                return;

            // IMPORTANT:
            // After editing a loan schedule, the historical principal/interest split can become incompatible with the new schedule.
            // To prevent "skipping" installments (allocating to later installments while earlier ones still have remaining total),
            // we re-allocate based on the total payment amount only, sequentially across installments.

            // But note: LoanPayments.Amount (for manual journals) includes principal + interest + paid penalty.
            // We keep penalty allocations (Type=1) intact and only rebuild principal/interest allocations (Type=2/3).
            // So the amount available for reallocation must exclude the paid penalty portion.

            const string sqlPenaltyByPayment = @"
SELECT a.PaymentId, ISNULL(SUM(a.Amount),0) AS PaidPenalty
FROM dbo.LoanPaymentAllocations a
INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
WHERE p.LoanId = @loanId
  AND a.Type = 1
GROUP BY a.PaymentId";

            var penaltyByPayment = (await _connection.QueryAsync<(long PaymentId, decimal PaidPenalty)>(sqlPenaltyByPayment, new { loanId }, tx))
                .ToDictionary(x => x.PaymentId, x => x.PaidPenalty);

            const string sqlDeleteAlloc = @"
DELETE a
FROM dbo.LoanPaymentAllocations a
INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
WHERE p.LoanId = @loanId
  AND a.Type IN (2,3);";

            await _connection.ExecuteAsync(sqlDeleteAlloc, new { loanId }, tx);

            const string sqlInsertAlloc = @"
INSERT INTO dbo.LoanPaymentAllocations (PaymentId, InstallmentId, Type, Amount, CreatedAt)
VALUES (@PaymentId, @InstallmentId, @Type, @Amount, GETUTCDATE());";

            var paidIByInstId = installments.ToDictionary(x => x.InstallmentId, _ => 0m);
            var paidPByInstId = installments.ToDictionary(x => x.InstallmentId, _ => 0m);

            foreach (var pay in payments)
            {
                var paidPenalty = penaltyByPayment.TryGetValue(pay.PaymentId, out var pp) ? pp : 0m;
                if (paidPenalty < 0m) paidPenalty = 0m;

                // Allocate only principal+interest, not penalty.
                var remainingTotal = Math.Max(0m, pay.Amount - paidPenalty);
                if (remainingTotal <= 0m)
                    continue;

                foreach (var inst in installments)
                {
                    if (remainingTotal <= 0m)
                        break;

                    var instRemI = Math.Max(0m, inst.InterestAmount - paidIByInstId[inst.InstallmentId]);
                    if (instRemI > 0m && remainingTotal > 0m)
                    {
                        var allocI = Math.Min(instRemI, remainingTotal);
                        if (allocI > 0m)
                        {
                            await _connection.ExecuteAsync(sqlInsertAlloc, new
                            {
                                PaymentId = pay.PaymentId,
                                InstallmentId = inst.InstallmentId,
                                Type = 2,
                                Amount = allocI
                            }, tx);
                            paidIByInstId[inst.InstallmentId] += allocI;
                            remainingTotal -= allocI;
                        }
                    }

                    var instRemP = Math.Max(0m, inst.PrincipalAmount - paidPByInstId[inst.InstallmentId]);
                    if (instRemP > 0m && remainingTotal > 0m)
                    {
                        var allocP = Math.Min(instRemP, remainingTotal);
                        if (allocP > 0m)
                        {
                            await _connection.ExecuteAsync(sqlInsertAlloc, new
                            {
                                PaymentId = pay.PaymentId,
                                InstallmentId = inst.InstallmentId,
                                Type = 3,
                                Amount = allocP
                            }, tx);
                            paidPByInstId[inst.InstallmentId] += allocP;
                            remainingTotal -= allocP;
                        }
                    }
                }
            }
        }

        public async Task DeleteLoanAsync(long loanId)
        {
            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var loan = await _loans.GetByIdAsync(loanId, tx);
                if (loan == null) return;

                if (loan.Status != LoanStatus.Draft && loan.Status != LoanStatus.Approved)
                    throw new InvalidOperationException("حذف فقط برای وام‌های پیش‌نویس یا آماده پرداخت مجاز است");

                var hasPayments = await _payments.HasAnyForLoanAsync(loanId, tx);
                if (hasPayments)
                    throw new InvalidOperationException("برای این وام پرداخت/تراکنش ثبت شده و حذف مجاز نیست");

                await _holds.DeleteByLoanIdAsync(loanId, tx);

                // Cascade deletes will remove installments/guarantees if FK constraints exist.
                await _loans.DeleteAsync(loanId, tx);

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task ForceDeleteLoanAsync(long loanId)
        {
            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var loan = await _loans.GetByIdAsync(loanId, tx);
                if (loan == null) return;

                const string sqlDeleteAllocations = @"
DELETE a
FROM dbo.LoanPaymentAllocations a
INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
WHERE p.LoanId = @loanId;";

                const string sqlDeletePayments = "DELETE FROM dbo.LoanPayments WHERE LoanId = @loanId";

                await _connection.ExecuteAsync(sqlDeleteAllocations, new { loanId }, tx);
                await _connection.ExecuteAsync(sqlDeletePayments, new { loanId }, tx);

                await _installments.DeleteByLoanIdAsync(loanId, tx);
                await _guarantees.ReplaceForLoanAsync(loanId, Array.Empty<LoanGuarantee>(), tx);
                await _holds.DeleteByLoanIdAsync(loanId, tx);

                await _loans.DeleteAsync(loanId, tx);

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task ForceDeleteAllLoansAsync()
        {
            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                const string sqlDeleteAllocations = @"
DELETE a
FROM dbo.LoanPaymentAllocations a
INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId;";

                const string sqlDeletePayments = "DELETE FROM dbo.LoanPayments";
                const string sqlDeleteInstallments = "DELETE FROM dbo.LoanInstallments";
                const string sqlDeleteGuarantees = "DELETE FROM dbo.LoanGuarantees";
                const string sqlDeleteHolds = "DELETE FROM dbo.CreditHolds WHERE LoanId IS NOT NULL";
                const string sqlDeleteLoans = "DELETE FROM dbo.Loans";

                await _connection.ExecuteAsync(sqlDeleteAllocations, null, tx);
                await _connection.ExecuteAsync(sqlDeletePayments, null, tx);
                await _connection.ExecuteAsync(sqlDeleteInstallments, null, tx);
                await _connection.ExecuteAsync(sqlDeleteGuarantees, null, tx);
                await _connection.ExecuteAsync(sqlDeleteHolds, null, tx);
                await _connection.ExecuteAsync(sqlDeleteLoans, null, tx);

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<bool> CanDeleteLoanAsync(long loanId)
        {
            if (loanId <= 0) return false;
            var hasPayments = await _payments.HasAnyForLoanAsync(loanId);
            return !hasPayments;
        }

        public sealed class InstallmentJournalRef
        {
            public long InstallmentId { get; set; }
            public long JournalId { get; set; }
            public int? DocumentNumber { get; set; }
            public DateTime OccurredAt { get; set; }
        }

        public sealed class DisbursementJournalRef
        {
            public long LoanId { get; set; }
            public long JournalId { get; set; }
            public int? DocumentNumber { get; set; }
            public DateTime OccurredAt { get; set; }
        }

        public async Task<IReadOnlyDictionary<long, DisbursementJournalRef>> GetDisbursementJournalRefsByLoanIdAsync(IEnumerable<long> loanIds)
        {
            var ids = (loanIds ?? Array.Empty<long>()).Where(x => x > 0).Distinct().Take(1000).ToList();
            if (ids.Count == 0)
                return new Dictionary<long, DisbursementJournalRef>();

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            const string sql = @"
WITH src AS (
    SELECT j.JournalId, j.OccurredAt, ISNULL(j.Notes,'') AS Notes
    FROM dbo.vw_ApprovedJournals j
    WHERE j.Notes LIKE '%LoanDisburse%'
      AND j.Notes LIKE '%Loan:%'
), parsed AS (
    SELECT
        s.JournalId,
        s.OccurredAt,
        TRY_CONVERT(BIGINT,
            SUBSTRING(
                SUBSTRING(s.Notes, CHARINDEX('Loan:', s.Notes) + LEN('Loan:'), 50),
                1,
                PATINDEX('%[^0-9]%', SUBSTRING(s.Notes, CHARINDEX('Loan:', s.Notes) + LEN('Loan:'), 50) + 'X') - 1
            )
        ) AS LoanId
    FROM src s
    WHERE CHARINDEX('Loan:', s.Notes) > 0
)
SELECT p.LoanId, MAX(p.JournalId) AS JournalId
FROM parsed p
WHERE p.LoanId IN @ids AND p.LoanId IS NOT NULL AND p.LoanId > 0
GROUP BY p.LoanId;";

            var maxRows = (await _connection.QueryAsync<(long LoanId, long JournalId)>(sql, new { ids })).ToList();
            if (maxRows.Count == 0)
                return new Dictionary<long, DisbursementJournalRef>();

            // Load occurredAt + DocumentNumber for resolved journal ids
            var jids = maxRows.Select(x => x.JournalId).Where(x => x > 0).Distinct().ToList();
            const string sql2 = @"SELECT JournalId, OccurredAt, DocumentNumber FROM dbo.vw_ApprovedJournals WHERE JournalId IN @jids";
            var meta = (await _connection.QueryAsync<DisbursementJournalRef>(sql2, new { jids }))
                .ToDictionary(x => x.JournalId, x => x);

            return maxRows
                .Where(x => x.LoanId > 0 && x.JournalId > 0)
                .GroupBy(x => x.LoanId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var row = g.OrderByDescending(x => x.JournalId).First();
                        meta.TryGetValue(row.JournalId, out var m);
                        return new DisbursementJournalRef
                        {
                            LoanId = row.LoanId,
                            JournalId = row.JournalId,
                            DocumentNumber = m?.DocumentNumber,
                            OccurredAt = m?.OccurredAt ?? default
                        };
                    });
        }

        public async Task<IReadOnlyDictionary<long, InstallmentJournalRef>> GetLastJournalRefsByInstallmentIdAsync(long loanId)
        {
            if (loanId <= 0)
                return new Dictionary<long, InstallmentJournalRef>();

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            const string sql = @"
WITH pay AS (
    SELECT
        a.InstallmentId,
        TRY_CONVERT(BIGINT,
            CASE
                WHEN p.Notes LIKE 'ManualJournal:%' THEN
                    SUBSTRING(
                        SUBSTRING(p.Notes, LEN('ManualJournal:') + 1, 50),
                        1,
                        PATINDEX('%[^0-9]%', SUBSTRING(p.Notes, LEN('ManualJournal:') + 1, 50) + 'X') - 1
                    )
                ELSE NULL
            END
        ) AS JournalId
    FROM dbo.LoanPaymentAllocations a
    INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
    WHERE p.LoanId = @loanId
      AND a.InstallmentId IS NOT NULL
)
SELECT
    x.InstallmentId,
    x.JournalId,
    j.DocumentNumber,
    j.OccurredAt
FROM (
    SELECT InstallmentId, MAX(JournalId) AS JournalId
    FROM pay
    WHERE JournalId IS NOT NULL AND JournalId > 0
    GROUP BY InstallmentId
) x
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = x.JournalId;";

            var rows = (await _connection.QueryAsync<InstallmentJournalRef>(sql, new { loanId })).ToList();
            return rows
                .Where(r => r.InstallmentId > 0 && r.JournalId > 0)
                .GroupBy(r => r.InstallmentId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.JournalId).First());
        }

        public sealed class JournalHeaderDto
        {
            public long JournalId { get; set; }
            public int? DocumentNumber { get; set; }
            public DateTime OccurredAt { get; set; }
            public string? Notes { get; set; }
        }

        public async Task<JournalHeaderDto?> GetJournalHeaderAsync(long journalId)
        {
            if (journalId <= 0)
                return null;

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            const string sql = @"
SELECT JournalId, DocumentNumber, OccurredAt, ISNULL(Notes,'') AS Notes
FROM dbo.vw_ApprovedJournals
WHERE JournalId = @journalId";

            return await _connection.QueryFirstOrDefaultAsync<JournalHeaderDto>(sql, new { journalId });
        }

        public sealed class JournalLineDetailsDto
        {
            public int LineNo { get; set; }
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public string? Description { get; set; }
            public long? ParentAccountId { get; set; }
            public string AccCode { get; set; } = string.Empty;
            public string AccName { get; set; } = string.Empty;
            public string ParentCode { get; set; } = string.Empty;
            public string ParentName { get; set; } = string.Empty;
        }

        public async Task<IReadOnlyList<JournalLineDetailsDto>> GetJournalLinesDetailsAsync(long journalId)
        {
            if (journalId <= 0)
                return Array.Empty<JournalLineDetailsDto>();

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            const string sql = @"
SELECT
  jl.[LineNo],
  jl.Debit,
  jl.Credit,
  ISNULL(jl.Description,'') AS Description,
  jl.ParentAccountId,
  ISNULL(pa.Code,'') AS ParentCode,
  ISNULL(pa.Name,'') AS ParentName,
  ISNULL(a.Code,'') AS AccCode,
  ISNULL(a.Name,'') AS AccName
FROM dbo.JournalLines jl
LEFT JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
LEFT JOIN dbo.Accounts pa ON pa.AccountId = jl.ParentAccountId
WHERE jl.JournalId = @journalId
ORDER BY jl.[LineNo]";

            var rows = (await _connection.QueryAsync<JournalLineDetailsDto>(sql, new { journalId })).ToList();
            return rows;
        }

        public async Task UpdateLoanWithScheduleAsync(
            long loanId,
            string memberCode,
            decimal principal,
            int termMonths,
            DateTime disbursedAt,
            decimal? fixedEmiAmount = null,
            DateTime? firstDueDate = null,
            string? notes = null,
            IEnumerable<LoanGuarantee>? guarantees = null,
            bool ignoreGuarantorCoverage = false,
            decimal? annualRatePercentOverride = null,
            decimal? penaltyAnnualRatePercentOverride = null)
        {
            if (loanId <= 0) throw new InvalidOperationException("شناسه وام معتبر نیست");
            if (string.IsNullOrWhiteSpace(memberCode)) throw new InvalidOperationException("عضو معتبر نیست");
            if (principal <= 0) throw new InvalidOperationException("مبلغ وام معتبر نیست");
            if (termMonths <= 0) throw new InvalidOperationException("تعداد اقساط معتبر نیست");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var existing = await _loans.GetByIdAsync(loanId, tx);
                if (existing == null) throw new InvalidOperationException("وام یافت نشد");

                var resolvedMemberId = await ResolveMemberIdFromCodeAsync(memberCode.Trim(), tx);
                if (resolvedMemberId <= 0) throw new InvalidOperationException("کد تفصیلی عضو معتبر نیست");

                existing.MemberId = resolvedMemberId;
                existing.MemberCode = memberCode.Trim();

                var hasPayments = await _payments.HasAnyForLoanAsync(loanId, tx);

                // IMPORTANT:
                // If the loan already has payments/transactions, we must NOT touch posted journals/transactions.
                // But we MAY allow full edits by reconciling existing installments in-place (by InstallmentNo),
                // without deleting installments that already have allocations.
                if (hasPayments)
                {
                    // Remove current guarantee/hold artifacts first so snapshot/locked calculations don't double-count this loan
                    await _guarantees.ReplaceForLoanAsync(loanId, Array.Empty<LoanGuarantee>(), tx);
                    await _holds.DeleteByLoanIdAsync(loanId, tx);

                    // Determine plan (same as normal edit)
                    LoanRatePlan? plan;
                    if (existing.LoanRatePlanId > 0)
                    {
                        plan = await _loanRates.GetByIdAsync(existing.LoanRatePlanId, tx);
                        if (plan == null)
                            plan = await _loanRates.GetActiveForDateAsync(disbursedAt.Date, tx);
                    }
                    else
                    {
                        plan = await _loanRates.GetActiveForDateAsync(disbursedAt.Date, tx);
                    }
                    if (plan == null)
                        plan = await _loanRates.GetLatestActiveAsync(tx);
                    if (plan == null) throw new InvalidOperationException("برای تاریخ پرداخت، نرخ وام فعال تعریف نشده است");
                    if (!plan.IsActive) throw new InvalidOperationException("نرخ وام انتخاب‌شده غیرفعال است");

                    var firstDue = (firstDueDate ?? disbursedAt.Date.AddPersianMonths(1)).Date;

                    existing.Principal = Math.Round(principal, 0, MidpointRounding.AwayFromZero);
                    existing.TermMonths = termMonths;
                    existing.DisbursedAt = disbursedAt.Date;
                    existing.FirstDueDate = firstDue;
                    if (annualRatePercentOverride.HasValue)
                    {
                        var v = annualRatePercentOverride.Value;
                        if (v < 0m) throw new InvalidOperationException("نرخ سود معتبر نیست");
                        existing.AnnualRatePercent = v;
                    }
                    else
                    {
                        existing.AnnualRatePercent = plan.AnnualRatePercent;
                    }

                    if (penaltyAnnualRatePercentOverride.HasValue)
                    {
                        var v = penaltyAnnualRatePercentOverride.Value;
                        if (v < 0m) throw new InvalidOperationException("نرخ دیرکرد معتبر نیست");
                        existing.PenaltyAnnualRatePercent = v;
                    }
                    else
                    {
                        existing.PenaltyAnnualRatePercent = plan.PenaltyAnnualRatePercent;
                    }

                    existing.Notes = notes;

                    var (emi, schedule) = (fixedEmiAmount.HasValue && fixedEmiAmount.Value > 0)
                        ? _calculator.GenerateScheduleWithFixedEmi(
                            loanId,
                            existing.Principal,
                            existing.TermMonths,
                            existing.FirstDueDate,
                            existing.AnnualRatePercent,
                            fixedEmiAmount.Value)
                        : _calculator.GenerateSchedule(
                            loanId,
                            existing.Principal,
                            existing.TermMonths,
                            existing.FirstDueDate,
                            existing.AnnualRatePercent);

                    existing.EmiAmount = emi;

                    // Reconcile installments in-place to preserve InstallmentId for installments that already have allocations.
                    const string sqlExistingInstallments = @"SELECT * FROM dbo.LoanInstallments WHERE LoanId = @loanId ORDER BY InstallmentNo ASC";
                    var existingInstallments = (await _connection.QueryAsync<MiniBank.Web.Models.LoanInstallment>(sqlExistingInstallments, new { loanId }, tx)).ToList();

                    const string sqlPaidInstallmentIds = @"
SELECT DISTINCT a.InstallmentId
FROM dbo.LoanPaymentAllocations a
INNER JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
WHERE p.LoanId = @loanId";
                    var paidInstallmentIds = (await _connection.QueryAsync<long>(sqlPaidInstallmentIds, new { loanId }, tx)).ToHashSet();

                    var maxPaidNo = existingInstallments
                        .Where(x => paidInstallmentIds.Contains(x.InstallmentId) || x.PaidPrincipal > 0m || x.PaidInterest > 0m || x.PaidPenalty > 0m)
                        .Select(x => x.InstallmentNo)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (existing.TermMonths < maxPaidNo)
                        throw new InvalidOperationException($"تعداد اقساط نمی‌تواند کمتر از {maxPaidNo} باشد چون برای اقساط بعد از آن پرداخت ثبت شده است");

                    // Update by InstallmentNo (keep InstallmentId)
                    const string sqlUpdateInstallment = @"
UPDATE dbo.LoanInstallments
SET
  DueDate = @DueDate,
  OpeningBalance = @OpeningBalance,
  EmiAmount = @EmiAmount,
  InterestAmount = @InterestAmount,
  PrincipalAmount = @PrincipalAmount,
  ClosingBalance = @ClosingBalance
WHERE InstallmentId = @InstallmentId";

                    var existingByNo = existingInstallments.ToDictionary(x => x.InstallmentNo, x => x);
                    foreach (var s in schedule)
                    {
                        if (existingByNo.TryGetValue(s.InstallmentNo, out var exInst))
                        {
                            await _connection.ExecuteAsync(sqlUpdateInstallment, new
                            {
                                InstallmentId = exInst.InstallmentId,
                                DueDate = s.DueDate.Date,
                                s.OpeningBalance,
                                s.EmiAmount,
                                s.InterestAmount,
                                s.PrincipalAmount,
                                s.ClosingBalance
                            }, tx);
                        }
                        else
                        {
                            // Insert new installment
                            const string sqlInsertInstallment = @"
INSERT INTO dbo.LoanInstallments (
    LoanId, InstallmentNo, DueDate,
    OpeningBalance, EmiAmount, InterestAmount, PrincipalAmount, ClosingBalance,
    PaidPrincipal, PaidInterest, PaidPenalty,
    Status, CreatedAt
)
VALUES (
    @LoanId, @InstallmentNo, @DueDate,
    @OpeningBalance, @EmiAmount, @InterestAmount, @PrincipalAmount, @ClosingBalance,
    0, 0, 0,
    @Status, GETUTCDATE()
);";

                            await _connection.ExecuteAsync(sqlInsertInstallment, new
                            {
                                LoanId = loanId,
                                s.InstallmentNo,
                                DueDate = s.DueDate.Date,
                                s.OpeningBalance,
                                s.EmiAmount,
                                s.InterestAmount,
                                s.PrincipalAmount,
                                s.ClosingBalance,
                                Status = (byte)MiniBank.Web.Models.LoanInstallmentStatus.Unpaid
                            }, tx);
                        }
                    }

                    // Delete extra installments only if they have no allocations
                    var desiredNos = schedule.Select(x => x.InstallmentNo).ToHashSet();
                    var extra = existingInstallments.Where(x => !desiredNos.Contains(x.InstallmentNo)).ToList();
                    var deletableIds = extra
                        .Where(x => !paidInstallmentIds.Contains(x.InstallmentId) && x.PaidPrincipal <= 0m && x.PaidInterest <= 0m && x.PaidPenalty <= 0m)
                        .Select(x => x.InstallmentId)
                        .ToList();
                    if (deletableIds.Count > 0)
                        await _connection.ExecuteAsync("DELETE FROM dbo.LoanInstallments WHERE InstallmentId IN @ids", new { ids = deletableIds }, tx);

                    await ReallocatePrincipalAndInterestAllocationsAsync(loanId, tx);

                    // Recompute Paid*/Status from allocations and update loan status (does not touch journals)
                    const string sqlRecomputeInstallments = @"
UPDATE li
SET
  PaidPrincipal = ISNULL(x.PaidPrincipal, 0),
  PaidInterest = ISNULL(x.PaidInterest, 0),
  PaidPenalty = ISNULL(x.PaidPenalty, 0),
  Status = CASE
    WHEN (ISNULL(x.PaidPrincipal,0) + ISNULL(x.PaidInterest,0)) <= 0 THEN 1
    WHEN (ISNULL(x.PaidPrincipal,0) + ISNULL(x.PaidInterest,0)) >= (li.PrincipalAmount + li.InterestAmount) - 0.01 THEN 3
    ELSE 2
  END
FROM dbo.LoanInstallments li
OUTER APPLY (
  SELECT
    SUM(CASE WHEN a.Type = 3 THEN a.Amount ELSE 0 END) AS PaidPrincipal,
    SUM(CASE WHEN a.Type = 2 THEN a.Amount ELSE 0 END) AS PaidInterest,
    SUM(CASE WHEN a.Type = 1 THEN a.Amount ELSE 0 END) AS PaidPenalty
  FROM dbo.LoanPaymentAllocations a
  JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
  WHERE p.LoanId = li.LoanId
    AND a.InstallmentId = li.InstallmentId
) x
WHERE li.LoanId = @loanId;";

                    await _connection.ExecuteAsync(sqlRecomputeInstallments, new { loanId }, tx);

                    const string sqlUpdateLoanStatus = @"
UPDATE l
SET
  Status = CASE
    WHEN r.RemainingCount <= 0 THEN 5
    WHEN p.HasPayments = 1 THEN 3
    ELSE 2
  END,
  UpdatedAt = GETUTCDATE()
FROM dbo.Loans l
CROSS APPLY (SELECT COUNT(1) AS RemainingCount FROM dbo.LoanInstallments WHERE LoanId = l.LoanId AND Status <> 3) r
CROSS APPLY (SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.LoanPayments WHERE LoanId = l.LoanId) THEN 1 ELSE 0 END AS HasPayments) p
WHERE l.LoanId = @loanId;";

                    await _connection.ExecuteAsync(sqlUpdateLoanStatus, new { loanId }, tx);

                    const string sqlSumPaidPrincipal = @"
SELECT ISNULL(SUM(a.Amount),0)
FROM dbo.LoanPaymentAllocations a
JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
WHERE p.LoanId = @loanId AND a.Type = 3;";
                    var paidPrincipalSum = await _connection.ExecuteScalarAsync<decimal>(sqlSumPaidPrincipal, new { loanId }, tx);
                    var outstanding = existing.Principal - paidPrincipalSum;
                    if (outstanding < 0) outstanding = 0;
                    existing.OutstandingPrincipal = Math.Round(outstanding, 0, MidpointRounding.AwayFromZero);

                    await _loans.UpdateAsync(existing, tx);

                    // Continue with guarantee/hold rebuild (same validation rules)
                }
                else
                {
                    // Remove current loan artifacts first so snapshot/locked calculations don't double-count this loan
                    await _installments.DeleteByLoanIdAsync(loanId, tx);
                    await _guarantees.ReplaceForLoanAsync(loanId, Array.Empty<LoanGuarantee>(), tx);
                    await _holds.DeleteByLoanIdAsync(loanId, tx);

                    // Determine plan
                    LoanRatePlan? plan;
                    if (existing.LoanRatePlanId > 0)
                    {
                        plan = await _loanRates.GetByIdAsync(existing.LoanRatePlanId, tx);
                        if (plan == null)
                            plan = await _loanRates.GetActiveForDateAsync(disbursedAt.Date, tx);
                    }
                    else
                    {
                        plan = await _loanRates.GetActiveForDateAsync(disbursedAt.Date, tx);
                    }
                    if (plan == null)
                        plan = await _loanRates.GetLatestActiveAsync(tx);
                    if (plan == null) throw new InvalidOperationException("برای تاریخ پرداخت، نرخ وام فعال تعریف نشده است");
                    if (!plan.IsActive) throw new InvalidOperationException("نرخ وام انتخاب‌شده غیرفعال است");

                    var firstDue = (firstDueDate ?? disbursedAt.Date.AddPersianMonths(1)).Date;

                    existing.Principal = Math.Round(principal, 0, MidpointRounding.AwayFromZero);
                    existing.TermMonths = termMonths;
                    existing.DisbursedAt = disbursedAt.Date;
                    existing.FirstDueDate = firstDue;
                    if (annualRatePercentOverride.HasValue)
                    {
                        var v = annualRatePercentOverride.Value;
                        if (v <= 0m) throw new InvalidOperationException("نرخ سود معتبر نیست");
                        existing.AnnualRatePercent = v;
                    }
                    else
                    {
                        existing.AnnualRatePercent = plan.AnnualRatePercent;
                    }

                    if (penaltyAnnualRatePercentOverride.HasValue)
                    {
                        var v = penaltyAnnualRatePercentOverride.Value;
                        if (v < 0m) throw new InvalidOperationException("نرخ دیرکرد معتبر نیست");
                        existing.PenaltyAnnualRatePercent = v;
                    }
                    else
                    {
                        existing.PenaltyAnnualRatePercent = plan.PenaltyAnnualRatePercent;
                    }
                    existing.Notes = notes;
                    existing.OutstandingPrincipal = existing.Principal;

                    var (emi, schedule) = (fixedEmiAmount.HasValue && fixedEmiAmount.Value > 0)
                        ? _calculator.GenerateScheduleWithFixedEmi(
                            loanId,
                            existing.Principal,
                            existing.TermMonths,
                            existing.FirstDueDate,
                            existing.AnnualRatePercent,
                            fixedEmiAmount.Value)
                        : _calculator.GenerateSchedule(
                            loanId,
                            existing.Principal,
                            existing.TermMonths,
                            existing.FirstDueDate,
                            existing.AnnualRatePercent);

                    existing.EmiAmount = emi;
                    await _loans.UpdateAsync(existing, tx);

                    await _installments.BulkInsertAsync(schedule, tx);
                }

                // Self-guarantee: if applicant is listed as guarantor, treat it as the applicant lock amount.
                var applicantSelfGuaranteeAmount = 0m;
                var safe = new List<LoanGuarantee>();
                if (guarantees != null)
                {
                    var normalizedApplicantCode = (existing.MemberCode ?? string.Empty).Trim();

                    applicantSelfGuaranteeAmount = guarantees
                        .Where(g => g != null)
                        .Where(g => g!.Type == LoanGuaranteeType.GuarantorMember)
                        .Where(g => !string.IsNullOrWhiteSpace(normalizedApplicantCode)
                            && !string.IsNullOrWhiteSpace(g!.GuarantorMemberCode)
                            && string.Equals(g!.GuarantorMemberCode!.Trim(), normalizedApplicantCode, StringComparison.OrdinalIgnoreCase))
                        .Sum(g => Math.Round(g!.Amount, 0, MidpointRounding.AwayFromZero));

                    safe = guarantees
                        .Where(g => g != null)
                        .Select(g => new LoanGuarantee
                        {
                            LoanId = loanId,
                            Type = g.Type,
                            GuarantorMemberId = g.GuarantorMemberId,
                            GuarantorMemberCode = g.GuarantorMemberCode,
                            DepositAccountId = g.DepositAccountId,
                            Amount = Math.Round(g.Amount, 0, MidpointRounding.AwayFromZero),
                            IsActive = true,
                            Notes = g.Notes
                        })
                        .Where(g => g.Amount > 0)
                        // Remove self-guarantee from guarantor list (handled as applicant lock)
                        .Where(g => string.IsNullOrWhiteSpace(normalizedApplicantCode)
                            || string.IsNullOrWhiteSpace(g.GuarantorMemberCode)
                            || !string.Equals(g.GuarantorMemberCode.Trim(), normalizedApplicantCode, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                // Applicant snapshot
                var applicantNet = 0m;
                var applicantAvailable = 0m;
                if (!string.IsNullOrWhiteSpace(existing.MemberCode))
                {
                    var snap = await _memberFinancial.GetSnapshotByMemberCodeAsync(existing.MemberCode!, tx);
                    applicantNet = snap.TotalNet;
                    applicantAvailable = snap.AvailableCapital;
                    if (applicantAvailable < 0) applicantAvailable = 0;
                }
                else
                {
                    applicantNet = await _txReports.GetMemberNetCapitalByMemberIdAsync(existing.MemberId, tx);
                    applicantAvailable = applicantNet;
                }
                var applicantLockAmount = applicantSelfGuaranteeAmount > 0 ? applicantSelfGuaranteeAmount : applicantAvailable;
                applicantLockAmount = Math.Round(applicantLockAmount, 0, MidpointRounding.AwayFromZero);
                if (applicantLockAmount <= 0)
                    throw new InvalidOperationException("سپرده آزاد متقاضی برای قفل شدن کافی نیست");
                if (applicantLockAmount > applicantAvailable)
                    throw new InvalidOperationException("اعتبار آزاد متقاضی برای قفل شدن کافی نیست");

                var guarantorsLocked = safe.Where(x => x.Type == LoanGuaranteeType.GuarantorMember).Sum(x => x.Amount);
                var totalPayable = existing.EmiAmount > 0 && existing.TermMonths > 0 ? (existing.EmiAmount * existing.TermMonths) : existing.Principal;
                if (!ignoreGuarantorCoverage && (applicantLockAmount + guarantorsLocked) < totalPayable)
                    throw new InvalidOperationException("مجموع اعتبار در رهن (متقاضی + ضامنین) کمتر از مانده کل وام (اصل + سود) است");

                if (safe.Count > 0)
                    await _guarantees.ReplaceForLoanAsync(loanId, safe, tx);

                var applicantPct = (byte)ClampPercent(applicantNet <= 0 ? 0 : (int)Math.Round((applicantLockAmount / applicantNet) * 100m, 0, MidpointRounding.AwayFromZero));
                if (applicantPct <= 0) applicantPct = 1;
                if (applicantPct > 100) applicantPct = 100;

                await _holds.InsertAsync(new CreditHold
                {
                    LoanId = loanId,
                    MemberId = existing.MemberId,
                    MemberCode = existing.MemberCode,
                    Role = CreditHoldRole.Applicant,
                    LockPercent = applicantPct,
                    BaseCapitalAmount = Math.Round(applicantNet, 0, MidpointRounding.AwayFromZero),
                    LockedAmount = Math.Round(applicantLockAmount, 0, MidpointRounding.AwayFromZero),
                    Status = CreditHoldStatus.Active,
                    Notes = notes
                }, tx);

                foreach (var g in safe.Where(x => x.Type == LoanGuaranteeType.GuarantorMember))
                {
                    var gMemberId = g.GuarantorMemberId ?? 0;
                    if (gMemberId <= 0 && !string.IsNullOrWhiteSpace(g.GuarantorMemberCode))
                        gMemberId = await ResolveMemberIdFromCodeAsync(g.GuarantorMemberCode!, tx);
                    if (gMemberId <= 0) continue;

                    var gNet = 0m;
                    var gExistingLocked = 0m;
                    if (!string.IsNullOrWhiteSpace(g.GuarantorMemberCode))
                    {
                        var snap = await _memberFinancial.GetSnapshotByMemberCodeAsync(g.GuarantorMemberCode!, tx);
                        gNet = snap.TotalNet;
                        gExistingLocked = snap.LockedAmount;
                    }
                    else
                    {
                        gNet = await _txReports.GetMemberNetCapitalByMemberIdAsync(gMemberId, tx);
                    }
                    if ((gExistingLocked + g.Amount) > gNet)
                        throw new InvalidOperationException("اعتبار آزاد ضامن برای قفل شدن کافی نیست");

                    var pct = (byte)ClampPercent(gNet <= 0 ? 0 : (int)Math.Round((g.Amount / gNet) * 100m, 0, MidpointRounding.AwayFromZero));
                    if (pct <= 0) pct = 1;
                    if (pct > 100) pct = 100;

                    await _holds.InsertAsync(new CreditHold
                    {
                        LoanId = loanId,
                        MemberId = gMemberId,
                        MemberCode = g.GuarantorMemberCode,
                        Role = CreditHoldRole.Guarantor,
                        LockPercent = pct,
                        BaseCapitalAmount = Math.Round(gNet, 0, MidpointRounding.AwayFromZero),
                        LockedAmount = Math.Round(g.Amount, 0, MidpointRounding.AwayFromZero),
                        Status = CreditHoldStatus.Active,
                        Notes = g.Notes
                    }, tx);
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<long> CreateLoanWithScheduleAsync(
            long memberId,
            long? loanRatePlanId,
            decimal principal,
            int termMonths,
            DateTime disbursedAt,
            decimal? fixedEmiAmount = null,
            DateTime? firstDueDate = null,
            string? notes = null,
            IEnumerable<LoanGuarantee>? guarantees = null,
            bool ignoreGuarantorCoverage = false,
            decimal? annualRatePercentOverride = null,
            decimal? penaltyAnnualRatePercentOverride = null)
        {
            if (memberId <= 0) throw new InvalidOperationException("عضو معتبر نیست");
            if (principal <= 0) throw new InvalidOperationException("مبلغ وام معتبر نیست");
            if (termMonths <= 0) throw new InvalidOperationException("تعداد اقساط معتبر نیست");

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                var loanId = await CreateLoanWithScheduleInternalAsync(
                    memberId,
                    null,
                    loanRatePlanId,
                    principal,
                    termMonths,
                    disbursedAt,
                    fixedEmiAmount,
                    firstDueDate,
                    notes,
                    guarantees,
                    ignoreGuarantorCoverage,
                    annualRatePercentOverride,
                    penaltyAnnualRatePercentOverride,
                    tx);
                tx.Commit();
                return loanId;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        private async Task<long> CreateLoanWithScheduleInternalAsync(
            long memberId,
            string? memberCode,
            long? loanRatePlanId,
            decimal principal,
            int termMonths,
            DateTime disbursedAt,
            decimal? fixedEmiAmount,
            DateTime? firstDueDate,
            string? notes,
            IEnumerable<LoanGuarantee>? guarantees,
            bool ignoreGuarantorCoverage,
            decimal? annualRatePercentOverride,
            decimal? penaltyAnnualRatePercentOverride,
            IDbTransaction tx)
        {
            if (memberId <= 0) throw new InvalidOperationException("عضو معتبر نیست");
            if (principal <= 0) throw new InvalidOperationException("مبلغ وام معتبر نیست");
            if (termMonths <= 0) throw new InvalidOperationException("تعداد اقساط معتبر نیست");

            LoanRatePlan? plan;
            if (loanRatePlanId.HasValue && loanRatePlanId.Value > 0)
            {
                plan = await _loanRates.GetByIdAsync(loanRatePlanId.Value, tx);
                if (plan == null) throw new InvalidOperationException("نرخ وام انتخاب‌شده یافت نشد");
            }
            else
            {
                plan = await _loanRates.GetActiveForDateAsync(disbursedAt.Date, tx);
                if (plan == null)
                    plan = await _loanRates.GetLatestActiveAsync(tx);
                if (plan == null) throw new InvalidOperationException("برای تاریخ پرداخت، نرخ وام فعال تعریف نشده است");
            }

            if (!plan.IsActive)
                throw new InvalidOperationException("نرخ وام انتخاب‌شده غیرفعال است");

            var firstDue = (firstDueDate ?? disbursedAt.Date.AddPersianMonths(1)).Date;

            var loan = new Loan
            {
                MemberId = memberId,
                MemberCode = memberCode,
                LoanRatePlanId = plan.PlanId,
                Principal = Math.Round(principal, 0, MidpointRounding.AwayFromZero),
                TermMonths = termMonths,
                DisbursedAt = disbursedAt.Date,
                FirstDueDate = firstDue,
                AnnualRatePercent = annualRatePercentOverride.HasValue
                    ? (annualRatePercentOverride.Value < 0m ? throw new InvalidOperationException("نرخ سود معتبر نیست") : annualRatePercentOverride.Value)
                    : plan.AnnualRatePercent,
                PenaltyAnnualRatePercent = penaltyAnnualRatePercentOverride.HasValue
                    ? (penaltyAnnualRatePercentOverride.Value < 0m ? throw new InvalidOperationException("نرخ دیرکرد معتبر نیست") : penaltyAnnualRatePercentOverride.Value)
                    : plan.PenaltyAnnualRatePercent,
                Status = LoanStatus.Approved,
                Notes = notes,
                OutstandingPrincipal = Math.Round(principal, 0, MidpointRounding.AwayFromZero)
            };

            loan.EmiAmount = 0;
            var loanId = await _loans.CreateAsync(loan, tx);

            var (emi, schedule) = (fixedEmiAmount.HasValue && fixedEmiAmount.Value > 0)
                ? _calculator.GenerateScheduleWithFixedEmi(
                    loanId,
                    loan.Principal,
                    loan.TermMonths,
                    loan.FirstDueDate,
                    loan.AnnualRatePercent,
                    fixedEmiAmount.Value)
                : _calculator.GenerateSchedule(
                    loanId,
                    loan.Principal,
                    loan.TermMonths,
                    loan.FirstDueDate,
                    loan.AnnualRatePercent);

            loan.LoanId = loanId;
            loan.EmiAmount = emi;
            loan.OutstandingPrincipal = loan.Principal;
            await _loans.UpdateAsync(loan, tx);

            await _installments.BulkInsertAsync(schedule, tx);

            // Self-guarantee: if applicant is listed as guarantor, treat it as the applicant lock amount.
            var applicantSelfGuaranteeAmount = 0m;
            List<LoanGuarantee> safe = new();
            if (guarantees != null)
            {
                var normalizedApplicantCode = (memberCode ?? string.Empty).Trim();

                applicantSelfGuaranteeAmount = guarantees
                    .Where(g => g != null)
                    .Where(g => g!.Type == LoanGuaranteeType.GuarantorMember)
                    .Where(g => !string.IsNullOrWhiteSpace(normalizedApplicantCode)
                        && !string.IsNullOrWhiteSpace(g!.GuarantorMemberCode)
                        && string.Equals(g!.GuarantorMemberCode!.Trim(), normalizedApplicantCode, StringComparison.OrdinalIgnoreCase))
                    .Sum(g => Math.Round(g!.Amount, 0, MidpointRounding.AwayFromZero));

                safe = guarantees
                    .Where(g => g != null)
                    .Select(g => new LoanGuarantee
                    {
                        LoanId = loanId,
                        Type = g.Type,
                        GuarantorMemberId = g.GuarantorMemberId,
                        GuarantorMemberCode = g.GuarantorMemberCode,
                        DepositAccountId = g.DepositAccountId,
                        Amount = Math.Round(g.Amount, 0, MidpointRounding.AwayFromZero),
                        IsActive = true,
                        Notes = g.Notes
                    })
                    .Where(g => g.Amount > 0)
                    // Remove self-guarantee from guarantor list (handled as applicant lock)
                    .Where(g => string.IsNullOrWhiteSpace(normalizedApplicantCode)
                        || string.IsNullOrWhiteSpace(g.GuarantorMemberCode)
                        || !string.Equals(g.GuarantorMemberCode.Trim(), normalizedApplicantCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Coverage rule:
            // Applicant capital is fully locked + guarantors locked amounts must cover total payable (principal + total interest)
            var applicantNet = 0m;
            var applicantExistingLocked = 0m;
            var applicantAvailable = 0m;
            if (!string.IsNullOrWhiteSpace(memberCode))
            {
                var snap = await _memberFinancial.GetSnapshotByMemberCodeAsync(memberCode!, tx);
                applicantNet = snap.TotalNet;
                applicantExistingLocked = snap.LockedAmount;
                applicantAvailable = snap.AvailableCapital;
                if (applicantAvailable < 0) applicantAvailable = 0;
            }
            else
            {
                applicantNet = await GetMemberNetCapitalInternalAsync(memberId, tx);
                applicantAvailable = applicantNet;
            }

            var applicantLockAmount = applicantSelfGuaranteeAmount > 0 ? applicantSelfGuaranteeAmount : applicantAvailable;
            applicantLockAmount = Math.Round(applicantLockAmount, 0, MidpointRounding.AwayFromZero);
            if (applicantLockAmount <= 0)
                throw new InvalidOperationException("سپرده آزاد متقاضی برای قفل شدن کافی نیست");
            if (applicantLockAmount > applicantAvailable)
                throw new InvalidOperationException("اعتبار آزاد متقاضی برای قفل شدن کافی نیست");

            var guarantorsLocked = safe.Where(x => x.Type == LoanGuaranteeType.GuarantorMember).Sum(x => x.Amount);
            var totalPayable = loan.EmiAmount > 0 && loan.TermMonths > 0 ? (loan.EmiAmount * loan.TermMonths) : loan.Principal;
            if (!ignoreGuarantorCoverage && (applicantLockAmount + guarantorsLocked) < totalPayable)
                throw new InvalidOperationException("مجموع اعتبار در رهن (متقاضی + ضامنین) کمتر از مانده کل وام (اصل + سود) است");

            if (safe.Count > 0)
                await _guarantees.ReplaceForLoanAsync(loanId, safe, tx);

            // Persist holds
            var applicantPct = (byte)ClampPercent(applicantNet <= 0 ? 0 : (int)Math.Round((applicantLockAmount / applicantNet) * 100m, 0, MidpointRounding.AwayFromZero));
            if (applicantPct <= 0) applicantPct = 1;
            if (applicantPct > 100) applicantPct = 100;
            await _holds.InsertAsync(new CreditHold
            {
                LoanId = loanId,
                MemberId = memberId,
                MemberCode = memberCode,
                Role = CreditHoldRole.Applicant,
                LockPercent = applicantPct,
                BaseCapitalAmount = Math.Round(applicantNet, 0, MidpointRounding.AwayFromZero),
                LockedAmount = Math.Round(applicantLockAmount, 0, MidpointRounding.AwayFromZero),
                Status = CreditHoldStatus.Active,
                Notes = notes
            }, tx);

            foreach (var g in safe.Where(x => x.Type == LoanGuaranteeType.GuarantorMember))
            {
                var gMemberId = g.GuarantorMemberId ?? 0;
                if (gMemberId <= 0 && !string.IsNullOrWhiteSpace(g.GuarantorMemberCode))
                    gMemberId = await ResolveMemberIdFromCodeAsync(g.GuarantorMemberCode!, tx);
                if (gMemberId <= 0) continue;

                var gNet = 0m;
                var gExistingLocked = 0m;
                if (!string.IsNullOrWhiteSpace(g.GuarantorMemberCode))
                {
                    var snap = await _memberFinancial.GetSnapshotByMemberCodeAsync(g.GuarantorMemberCode!, tx);
                    gNet = snap.TotalNet;
                    gExistingLocked = snap.LockedAmount;
                }
                else
                {
                    gNet = await GetMemberNetCapitalInternalAsync(gMemberId, tx);
                }
                if ((gExistingLocked + g.Amount) > gNet)
                    throw new InvalidOperationException("اعتبار آزاد ضامن برای قفل شدن کافی نیست");

                var pct = (byte)ClampPercent(gNet <= 0 ? 0 : (int)Math.Round((g.Amount / gNet) * 100m, 0, MidpointRounding.AwayFromZero));
                if (pct <= 0) pct = 1;
                if (pct > 100) pct = 100;

                await _holds.InsertAsync(new CreditHold
                {
                    LoanId = loanId,
                    MemberId = gMemberId,
                    MemberCode = g.GuarantorMemberCode,
                    Role = CreditHoldRole.Guarantor,
                    LockPercent = pct,
                    BaseCapitalAmount = Math.Round(gNet, 0, MidpointRounding.AwayFromZero),
                    LockedAmount = Math.Round(g.Amount, 0, MidpointRounding.AwayFromZero),
                    Status = CreditHoldStatus.Active,
                    Notes = g.Notes
                }, tx);
            }

            return loanId;
        }

        private static int ClampPercent(int pct)
        {
            if (pct < 0) return 0;
            if (pct > 100) return 100;
            return pct;
        }

        private async Task<decimal> GetMemberNetCapitalInternalAsync(long memberId, IDbTransaction tx)
        {
            if (memberId <= 0) return 0m;
            return await _txReports.GetMemberNetCapitalByMemberIdAsync(memberId, tx);
        }
    }
}
