using MediatR;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Core.Handlers
{
    public class CreateMemberHandler : IRequestHandler<CreateMemberCommand, long>
    {
        private readonly MemberRepository _memberRepository;
        private readonly MiniBank.Web.Services.Auth.AuthSyncService _authSync;

        public CreateMemberHandler(MemberRepository memberRepository, MiniBank.Web.Services.Auth.AuthSyncService authSync)
        {
            _memberRepository = memberRepository;
            _authSync = authSync;
        }

        public async Task<long> Handle(CreateMemberCommand request, CancellationToken cancellationToken)
        {
            // Check if national code already exists
            if (await _memberRepository.ExistsByNationalCodeAsync(request.NationalCode))
            {
                throw new InvalidOperationException($"عضوی با کد ملی {request.NationalCode} قبلاً ثبت شده است.");
            }

            var member = new Models.Member
            {
                MemberId = request.MemberId ?? 0,
                FirstName = request.FirstName,
                FatherName = request.FatherName,
                LastName = request.LastName,
                NationalCode = request.NationalCode,
                MobileNumber = request.MobileNumber,
                HouseholdCode = request.HouseholdCode,
                IsActive = request.IsActive,
                JoinedAt = request.JoinedAt,
                CreatedAt = DateTime.UtcNow
            };

            var id = await _memberRepository.CreateAsync(member);
            try { await _authSync.EnsureUserForMemberAsync(id); } catch { }
            return id;
        }
    }
}
