using MediatR;
using MiniBank.Web.Core.Queries;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Handlers
{
    public class GetMemberByIdHandler : IRequestHandler<GetMemberByIdQuery, Member?>
    {
        private readonly MemberRepository _memberRepository;

        public GetMemberByIdHandler(MemberRepository memberRepository)
        {
            _memberRepository = memberRepository;
        }

        public async Task<Member?> Handle(GetMemberByIdQuery request, CancellationToken cancellationToken)
        {
            return await _memberRepository.GetByIdAsync(request.MemberId);
        }
    }
}
