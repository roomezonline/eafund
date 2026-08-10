using MediatR;

namespace MiniBank.Web.Core.Commands
{
    public class DeleteMemberCommand : IRequest
    {
        public long MemberId { get; set; }
    }
}
