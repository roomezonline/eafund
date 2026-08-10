using MediatR;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Queries
{
    public class GetAllMembersQuery : IRequest<IEnumerable<Member>>
    {
        public string? SearchTerm { get; set; }
        public bool ActiveOnly { get; set; }
        public long? HouseholdId { get; set; }
        public int Limit { get; set; } = 200;
    }
}
