using MediatR;

namespace MiniBank.Web.Core.Commands
{
    public class UpdateHouseholdCommand : IRequest<Unit>
    {
        public long HouseholdId { get; set; }
        public string HouseholdCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long? OwnerMemberId { get; set; }
        public bool IsActive { get; set; }
    }
}
