using MediatR;

namespace MiniBank.Web.Core.Commands
{
    public class DeleteHouseholdCommand : IRequest<Unit>
    {
        public long HouseholdId { get; set; }
    }
}
