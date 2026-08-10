using Microsoft.Data.SqlClient;
using MiniBank.Web.Core.Interfaces;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Infrastructure
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly string _connectionString;
        private readonly IServiceProvider _serviceProvider;
        private SqlConnection? _connection;
        private SqlTransaction? _transaction;
        private bool _disposed = false;

        private IHouseholdRepository? _households;

        public UnitOfWork(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string not found");
            _serviceProvider = serviceProvider;
        }

        public IHouseholdRepository Households
        {
            get
            {
                _households ??= _serviceProvider.GetRequiredService<IHouseholdRepository>();
                return _households;
            }
        }

        public async Task<int> SaveChangesAsync()
        {
            // In this simple implementation, changes are saved immediately
            // This method is here for interface compliance and future enhancement
            return await Task.FromResult(0);
        }

        public async Task BeginTransactionAsync()
        {
            if (_connection == null)
            {
                _connection = new SqlConnection(_connectionString);
                await _connection.OpenAsync();
            }

            _transaction = _connection.BeginTransaction();
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.CommitAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _transaction?.Dispose();
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }
}
