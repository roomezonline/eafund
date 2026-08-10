using FluentValidation;
using MiniBank.Web.Core.Commands;

namespace MiniBank.Web.Core.Validators
{
    public class UpdateHouseholdValidator : AbstractValidator<UpdateHouseholdCommand>
    {
        public UpdateHouseholdValidator()
        {
            RuleFor(x => x.HouseholdId)
                .GreaterThan(0)
                .WithMessage("شناسه خانوار باید بزرگتر از صفر باشد");

            RuleFor(x => x.HouseholdCode)
                .NotEmpty()
                .WithMessage("کد خانوار الزامی است")
                .MaximumLength(20)
                .WithMessage("کد خانوار نمی‌تواند بیش از ۲۰ کاراکتر باشد")
                .Matches("^[a-zA-Z0-9_-]+$")
                .WithMessage("کد خانوار فقط می‌تواند شامل حروف انگلیسی، اعداد، خط تیره و زیرخط باشد");

            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("نام خانوار الزامی است")
                .MaximumLength(100)
                .WithMessage("نام خانوار نمی‌تواند بیش از ۱۰۰ کاراکتر باشد");

            RuleFor(x => x.OwnerMemberId)
                .GreaterThan(0)
                .When(x => x.OwnerMemberId.HasValue)
                .WithMessage("شناسه مالک باید بزرگتر از صفر باشد");
        }
    }
}
