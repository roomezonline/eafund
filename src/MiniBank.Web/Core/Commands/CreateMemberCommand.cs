using MediatR;

namespace MiniBank.Web.Core.Commands
{
    public class CreateMemberCommand : IRequest<long>
    {
        public long? MemberId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string HouseholdCode { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
