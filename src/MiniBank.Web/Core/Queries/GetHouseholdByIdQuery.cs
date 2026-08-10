using MediatR;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Queries
{
    public class GetHouseholdByIdQuery : IRequest<Household?>
    {
        public long HouseholdId { get; set; }
    }
}
