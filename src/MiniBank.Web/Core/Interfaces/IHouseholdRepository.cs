using MiniBank.Web.Models;

namespace MiniBank.Web.Core.Interfaces
{
    public interface IHouseholdRepository : IRepository<Household>
    {
        Task<IEnumerable<Household>> GetActiveHouseholdsAsync();
        Task<IEnumerable<Household>> SearchHouseholdsAsync(string searchTerm);
        Task<Household?> GetByCodeAsync(string householdCode);
        Task<bool> IsCodeUniqueAsync(string householdCode, long? excludeId = null);
        Task<IEnumerable<Household>> GetRecentHouseholdsAsync(int count = 10);
    }
}
