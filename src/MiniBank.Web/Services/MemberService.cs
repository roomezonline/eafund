using MediatR;
using MiniBank.Web.Models;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Core.Queries;

namespace MiniBank.Web.Services
{
    public class MemberService
    {
        private readonly IMediator _mediator;
        private readonly ILogger<MemberService> _logger;

        public MemberService(IMediator mediator, ILogger<MemberService> logger)
        {
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<IReadOnlyList<Member>> GetAsync(string? keyword = null, bool? isActive = null, long? householdId = null, int top = 200)
        {
            try
            {
                var query = new GetAllMembersQuery
                {
                    SearchTerm = keyword,
                    ActiveOnly = isActive ?? false,
                    HouseholdId = householdId,
                    Limit = top
                };

                var members = await _mediator.Send(query);
                return members.ToList().AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving members with keyword: {Keyword}, isActive: {IsActive}, householdId: {HouseholdId}", keyword, isActive, householdId);
                throw;
            }
        }

        public async Task<Member?> GetByIdAsync(long id)
        {
            try
            {
                var query = new GetMemberByIdQuery { MemberId = id };
                return await _mediator.Send(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving member with ID: {MemberId}", id);
                throw;
            }
        }

        public async Task<long> CreateAsync(Member model)
        {
            try
            {
                var command = new CreateMemberCommand
                {
                    MemberId = model.MemberId > 0 ? model.MemberId : null,
                    FirstName = model.FirstName ?? string.Empty,
                    FatherName = model.FatherName ?? string.Empty,
                    LastName = model.LastName ?? string.Empty,
                    NationalCode = model.NationalCode ?? string.Empty,
                    MobileNumber = model.MobileNumber ?? string.Empty,
                    HouseholdCode = model.HouseholdCode ?? string.Empty,
                    IsActive = model.IsActive,
                    JoinedAt = model.JoinedAt
                };

                return await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating member with national code: {NationalCode}", model.NationalCode);
                throw;
            }
        }

        public async Task UpdateAsync(Member model)
        {
            try
            {
                var command = new UpdateMemberCommand
                {
                    MemberId = model.MemberId,
                    FirstName = model.FirstName ?? string.Empty,
                    FatherName = model.FatherName ?? string.Empty,
                    LastName = model.LastName ?? string.Empty,
                    NationalCode = model.NationalCode ?? string.Empty,
                    MobileNumber = model.MobileNumber ?? string.Empty,
                    HouseholdCode = model.HouseholdCode ?? string.Empty,
                    IsActive = model.IsActive,
                    JoinedAt = model.JoinedAt
                };

                await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating member with ID: {MemberId}", model.MemberId);
                throw;
            }
        }

        public async Task ToggleActiveAsync(long id, bool isActive)
        {
            try
            {
                var member = await GetByIdAsync(id);
                if (member == null)
                {
                    throw new InvalidOperationException($"Member with ID {id} not found.");
                }

                var command = new UpdateMemberCommand
                {
                    MemberId = member.MemberId,
                    FirstName = member.FirstName ?? string.Empty,
                    FatherName = member.FatherName ?? string.Empty,
                    LastName = member.LastName ?? string.Empty,
                    NationalCode = member.NationalCode ?? string.Empty,
                    MobileNumber = member.MobileNumber ?? string.Empty,
                    HouseholdCode = member.HouseholdCode ?? string.Empty,
                    IsActive = isActive,
                    JoinedAt = member.JoinedAt
                };

                await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active status for member with ID: {MemberId}", id);
                throw;
            }
        }

        public async Task DeleteAsync(long id)
        {
            try
            {
                var command = new DeleteMemberCommand { MemberId = id };
                await _mediator.Send(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting member with ID: {MemberId}", id);
                throw;
            }
        }

        public async Task<IReadOnlyList<Household>> GetHouseholdsAsync()
        {
            try
            {
                var query = new GetAllHouseholdsQuery
                {
                    ActiveOnly = true,
                    Limit = 1000
                };

                var households = await _mediator.Send(query);
                return households.ToList().AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving households for member selection");
                throw;
            }
        }

        public async Task<long> GetNextMemberIdAsync()
        {
            try
            {
                var query = new GetNextMemberIdQuery();
                return await _mediator.Send(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving next member id");
                throw;
            }
        }
    }
}
