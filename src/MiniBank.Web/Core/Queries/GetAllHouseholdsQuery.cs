using MediatR;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Queries
{
    public class GetAllHouseholdsQuery : IRequest<IEnumerable<Household>>
    {
        public bool ActiveOnly { get; set; } = false;
        public string? SearchTerm { get; set; }
        public int? Limit { get; set; }
    }
}
