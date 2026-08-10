using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Services
{
    public class PostingService
    {
        private readonly JournalsRepository _journalsRepo;
        private readonly JournalLinesRepository _journalLinesRepo;
        private readonly AccountsRepository _accountsRepo;
        private readonly ILogger<PostingService> _logger;

        public PostingService(
            JournalsRepository journalsRepo,
            JournalLinesRepository journalLinesRepo,
            AccountsRepository accountsRepo,
            ILogger<PostingService> logger)
        {
            _journalsRepo = journalsRepo;
            _journalLinesRepo = journalLinesRepo;
            _accountsRepo = accountsRepo;
            _logger = logger;
        }

        public async Task<long> CreateJournalAsync(PostJournalRequest request)
        {
            try
            {
                var journalId = await _journalsRepo.CreateJournalAsync(
                    request.TransactionId,
                    request.OccurredAt,
                    request.CreatedBy,
                    request.Notes);

                _logger.LogInformation("Created journal {JournalId} for transaction {TransactionId}", journalId, request.TransactionId);
                return journalId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating journal for transaction {TransactionId}", request.TransactionId);
                throw;
            }
        }

        public async Task<long> AddJournalLineAsync(AddJournalLineRequest request)
        {
            try
            {
                // Validate account exists
                var account = await _accountsRepo.GetByIdAsync(request.AccountId);
                if (account == null)
                {
                    throw new InvalidOperationException($"Account {request.AccountId} not found");
                }

                // Validate debit/credit rules
                if (request.Debit < 0 || request.Credit < 0)
                {
                    throw new ArgumentException("Debit and Credit amounts must be non-negative");
                }

                if (request.Debit > 0 && request.Credit > 0)
                {
                    throw new ArgumentException("A journal line cannot have both debit and credit amounts");
                }

                if (request.Debit == 0 && request.Credit == 0)
                {
                    throw new ArgumentException("A journal line must have either debit or credit amount");
                }

                var lineNo = request.LineNo ?? await _journalLinesRepo.GetNextLineNoAsync(request.JournalId);
                
                var lineId = await _journalLinesRepo.AddLineAsync(
                    request.JournalId,
                    lineNo,
                    request.AccountId,
                    request.MemberId,
                    request.Debit,
                    request.Credit,
                    request.Description);

                _logger.LogInformation("Added journal line {LineId} to journal {JournalId}: {AccountId} DR:{Debit} CR:{Credit}", 
                    lineId, request.JournalId, request.AccountId, request.Debit, request.Credit);

                return lineId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding journal line to journal {JournalId}", request.JournalId);
                throw;
            }
        }

        public async Task ValidateAndFinalizeJournalAsync(long journalId)
        {
            try
            {
                var isBalanced = await _journalLinesRepo.ValidateBalanceAsync(journalId);
                if (!isBalanced)
                {
                    var (debit, credit) = await _journalLinesRepo.GetJournalBalanceAsync(journalId);
                    throw new InvalidOperationException($"Journal {journalId} is not balanced. Total Debit: {debit:N2}, Total Credit: {credit:N2}");
                }

                _logger.LogInformation("Journal {JournalId} validated and balanced successfully", journalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating journal {JournalId}", journalId);
                throw;
            }
        }

        public async Task<PostingResult> PostSimpleEntryAsync(SimplePostingRequest request)
        {
            try
            {
                // Create journal
                var journalId = await CreateJournalAsync(new PostJournalRequest
                {
                    TransactionId = request.TransactionId,
                    OccurredAt = request.OccurredAt,
                    CreatedBy = request.CreatedBy,
                    Notes = request.Notes
                });

                // Add debit line
                await AddJournalLineAsync(new AddJournalLineRequest
                {
                    JournalId = journalId,
                    LineNo = 1,
                    AccountId = request.DebitAccountId,
                    MemberId = request.DebitMemberId,
                    Debit = request.Amount,
                    Credit = 0,
                    Description = request.DebitDescription ?? request.Description
                });

                // Add credit line
                await AddJournalLineAsync(new AddJournalLineRequest
                {
                    JournalId = journalId,
                    LineNo = 2,
                    AccountId = request.CreditAccountId,
                    MemberId = request.CreditMemberId,
                    Debit = 0,
                    Credit = request.Amount,
                    Description = request.CreditDescription ?? request.Description
                });

                // Validate balance
                await ValidateAndFinalizeJournalAsync(journalId);

                return new PostingResult
                {
                    JournalId = journalId,
                    Success = true,
                    Message = "Entry posted successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error posting simple entry for transaction {TransactionId}", request.TransactionId);
                return new PostingResult
                {
                    Success = false,
                    Message = ex.Message,
                    Exception = ex
                };
            }
        }

        public async Task<Journal?> GetJournalByTransactionIdAsync(long transactionId)
        {
            return await _journalsRepo.GetByTransactionIdAsync(transactionId);
        }

        public async Task<List<JournalLine>> GetJournalLinesAsync(long journalId)
        {
            return await _journalLinesRepo.GetByJournalIdAsync(journalId);
        }
    }

    // Request/Response DTOs
    public class PostJournalRequest
    {
        public long TransactionId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class AddJournalLineRequest
    {
        public long JournalId { get; set; }
        public int? LineNo { get; set; } // Auto-generate if null
        public long AccountId { get; set; }
        public long? MemberId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string? Description { get; set; }
    }

    public class SimplePostingRequest
    {
        public long TransactionId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? Description { get; set; }
        
        public long DebitAccountId { get; set; }
        public long? DebitMemberId { get; set; }
        public string? DebitDescription { get; set; }
        
        public long CreditAccountId { get; set; }
        public long? CreditMemberId { get; set; }
        public string? CreditDescription { get; set; }
        
        public decimal Amount { get; set; }
    }

    public class PostingResult
    {
        public long? JournalId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }
}
