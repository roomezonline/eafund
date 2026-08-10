using MediatR;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Core.Handlers
{
    public class UpdateMemberHandler : IRequestHandler<UpdateMemberCommand>
    {
        private readonly MemberRepository _memberRepository;

        public UpdateMemberHandler(MemberRepository memberRepository)
        {
            _memberRepository = memberRepository;
        }

        public async Task Handle(UpdateMemberCommand request, CancellationToken cancellationToken)
        {
            // Check if national code already exists for another member
            if (await _memberRepository.ExistsByNationalCodeAsync(request.NationalCode, request.MemberId))
            {
                throw new InvalidOperationException($"عضوی با کد ملی {request.NationalCode} قبلاً ثبت شده است.");
            }

            var member = new Models.Member
            {
                MemberId = request.MemberId,
                FirstName = request.FirstName,
                FatherName = request.FatherName,
                LastName = request.LastName,
                NationalCode = request.NationalCode,
                MobileNumber = request.MobileNumber,
                HouseholdCode = request.HouseholdCode,
                IsActive = request.IsActive,
                JoinedAt = request.JoinedAt
            };

            await _memberRepository.UpdateAsync(member);
        }
    }
}
