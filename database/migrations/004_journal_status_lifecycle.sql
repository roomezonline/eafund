-- ============================================================
-- Migration 004: Journal Status Lifecycle (Temporary/Permanent/Final)
-- 
-- Adds: Status (TINYINT), IsDeleted (BIT), SerialNumber (INT) to Journals
-- Creates: vw_ApprovedJournals (filters Status>=2 AND IsDeleted=0)
-- Updates: TR_Journals_EnforceBalance, existing views, sp_BalanceSheet_AsOf
-- 
-- Status: 1=موقت, 2=دائمی, 3=قطعی
-- ============================================================

BEGIN TRANSACTION;
GO

-- 1. Add Status column (TINYINT, default 2=Permanent)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Journals') AND name = N'Status')
BEGIN
    ALTER TABLE dbo.Journals ADD Status TINYINT NOT NULL CONSTRAINT DF_Journals_Status DEFAULT 2;
    PRINT 'Column Status added to Journals (default: 2=Permanent)';
END
ELSE
    PRINT 'Column Status already exists on Journals';
GO

-- 2. Add IsDeleted column (BIT, default 0)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Journals') AND name = N'IsDeleted')
BEGIN
    ALTER TABLE dbo.Journals ADD IsDeleted BIT NOT NULL CONSTRAINT DF_Journals_IsDeleted DEFAULT 0;
    PRINT 'Column IsDeleted added to Journals (default: 0)';
END
ELSE
    PRINT 'Column IsDeleted already exists on Journals';
GO

-- 3. Add SerialNumber column (INT, nullable)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Journals') AND name = N'SerialNumber')
BEGIN
    ALTER TABLE dbo.Journals ADD SerialNumber INT NULL;
    PRINT 'Column SerialNumber added to Journals';
END
ELSE
    PRINT 'Column SerialNumber already exists on Journals';
GO

-- 4. Safety: Ensure all existing journals have Status=2 (Permanent)
UPDATE dbo.Journals SET Status = 2 WHERE Status IS NULL OR Status = 0;
PRINT 'All existing journals set to Permanent (Status=2)';

-- Verify: Count journals by status
DECLARE @total INT, @perm INT, @temp INT, @final INT;
SELECT @total = COUNT(*) FROM dbo.Journals;
SELECT @perm = COUNT(*) FROM dbo.Journals WHERE Status = 2;
SELECT @temp = COUNT(*) FROM dbo.Journals WHERE Status = 1;
SELECT @final = COUNT(*) FROM dbo.Journals WHERE Status = 3;
PRINT 'Status distribution: Total=' + CAST(@total AS NVARCHAR) + ', Permanent=' + CAST(@perm AS NVARCHAR) + ', Temporary=' + CAST(@temp AS NVARCHAR) + ', Final=' + CAST(@final AS NVARCHAR);
GO

-- 5. Create vw_ApprovedJournals view
IF OBJECT_ID(N'dbo.vw_ApprovedJournals', N'V') IS NOT NULL DROP VIEW dbo.vw_ApprovedJournals;
GO
CREATE VIEW dbo.vw_ApprovedJournals AS
SELECT JournalId, TransactionId, OccurredAt, CreatedAt, CreatedBy, Notes, Status, IsDeleted, SerialNumber
FROM dbo.Journals
WHERE Status >= 2 AND IsDeleted = 0;
GO
PRINT 'View vw_ApprovedJournals created';

-- Verify: Row count matches
DECLARE @journalsRows INT, @viewRows INT;
SELECT @journalsRows = COUNT(*) FROM dbo.Journals WHERE Status >= 2 AND IsDeleted = 0;
SELECT @viewRows = COUNT(*) FROM dbo.vw_ApprovedJournals;
IF @journalsRows <> @viewRows
    PRINT 'WARNING: vw_ApprovedJournals row count mismatch! Journals=' + CAST(@journalsRows AS NVARCHAR) + ', View=' + CAST(@viewRows AS NVARCHAR);
ELSE
    PRINT 'OK: vw_ApprovedJournals has ' + CAST(@viewRows AS NVARCHAR) + ' rows (matches Journals filter)';
GO

-- 6. Recreate TR_Journals_EnforceBalance trigger
IF OBJECT_ID(N'[dbo].[TR_Journals_EnforceBalance]', N'TR') IS NOT NULL
    DROP TRIGGER [dbo].[TR_Journals_EnforceBalance];
GO
EXEC(N'
CREATE TRIGGER [dbo].[TR_Journals_EnforceBalance] ON [dbo].[JournalLines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    -- Prevent modifications to Final (Status=3) journals
    IF EXISTS (
        SELECT 1
        FROM dbo.Journals j
        WHERE j.Status = 3
        AND j.JournalId IN (
            SELECT DISTINCT JournalId FROM (
                SELECT JournalId FROM inserted
                UNION ALL
                SELECT JournalId FROM deleted
            ) x WHERE JournalId IS NOT NULL
        )
    )
    BEGIN
        RAISERROR(''اسناد قطعی قابل ویرایش یا حذف نیستند.'', 16, 1);
        ROLLBACK;
        RETURN;
    END
    -- Enforce balance for Permanent (Status>=2) journals only
    ;WITH impacted AS (
        SELECT DISTINCT JournalId FROM (
            SELECT JournalId FROM inserted
            UNION ALL
            SELECT JournalId FROM deleted
        ) x WHERE JournalId IS NOT NULL
    )
    SELECT 1/0
    FROM impacted i
    INNER JOIN dbo.Journals j ON j.JournalId = i.JournalId
    CROSS APPLY (
        SELECT SUM(Debit) AS SumDebit, SUM(Credit) AS SumCredit
        FROM dbo.JournalLines jl WHERE jl.JournalId = i.JournalId
    ) s
    WHERE j.Status >= 2
    AND ISNULL(s.SumDebit, 0) <> ISNULL(s.SumCredit, 0);
END');
GO
PRINT 'Trigger TR_Journals_EnforceBalance recreated';

-- 7. Update vw_JournalLinesFlat
IF OBJECT_ID(N'dbo.vw_JournalLinesFlat', N'V') IS NOT NULL DROP VIEW dbo.vw_JournalLinesFlat;
GO
CREATE VIEW dbo.vw_JournalLinesFlat AS
SELECT jl.JournalLineId, jl.JournalId, j.OccurredAt, jl.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType, jl.MemberId, jl.Debit, jl.Credit
FROM dbo.JournalLines jl
JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
JOIN dbo.Accounts a ON a.AccountId = jl.AccountId;
GO
PRINT 'View vw_JournalLinesFlat updated (uses vw_ApprovedJournals)';

-- 8. Update vw_TrialBalance
IF OBJECT_ID(N'dbo.vw_TrialBalance', N'V') IS NOT NULL DROP VIEW dbo.vw_TrialBalance;
GO
CREATE VIEW dbo.vw_TrialBalance AS
SELECT a.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType,
       SUM(jl.Debit) AS TotalDebit, SUM(jl.Credit) AS TotalCredit, SUM(jl.Debit - jl.Credit) AS Net
FROM dbo.JournalLines jl
JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
GROUP BY a.AccountId, a.Code, a.Name, a.AccountType;
GO
PRINT 'View vw_TrialBalance updated (uses vw_ApprovedJournals)';

-- 9. Update vw_MemberDepositBalances
IF OBJECT_ID(N'dbo.vw_MemberDepositBalances', N'V') IS NOT NULL DROP VIEW dbo.vw_MemberDepositBalances;
GO
CREATE VIEW dbo.vw_MemberDepositBalances AS
SELECT jl.MemberId, SUM(CASE WHEN a.Code = N'2000' THEN (jl.Credit - jl.Debit) ELSE 0 END) AS DepositLiability
FROM dbo.JournalLines jl
JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
WHERE a.Code = N'2000' AND jl.MemberId IS NOT NULL
GROUP BY jl.MemberId;
GO
PRINT 'View vw_MemberDepositBalances updated (uses vw_ApprovedJournals)';

-- 10. Update sp_BalanceSheet_AsOf
IF OBJECT_ID(N'dbo.sp_BalanceSheet_AsOf', N'P') IS NOT NULL
BEGIN
    EXEC('
    ALTER PROCEDURE dbo.sp_BalanceSheet_AsOf @AsOf DATETIME2
    AS
    BEGIN
        SET NOCOUNT ON;
        ;WITH sums AS (
            SELECT a.AccountType, SUM(jl.Debit) AS Debit, SUM(jl.Credit) AS Credit, SUM(jl.Debit - jl.Credit) AS Net
            FROM dbo.JournalLines jl
            JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
            JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
            WHERE j.OccurredAt <= @AsOf
            GROUP BY a.AccountType
        )
        SELECT ISNULL((SELECT Net FROM sums WHERE AccountType=1),0) AS Assets,
               ISNULL((SELECT -Net FROM sums WHERE AccountType=2),0) AS Liabilities,
               ISNULL((SELECT -Net FROM sums WHERE AccountType=3),0) AS Equity,
               ISNULL((SELECT Net FROM sums WHERE AccountType=4),0) AS Income,
               ISNULL((SELECT -Net FROM sums WHERE AccountType=5),0) AS Expense;
    END');
    PRINT 'Stored procedure sp_BalanceSheet_AsOf updated (uses vw_ApprovedJournals)';
END
GO

-- 11. Final verification
DECLARE @vwCount INT, @spCount INT;
SELECT @vwCount = COUNT(*) FROM dbo.vw_ApprovedJournals;
PRINT 'Final: vw_ApprovedJournals has ' + CAST(@vwCount AS NVARCHAR) + ' rows';
PRINT 'Migration 004 completed successfully';
GO

COMMIT TRANSACTION;
GO
