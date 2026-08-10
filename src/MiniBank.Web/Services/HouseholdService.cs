using MediatR;
using MiniBank.Web.Models;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Core.Queries;

namespace MiniBank.Web.Services
{
    public class HouseholdService
    {
        private readonly IMediator _mediator;
        private readonly ILogger<HouseholdService> _logger;

        public HouseholdService(IMediator mediator, ILogger<HouseholdService> logger)
        {
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<IReadOnlyList<Household>> GetAsync(string? keyword = null, bool? isActive = null, int top = 200)
        {
            try
            {
                var query = new GetAllHouseholdsQuery
                {
                    SearchTerm = keyword,
                    ActiveOnly = isActive ?? false,
                    Limit = top
                };

                var households = await _mediator.Send(query);
                return households.ToList().AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving households with keyword: {Keyword}, isActive: {IsActive}", keyword, isActive);
                throw;
            }
        }

        public async Task<Household?> GetByIdAsync(long id)
        {
            try
            {
                var query = new GetHouseholdByIdQuery { HouseholdId = id };
                return await _mediator.Send(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving household with ID: {HouseholdId}", id);
                throw;
            }
        }

        public async Task<long> CreateAsync(Household model)
        {
            try
            {
                var command = new CreateHouseholdCommand
                {
                    HouseholdCode = model.HouseholdCode ?? string.Empty,
                    Name = model.Name ?? string.Empty,
                    OwnerMemberId = model.OwnerMemberId,
                    IsActive = model.IsActive
                };

                return await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating household with code: {HouseholdCode}", model.HouseholdCode);
                throw;
            }
        }

        public async Task UpdateAsync(Household model)
        {
            try
            {
                var command = new UpdateHouseholdCommand
                {
                    HouseholdId = model.HouseholdId,
                    HouseholdCode = model.HouseholdCode ?? string.Empty,
                    Name = model.Name ?? string.Empty,
                    OwnerMemberId = model.OwnerMemberId,
                    IsActive = model.IsActive
                };

                await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating household with ID: {HouseholdId}", model.HouseholdId);
                throw;
            }
        }

        public async Task ToggleActiveAsync(long id, bool isActive)
        {
            try
            {
                // Get the existing household first
                var household = await GetByIdAsync(id);
                if (household == null)
                {
                    throw new InvalidOperationException($"Household with ID {id} not found.");
                }

                // Update only the IsActive property
                var command = new UpdateHouseholdCommand
                {
                    HouseholdId = household.HouseholdId,
                    HouseholdCode = household.HouseholdCode ?? string.Empty,
                    Name = household.Name ?? string.Empty,
                    OwnerMemberId = household.OwnerMemberId,
                    IsActive = isActive
                };

                await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active status for household with ID: {HouseholdId}", id);
                throw;
            }
        }

        public async Task DeleteAsync(long id)
        {
            try
            {
                var command = new DeleteHouseholdCommand { HouseholdId = id };
                await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting household with ID: {HouseholdId}", id);
                throw;
            }
        }
    }
}
