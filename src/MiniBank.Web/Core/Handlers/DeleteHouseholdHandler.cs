using MediatR;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Core.Interfaces;

namespace MiniBank.Web.Core.Handlers
{
    public class DeleteHouseholdHandler : IRequestHandler<DeleteHouseholdCommand, Unit>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<DeleteHouseholdHandler> _logger;

        public DeleteHouseholdHandler(
            IUnitOfWork unitOfWork,
            ILogger<DeleteHouseholdHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Unit> Handle(DeleteHouseholdCommand request, CancellationToken cancellationToken)
        {
            // Check if household exists
            var existingHousehold = await _unitOfWork.Households.GetByIdAsync(request.HouseholdId);
            if (existingHousehold == null)
            {
                throw new InvalidOperationException($"Household with ID {request.HouseholdId} not found.");
            }

            try
            {
                await _unitOfWork.Households.DeleteAsync(request.HouseholdId);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Deleted household with ID {HouseholdId}", request.HouseholdId);

                return Unit.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting household with ID {HouseholdId}", request.HouseholdId);
                throw;
            }
        }
    }
}
