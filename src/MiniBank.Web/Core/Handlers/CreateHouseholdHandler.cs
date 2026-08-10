using MediatR;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Core.Interfaces;
using MiniBank.Web.Models;
using FluentValidation;

namespace MiniBank.Web.Core.Handlers
{
    public class CreateHouseholdHandler : IRequestHandler<CreateHouseholdCommand, long>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator<CreateHouseholdCommand> _validator;
        private readonly ILogger<CreateHouseholdHandler> _logger;

        public CreateHouseholdHandler(
            IUnitOfWork unitOfWork,
            IValidator<CreateHouseholdCommand> validator,
            ILogger<CreateHouseholdHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _logger = logger;
        }

        public async Task<long> Handle(CreateHouseholdCommand request, CancellationToken cancellationToken)
        {
            // Validate the request
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            // Check if household code is unique
            var isCodeUnique = await _unitOfWork.Households.IsCodeUniqueAsync(request.HouseholdCode);
            if (!isCodeUnique)
            {
                throw new InvalidOperationException($"Household code '{request.HouseholdCode}' already exists.");
            }

            // Create the household
            var household = new Household
            {
                HouseholdCode = request.HouseholdCode,
                Name = request.Name,
                OwnerMemberId = request.OwnerMemberId,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                var householdId = await _unitOfWork.Households.AddAsync(household);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Created household with ID {HouseholdId} and code {HouseholdCode}", 
                    householdId, request.HouseholdCode);

                return householdId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating household with code {HouseholdCode}", request.HouseholdCode);
                throw;
            }
        }
    }
}
