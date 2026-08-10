using FluentValidation;
using MiniBank.Web.Models;

namespace MiniBank.Web.Validators
{
    public class HouseholdReceiptAllocationValidator : AbstractValidator<HouseholdReceiptAllocation>
    {
        public HouseholdReceiptAllocationValidator()
        {
            RuleFor(x => x.HouseholdReceiptId)
                .GreaterThan(0)
                .WithMessage("شناسه رسید الزامی است");

            RuleFor(x => x.Amount)
                .GreaterThan(0)
                .WithMessage("مبلغ باید بیشتر از صفر باشد");

            RuleFor(x => x.SortOrder)
                .GreaterThanOrEqualTo(0)
                .WithMessage("ترتیب نمایش نمی‌تواند منفی باشد");

            RuleFor(x => x.Notes)
                .MaximumLength(500)
                .WithMessage("توضیحات نمی‌تواند بیشتر از 500 کاراکتر باشد");
        }
    }
}
