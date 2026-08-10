using MediatR;
using MiniBank.Web.Core.Queries;
using MiniBank.Web.Core.Interfaces;
using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Handlers
{
    public class GetHouseholdByIdHandler : IRequestHandler<GetHouseholdByIdQuery, Household?>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<GetHouseholdByIdHandler> _logger;

        public GetHouseholdByIdHandler(
            IUnitOfWork unitOfWork,
            ILogger<GetHouseholdByIdHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Household?> Handle(GetHouseholdByIdQuery request, CancellationToken cancellationToken)
        {
            try
            {
                var household = await _unitOfWork.Households.GetByIdAsync(request.HouseholdId);
                
                if (household == null)
                {
                    _logger.LogWarning("Household with ID {HouseholdId} not found", request.HouseholdId);
                }

                return household;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving household with ID {HouseholdId}", request.HouseholdId);
                throw;
            }
        }
    }
}
