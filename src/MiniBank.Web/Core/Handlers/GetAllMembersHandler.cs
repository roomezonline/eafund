using MediatR;
using MiniBank.Web.Core.Queries;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Handlers
{
    public class GetAllMembersHandler : IRequestHandler<GetAllMembersQuery, IEnumerable<Member>>
    {
        private readonly MemberRepository _memberRepository;

        public GetAllMembersHandler(MemberRepository memberRepository)
        {
            _memberRepository = memberRepository;
        }

        public async Task<IEnumerable<Member>> Handle(GetAllMembersQuery request, CancellationToken cancellationToken)
        {
            return await _memberRepository.GetAllAsync(
                request.SearchTerm,
                request.ActiveOnly,
                request.HouseholdId,
                request.Limit
            );
        }
    }
}
