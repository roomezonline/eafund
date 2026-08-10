using MediatR;

namespace MiniBank.Web.Core.Queries
{
    public class GetNextMemberIdQuery : IRequest<long>
    {
    }
}
