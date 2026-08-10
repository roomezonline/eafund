using MediatR;
using MiniBank.Web.Core.Queries;
using MiniBank.Web.Core.Interfaces;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Handlers
{
    public class GetAllHouseholdsHandler : IRequestHandler<GetAllHouseholdsQuery, IEnumerable<Household>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<GetAllHouseholdsHandler> _logger;

        public GetAllHouseholdsHandler(
            IUnitOfWork unitOfWork,
            ILogger<GetAllHouseholdsHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<IEnumerable<Household>> Handle(GetAllHouseholdsQuery request, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<Household> households;

                if (!string.IsNullOrWhiteSpace(request.SearchTerm))
                {
                    households = await _unitOfWork.Households.SearchHouseholdsAsync(request.SearchTerm);
                }
                else if (request.ActiveOnly)
                {
                    households = await _unitOfWork.Households.GetActiveHouseholdsAsync();
                }
                else
                {
                    households = await _unitOfWork.Households.GetAllAsync();
                }

                if (request.Limit.HasValue && request.Limit > 0)
                {
                    households = households.Take(request.Limit.Value);
                }

                return households;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving households with filters: ActiveOnly={ActiveOnly}, SearchTerm={SearchTerm}, Limit={Limit}", 
                    request.ActiveOnly, request.SearchTerm, request.Limit);
                throw;
            }
        }
    }
}
