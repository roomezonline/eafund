using MediatR;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Core.Interfaces;
using FluentValidation;

namespace MiniBank.Web.Core.Handlers
{
    public class UpdateHouseholdHandler : IRequestHandler<UpdateHouseholdCommand, Unit>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator<UpdateHouseholdCommand> _validator;
        private readonly ILogger<UpdateHouseholdHandler> _logger;

        public UpdateHouseholdHandler(
            IUnitOfWork unitOfWork,
            IValidator<UpdateHouseholdCommand> validator,
            ILogger<UpdateHouseholdHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _logger = logger;
        }

        public async Task<Unit> Handle(UpdateHouseholdCommand request, CancellationToken cancellationToken)
        {
            // Validate the request
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            // Check if household exists
            var existingHousehold = await _unitOfWork.Households.GetByIdAsync(request.HouseholdId);
            if (existingHousehold == null)
            {
                throw new InvalidOperationException($"Household with ID {request.HouseholdId} not found.");
            }

            // Check if household code is unique (excluding current household)
            var isCodeUnique = await _unitOfWork.Households.IsCodeUniqueAsync(request.HouseholdCode, request.HouseholdId);
            if (!isCodeUnique)
            {
                throw new InvalidOperationException($"Household code '{request.HouseholdCode}' already exists.");
            }

            // Update the household
            existingHousehold.HouseholdCode = request.HouseholdCode;
            existingHousehold.Name = request.Name;
            existingHousehold.OwnerMemberId = request.OwnerMemberId;
            existingHousehold.IsActive = request.IsActive;

            try
            {
                await _unitOfWork.Households.UpdateAsync(existingHousehold);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Updated household with ID {HouseholdId}", request.HouseholdId);

                return Unit.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating household with ID {HouseholdId}", request.HouseholdId);
                throw;
            }
        }
    }
}
