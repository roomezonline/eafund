using MediatR;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Queries
{
    public class GetMemberByIdQuery : IRequest<Member?>
    {
        public long MemberId { get; set; }
    }
}
