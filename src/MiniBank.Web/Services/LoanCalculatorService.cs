using MiniBank.Web.Models;
using MiniBank.Web.Components.Extensions;

namespace MiniBank.Web.Services
{
    public class LoanCalculatorService
    {
        // Banking convention: values should be whole currency units (no decimals).
        // We store as decimal but always round to 0 decimals.
        private static decimal Round0(decimal value)
            => Math.Round(value, 0, MidpointRounding.AwayFromZero);

        private static decimal CalcEmi(decimal principal, decimal monthlyRate, int termMonths)
        {
            if (principal <= 0) return 0;
            if (termMonths <= 0) return 0;

            var r = (double)monthlyRate;
            var pow = Math.Pow(1d + r, termMonths);
            var emi = principal * monthlyRate * (decimal)pow / ((decimal)pow - 1m);
            return Round0(emi);
        }

        public (decimal emiAmount, List<LoanInstallment> installments) GenerateSchedule(
            long loanId,
            decimal principal,
            int termMonths,
            DateTime firstDueDate,
            decimal annualRatePercent)
        {
            if (principal <= 0) throw new InvalidOperationException("مبلغ وام معتبر نیست");
            if (termMonths <= 0) throw new InvalidOperationException("تعداد اقساط معتبر نیست");
            if (firstDueDate == default) throw new InvalidOperationException("تاریخ اولین سررسید معتبر نیست");
            if (annualRatePercent < 0) throw new InvalidOperationException("نرخ سود معتبر نیست");

            var monthlyRate = (annualRatePercent / 100m) / 12m;
            decimal emi;

            if (monthlyRate == 0m)
            {
                emi = Round0(principal / termMonths);
            }
            else
            {
                // EMI = P*r*(1+r)^n / ((1+r)^n - 1)
                var r = (double)monthlyRate;
                var pow = Math.Pow(1d + r, termMonths);
                emi = principal * monthlyRate * (decimal)pow / ((decimal)pow - 1m);
            }
            emi = Round0(emi);

            var installments = new List<LoanInstallment>(termMonths);
            var balance = Round0(principal);

            for (var i = 1; i <= termMonths; i++)
            {
                var due = firstDueDate.Date.AddPersianMonths(i - 1);

                if (i < termMonths)
                {
                    var interest = Round0(balance * monthlyRate);
                    var principalPart = Round0(emi - interest);

                    // Guard against rounding drift
                    if (principalPart < 0) principalPart = 0;
                    if (principalPart > balance) principalPart = balance;

                    var closing = Round0(balance - principalPart);

                    installments.Add(new LoanInstallment
                    {
                        LoanId = loanId,
                        InstallmentNo = i,
                        DueDate = due,
                        OpeningBalance = balance,
                        EmiAmount = emi,
                        InterestAmount = interest,
                        PrincipalAmount = principalPart,
                        ClosingBalance = closing,
                        Status = LoanInstallmentStatus.Unpaid
                    });

                    balance = closing;
                }
                else
                {
                    // Last installment: close loan exactly (avoid non-zero residuals)
                    var interest = Round0(balance * monthlyRate);
                    var principalPart = balance;
                    var lastEmi = Round0(principalPart + interest);

                    installments.Add(new LoanInstallment
                    {
                        LoanId = loanId,
                        InstallmentNo = i,
                        DueDate = due,
                        OpeningBalance = balance,
                        EmiAmount = lastEmi,
                        InterestAmount = interest,
                        PrincipalAmount = principalPart,
                        ClosingBalance = 0,
                        Status = LoanInstallmentStatus.Unpaid
                    });

                    balance = 0;
                }
            }

            // If last EMI differs from EMI (due to rounding), return the base EMI for loan header,
            // but installments contain the true last EMI.
            return (emi, installments);
        }

        public (decimal emiAmount, List<LoanInstallment> installments) GenerateScheduleWithFixedEmi(
            long loanId,
            decimal principal,
            int termMonths,
            DateTime firstDueDate,
            decimal annualRatePercent,
            decimal fixedEmiAmount)
        {
            if (principal <= 0) throw new InvalidOperationException("مبلغ وام معتبر نیست");
            if (termMonths <= 0) throw new InvalidOperationException("تعداد اقساط معتبر نیست");
            if (firstDueDate == default) throw new InvalidOperationException("تاریخ اولین سررسید معتبر نیست");
            if (annualRatePercent < 0) throw new InvalidOperationException("نرخ سود معتبر نیست");
            if (fixedEmiAmount <= 0) throw new InvalidOperationException("مبلغ قسط معتبر نیست");

            var monthlyRate = (annualRatePercent / 100m) / 12m;
            var emi = Round0(fixedEmiAmount);

            var installments = new List<LoanInstallment>(termMonths);
            var balance = Round0(principal);

            // Validate amortization possibility: EMI must be greater than first month's interest
            var firstInterest = Round0(balance * monthlyRate);
            if (emi <= firstInterest)
                throw new InvalidOperationException("مبلغ قسط کمتر یا مساوی سود ماهانه است");

            for (var i = 1; i <= termMonths; i++)
            {
                var due = firstDueDate.Date.AddPersianMonths(i - 1);

                var interest = Round0(balance * monthlyRate);
                if (i < termMonths)
                {
                    // Keep the user's EMI as-is. Only if it would pay off the loan before the last installment,
                    // reduce the payment starting from this month so the schedule still spans exactly termMonths.
                    var payment = emi;
                    var principalPart = Round0(payment - interest);
                    if (principalPart <= 0)
                        throw new InvalidOperationException("مبلغ قسط کمتر یا مساوی سود ماهانه است");

                    if (principalPart >= balance)
                    {
                        var remainingMonths = termMonths - i + 1;
                        var requiredEmiForRemaining = CalcEmi(balance, monthlyRate, remainingMonths);
                        if (requiredEmiForRemaining > 0)
                            payment = requiredEmiForRemaining;

                        principalPart = Round0(payment - interest);
                        if (principalPart <= 0)
                            throw new InvalidOperationException("مبلغ قسط کمتر یا مساوی سود ماهانه است");

                        if (principalPart > balance)
                        {
                            principalPart = balance;
                            payment = Round0(principalPart + interest);
                        }
                    }

                    var closing = Round0(balance - principalPart);

                    installments.Add(new LoanInstallment
                    {
                        LoanId = loanId,
                        InstallmentNo = i,
                        DueDate = due,
                        OpeningBalance = balance,
                        EmiAmount = payment,
                        InterestAmount = interest,
                        PrincipalAmount = principalPart,
                        ClosingBalance = closing,
                        Status = LoanInstallmentStatus.Unpaid
                    });

                    balance = closing;
                }
                else
                {
                    // Last installment: close loan exactly
                    var principalPart = balance;
                    var lastEmi = Round0(principalPart + interest);

                    installments.Add(new LoanInstallment
                    {
                        LoanId = loanId,
                        InstallmentNo = i,
                        DueDate = due,
                        OpeningBalance = balance,
                        EmiAmount = lastEmi,
                        InterestAmount = interest,
                        PrincipalAmount = principalPart,
                        ClosingBalance = 0,
                        Status = LoanInstallmentStatus.Unpaid
                    });

                    balance = 0;
                }
            }

            return (emi, installments);
        }
    }
}
