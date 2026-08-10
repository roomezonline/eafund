using FluentValidation;
using MiniBank.Web.Models;

namespace MiniBank.Web.Validators
{
    public class HouseholdReceiptValidator : AbstractValidator<HouseholdReceipt>
    {
        public HouseholdReceiptValidator()
        {
            RuleFor(x => x.DocumentType)
                .Must(x => x == 1 || x == 2)
                .WithMessage("نوع سند نامعتبر است");

            When(x => x.DocumentType == 1, () =>
            {
                RuleFor(x => x.TargetHouseholdId)
                    .NotNull()
                    .WithMessage("خانوار مقصد باید انتخاب شود")
                    .GreaterThan(0)
                    .WithMessage("خانوار مقصد باید انتخاب شود");
            });

            RuleFor(x => x.TransactionType)
                .NotEmpty()
                .WithMessage("نوع تراکنش باید انتخاب شود");

            RuleFor(x => x.AmountTotal)
                .GreaterThan(0)
                .WithMessage("مبلغ کل باید بیشتر از صفر باشد")
                .LessThanOrEqualTo(1000000000)
                .WithMessage("مبلغ کل نمی‌تواند بیشتر از یک میلیارد ریال باشد");

            RuleFor(x => x.PayChannel)
                .Must(x => x == 1 || x == 2)
                .WithMessage("کانال پرداخت نامعتبر است");

            RuleFor(x => x.Notes)
                .MaximumLength(1000)
                .WithMessage("توضیحات نمی‌تواند بیشتر از 1000 کاراکتر باشد");
        }
    }
}
