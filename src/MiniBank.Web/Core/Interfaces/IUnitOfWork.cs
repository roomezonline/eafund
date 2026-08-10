namespace MiniBank.Web.Core.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IHouseholdRepository Households { get; }
        Task<int> SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
    }
}
