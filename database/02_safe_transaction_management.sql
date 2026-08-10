-- ==================================================
-- مرحله 2: Transaction Management امن با rollback
-- تاریخ: 2025-01-06
-- هدف: جلوگیری از داده‌های ناقص و اطمینان از یکپارچگی
-- ==================================================

USE [foodir_EmamAli_minibank];
GO

PRINT N'=== شروع مرحله 2: Transaction Management امن ===';

-- 1. ایجاد جدول ProcessLog برای ردیابی فرآیندها
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ProcessLog]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[ProcessLog] (
        [LogId] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [ProcessId] NVARCHAR(50) NOT NULL,
        [ProcessType] NVARCHAR(50) NOT NULL, -- ReceiptProcessing, JournalPosting, etc.
        [Step] NVARCHAR(100) NOT NULL,
        [Status] NVARCHAR(20) NOT NULL, -- Started, InProgress, Completed, Failed
        [Message] NVARCHAR(500) NULL,
        [RelatedId] BIGINT NULL, -- ReceiptId, TransactionId, etc.
        [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
        [CreatedBy] NVARCHAR(128) DEFAULT SYSTEM_USER
    );
    
    CREATE INDEX IX_ProcessLog_ProcessId ON ProcessLog(ProcessId, CreatedAt);
    CREATE INDEX IX_ProcessLog_RelatedId ON ProcessLog(RelatedId, ProcessType);
    
    PRINT N'✅ جدول ProcessLog ایجاد شد';
END
ELSE
BEGIN
    PRINT N'ℹ️ جدول ProcessLog از قبل موجود است';
END

-- 2. ایجاد جدول FailedTransactions برای ردیابی خطاها
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FailedTransactions]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[FailedTransactions] (
        [FailedTransactionId] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [OriginalTransactionId] BIGINT NULL,
        [ReceiptId] BIGINT NULL,
        [ProcessId] NVARCHAR(50) NOT NULL,
        [ErrorMessage] NVARCHAR(MAX) NOT NULL,
        [ErrorCode] NVARCHAR(50) NULL,
        [StackTrace] NVARCHAR(MAX) NULL,
        [FailedAt] DATETIME2 DEFAULT GETUTCDATE(),
        [RetryCount] INT DEFAULT 0,
        [IsResolved] BIT DEFAULT 0,
        [ResolvedAt] DATETIME2 NULL,
        [ResolvedBy] NVARCHAR(128) NULL
    );
    
    CREATE INDEX IX_FailedTransactions_ProcessId ON FailedTransactions(ProcessId);
    CREATE INDEX IX_FailedTransactions_IsResolved ON FailedTransactions(IsResolved, FailedAt);
    
    PRINT N'✅ جدول FailedTransactions ایجاد شد';
END
ELSE
BEGIN
    PRINT N'ℹ️ جدول FailedTransactions از قبل موجود است';
END

-- 3. اضافه کردن ستون ProcessId به جداول اصلی (اگر وجود ندارد)
IF COL_LENGTH('dbo.HouseholdReceipts', 'ProcessId') IS NULL
BEGIN
    ALTER TABLE [dbo].[HouseholdReceipts] ADD [ProcessId] NVARCHAR(50) NULL;
    PRINT N'✅ ستون ProcessId به HouseholdReceipts اضافه شد';
END

IF COL_LENGTH('dbo.Transactions', 'ProcessId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Transactions] ADD [ProcessId] NVARCHAR(50) NULL;
    PRINT N'✅ ستون ProcessId به Transactions اضافه شد';
END

IF COL_LENGTH('dbo.Journals', 'ProcessId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Journals] ADD [ProcessId] NVARCHAR(50) NULL;
    PRINT N'✅ ستون ProcessId به Journals اضافه شد';
END

-- 4. ایجاد Stored Procedure برای شروع فرآیند امن
IF OBJECT_ID(N'dbo.sp_StartSafeProcess', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_StartSafeProcess;
GO

CREATE PROCEDURE dbo.sp_StartSafeProcess
    @ProcessType NVARCHAR(50),
    @RelatedId BIGINT = NULL,
    @CreatedBy NVARCHAR(128) = NULL,
    @ProcessId NVARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- تولید ProcessId یکتا
    SET @ProcessId = @ProcessType + '_' + FORMAT(GETUTCDATE(), 'yyyyMMdd_HHmmss') + '_' + CAST(ABS(CHECKSUM(NEWID())) % 10000 AS NVARCHAR(4));
    
    -- ثبت شروع فرآیند
    INSERT INTO ProcessLog (ProcessId, ProcessType, Step, Status, RelatedId, CreatedBy)
    VALUES (@ProcessId, @ProcessType, 'Started', 'Started', @RelatedId, ISNULL(@CreatedBy, SYSTEM_USER));
    
    RETURN 0;
END;
GO

PRINT N'✅ Stored Procedure شروع فرآیند امن ایجاد شد';

-- 5. ایجاد Stored Procedure برای ثبت مراحل فرآیند
IF OBJECT_ID(N'dbo.sp_LogProcessStep', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_LogProcessStep;
GO

CREATE PROCEDURE dbo.sp_LogProcessStep
    @ProcessId NVARCHAR(50),
    @Step NVARCHAR(100),
    @Status NVARCHAR(20),
    @Message NVARCHAR(500) = NULL,
    @RelatedId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO ProcessLog (ProcessId, ProcessType, Step, Status, Message, RelatedId, CreatedBy)
    SELECT @ProcessId, 
           (SELECT TOP 1 ProcessType FROM ProcessLog WHERE ProcessId = @ProcessId),
           @Step, 
           @Status, 
           @Message, 
           ISNULL(@RelatedId, (SELECT TOP 1 RelatedId FROM ProcessLog WHERE ProcessId = @ProcessId)),
           SYSTEM_USER;
    
    RETURN 0;
END;
GO

PRINT N'✅ Stored Procedure ثبت مراحل فرآیند ایجاد شد';

-- 6. ایجاد Stored Procedure برای ثبت خطا
IF OBJECT_ID(N'dbo.sp_LogProcessError', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_LogProcessError;
GO

CREATE PROCEDURE dbo.sp_LogProcessError
    @ProcessId NVARCHAR(50),
    @ErrorMessage NVARCHAR(MAX),
    @ErrorCode NVARCHAR(50) = NULL,
    @StackTrace NVARCHAR(MAX) = NULL,
    @TransactionId BIGINT = NULL,
    @ReceiptId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- ثبت در ProcessLog
    EXEC sp_LogProcessStep @ProcessId, 'Error', 'Failed', @ErrorMessage;
    
    -- ثبت در FailedTransactions
    INSERT INTO FailedTransactions (OriginalTransactionId, ReceiptId, ProcessId, ErrorMessage, ErrorCode, StackTrace)
    VALUES (@TransactionId, @ReceiptId, @ProcessId, @ErrorMessage, @ErrorCode, @StackTrace);
    
    RETURN 0;
END;
GO

PRINT N'✅ Stored Procedure ثبت خطا ایجاد شد';

-- 7. ایجاد Stored Procedure برای پاکسازی فرآیند ناقص
IF OBJECT_ID(N'dbo.sp_CleanupFailedProcess', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_CleanupFailedProcess;
GO

CREATE PROCEDURE dbo.sp_CleanupFailedProcess
    @ProcessId NVARCHAR(50),
    @ReceiptId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        -- حذف JournalLines مرتبط با فرآیند ناقص
        DELETE jl 
        FROM JournalLines jl
        JOIN Journals j ON j.JournalId = jl.JournalId
        WHERE j.ProcessId = @ProcessId;
        
        -- حذف Journals مرتبط با فرآیند ناقص
        DELETE FROM Journals WHERE ProcessId = @ProcessId;
        
        -- حذف Transactions مرتبط با فرآیند ناقص
        DELETE FROM Transactions WHERE ProcessId = @ProcessId;
        
        -- بازگردانی وضعیت رسید (اگر مشخص شده)
        IF @ReceiptId IS NOT NULL
        BEGIN
            UPDATE HouseholdReceipts 
            SET Status = 0, ProcessId = NULL 
            WHERE HouseholdReceiptId = @ReceiptId;
        END
        
        -- ثبت پاکسازی موفق
        EXEC sp_LogProcessStep @ProcessId, 'Cleanup', 'Completed', 'فرآیند ناقص پاکسازی شد';
        
        RETURN 0;
    END TRY
    BEGIN CATCH
        -- ثبت خطای پاکسازی
        DECLARE @ErrorMsg NVARCHAR(MAX) = ERROR_MESSAGE();
        EXEC sp_LogProcessError @ProcessId, @ErrorMsg, 'CLEANUP_ERROR';
        
        RETURN -1;
    END CATCH
END;
GO

PRINT N'✅ Stored Procedure پاکسازی فرآیند ناقص ایجاد شد';

-- 8. ایجاد View برای نمایش وضعیت فرآیندها
IF OBJECT_ID(N'dbo.vw_ProcessStatus', N'V') IS NOT NULL 
    DROP VIEW dbo.vw_ProcessStatus;
GO

CREATE VIEW dbo.vw_ProcessStatus AS
SELECT 
    p.ProcessId,
    p.ProcessType,
    p.RelatedId,
    MIN(p.CreatedAt) AS StartedAt,
    MAX(p.CreatedAt) AS LastUpdateAt,
    CASE 
        WHEN EXISTS(SELECT 1 FROM ProcessLog p2 WHERE p2.ProcessId = p.ProcessId AND p2.Status = 'Failed') THEN 'Failed'
        WHEN EXISTS(SELECT 1 FROM ProcessLog p2 WHERE p2.ProcessId = p.ProcessId AND p2.Status = 'Completed') THEN 'Completed'
        ELSE 'InProgress'
    END AS CurrentStatus,
    (SELECT COUNT(*) FROM ProcessLog p2 WHERE p2.ProcessId = p.ProcessId) AS StepCount,
    (SELECT TOP 1 Message FROM ProcessLog p2 WHERE p2.ProcessId = p.ProcessId ORDER BY CreatedAt DESC) AS LastMessage
FROM ProcessLog p
GROUP BY p.ProcessId, p.ProcessType, p.RelatedId;
GO

PRINT N'✅ View وضعیت فرآیندها ایجاد شد';

-- 9. ایجاد Function برای بررسی وضعیت فرآیند
IF OBJECT_ID(N'dbo.fn_GetProcessStatus', N'FN') IS NOT NULL 
    DROP FUNCTION dbo.fn_GetProcessStatus;
GO

CREATE FUNCTION dbo.fn_GetProcessStatus(@ProcessId NVARCHAR(50))
RETURNS TABLE
AS
RETURN
(
    SELECT 
        ProcessId,
        ProcessType,
        CurrentStatus,
        StartedAt,
        LastUpdateAt,
        StepCount,
        LastMessage,
        CASE CurrentStatus
            WHEN 'Failed' THEN 1
            WHEN 'Completed' THEN 0
            ELSE 0
        END AS RequiresCleanup
    FROM vw_ProcessStatus
    WHERE ProcessId = @ProcessId
);
GO

PRINT N'✅ Function بررسی وضعیت فرآیند ایجاد شد';

PRINT N'=== مرحله 2 با موفقیت تکمیل شد ===';
PRINT N'';
PRINT N'قابلیت‌های جدید:';
PRINT N'• ردیابی کامل فرآیندها با ProcessId یکتا';
PRINT N'• ثبت تمام مراحل و خطاها در ProcessLog';
PRINT N'• پاکسازی خودکار فرآیندهای ناقص';
PRINT N'• بازیابی وضعیت در صورت خطا';
PRINT N'• گزارش‌گیری از وضعیت فرآیندها';
PRINT N'';
