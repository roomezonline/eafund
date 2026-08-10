using MediatR;
using MiniBank.Web.Core.Queries;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Core.Handlers
{
    public class GetNextMemberIdHandler : IRequestHandler<GetNextMemberIdQuery, long>
    {
        private readonly MemberRepository _memberRepository;

        public GetNextMemberIdHandler(MemberRepository memberRepository)
        {
            _memberRepository = memberRepository;
        }

        public async Task<long> Handle(GetNextMemberIdQuery request, CancellationToken cancellationToken)
        {
            return await _memberRepository.GetNextMemberIdAsync();
        }
    }
}
