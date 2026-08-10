-- ==================================================
-- مرحله 3: Audit Trail جامع برای ردیابی تغییرات
-- تاریخ: 2025-01-06
-- هدف: ردیابی کامل تغییرات در جداول حیاتی
-- ==================================================

USE [foodir_EmamAli_minibank];
GO

PRINT N'=== شروع مرحله 3: Audit Trail جامع ===';

-- 1. ایجاد جدول AuditTrail اصلی
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuditTrail]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[AuditTrail] (
        [AuditId] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [TableName] NVARCHAR(50) NOT NULL,
        [RecordId] NVARCHAR(50) NOT NULL,
        [Action] NVARCHAR(20) NOT NULL, -- INSERT/UPDATE/DELETE/PROCESS
        [UserId] NVARCHAR(50) NOT NULL,
        [UserIP] NVARCHAR(45) NULL,
        [Timestamp] DATETIME2 DEFAULT GETUTCDATE(),
        [ProcessId] NVARCHAR(50) NULL,
        [OldValues] NVARCHAR(MAX) NULL, -- JSON format
        [NewValues] NVARCHAR(MAX) NULL, -- JSON format
        [ChangedColumns] NVARCHAR(500) NULL, -- فهرست ستون‌های تغییر یافته
        [Description] NVARCHAR(500) NULL,
        [Severity] TINYINT DEFAULT 1 -- 1=Info, 2=Warning, 3=Error, 4=Critical
    );
    
    -- ایندکس‌های بهینه‌سازی
    CREATE INDEX IX_AuditTrail_TableName_Timestamp ON AuditTrail(TableName, Timestamp DESC);
    CREATE INDEX IX_AuditTrail_RecordId ON AuditTrail(RecordId, TableName);
    CREATE INDEX IX_AuditTrail_UserId ON AuditTrail(UserId, Timestamp DESC);
    CREATE INDEX IX_AuditTrail_ProcessId ON AuditTrail(ProcessId) WHERE ProcessId IS NOT NULL;
    
    PRINT N'✅ جدول AuditTrail ایجاد شد';
END
ELSE
BEGIN
    PRINT N'ℹ️ جدول AuditTrail از قبل موجود است';
END

-- 2. ایجاد Stored Procedure برای ثبت audit
IF OBJECT_ID(N'dbo.sp_InsertAuditRecord', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_InsertAuditRecord;
GO

CREATE PROCEDURE dbo.sp_InsertAuditRecord
    @TableName NVARCHAR(50),
    @RecordId NVARCHAR(50),
    @Action NVARCHAR(20),
    @UserId NVARCHAR(50) = NULL,
    @UserIP NVARCHAR(45) = NULL,
    @ProcessId NVARCHAR(50) = NULL,
    @OldValues NVARCHAR(MAX) = NULL,
    @NewValues NVARCHAR(MAX) = NULL,
    @ChangedColumns NVARCHAR(500) = NULL,
    @Description NVARCHAR(500) = NULL,
    @Severity TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO AuditTrail (
        TableName, RecordId, Action, UserId, UserIP, ProcessId,
        OldValues, NewValues, ChangedColumns, Description, Severity
    )
    VALUES (
        @TableName, @RecordId, @Action, 
        ISNULL(@UserId, SYSTEM_USER), @UserIP, @ProcessId,
        @OldValues, @NewValues, @ChangedColumns, @Description, @Severity
    );
END;
GO

PRINT N'✅ Stored Procedure ثبت audit ایجاد شد';

-- 3. Trigger برای جدول Accounts
IF OBJECT_ID(N'dbo.TR_Accounts_AuditTrail', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_Accounts_AuditTrail;
GO

CREATE TRIGGER dbo.TR_Accounts_AuditTrail ON dbo.Accounts
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    
    -- INSERT
    IF EXISTS(SELECT * FROM inserted) AND NOT EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, NewValues, Description)
        SELECT 'Accounts', 
               CAST(i.AccountId AS NVARCHAR(50)), 
               'INSERT', 
               SYSTEM_USER,
               '{"AccountId":' + CAST(i.AccountId AS NVARCHAR(20)) + 
               ',"Code":"' + ISNULL(i.Code, '') + 
               '","Name":"' + ISNULL(i.Name, '') + 
               '","AccountType":' + CAST(i.AccountType AS NVARCHAR(10)) + 
               ',"Level":' + CAST(ISNULL(i.Level, 0) AS NVARCHAR(10)) + 
               ',"IsActive":' + CASE WHEN i.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               N'حساب جدید: ' + ISNULL(i.Code, '') + ' - ' + ISNULL(i.Name, '')
        FROM inserted i;
    END
    
    -- UPDATE
    IF EXISTS(SELECT * FROM inserted) AND EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, OldValues, NewValues, ChangedColumns, Description)
        SELECT 'Accounts',
               CAST(i.AccountId AS NVARCHAR(50)),
               'UPDATE',
               SYSTEM_USER,
               '{"AccountId":' + CAST(d.AccountId AS NVARCHAR(20)) + 
               ',"Code":"' + ISNULL(d.Code, '') + 
               '","Name":"' + ISNULL(d.Name, '') + 
               '","AccountType":' + CAST(d.AccountType AS NVARCHAR(10)) + 
               ',"Level":' + CAST(ISNULL(d.Level, 0) AS NVARCHAR(10)) + 
               ',"IsActive":' + CASE WHEN d.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               '{"AccountId":' + CAST(i.AccountId AS NVARCHAR(20)) + 
               ',"Code":"' + ISNULL(i.Code, '') + 
               '","Name":"' + ISNULL(i.Name, '') + 
               '","AccountType":' + CAST(i.AccountType AS NVARCHAR(10)) + 
               ',"Level":' + CAST(ISNULL(i.Level, 0) AS NVARCHAR(10)) + 
               ',"IsActive":' + CASE WHEN i.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               CASE 
                   WHEN d.Code != i.Code THEN 'Code,'
                   ELSE ''
               END +
               CASE 
                   WHEN d.Name != i.Name THEN 'Name,'
                   ELSE ''
               END +
               CASE 
                   WHEN d.AccountType != i.AccountType THEN 'AccountType,'
                   ELSE ''
               END +
               CASE 
                   WHEN ISNULL(d.Level, 0) != ISNULL(i.Level, 0) THEN 'Level,'
                   ELSE ''
               END +
               CASE 
                   WHEN d.IsActive != i.IsActive THEN 'IsActive,'
                   ELSE ''
               END,
               N'تغییر حساب: ' + ISNULL(COALESCE(i.Code, d.Code), '') + ' - ' + ISNULL(COALESCE(i.Name, d.Name), '')
        FROM inserted i
        JOIN deleted d ON i.AccountId = d.AccountId;
    END
    
    -- DELETE
    IF NOT EXISTS(SELECT * FROM inserted) AND EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, OldValues, Description, Severity)
        SELECT 'Accounts',
               CAST(d.AccountId AS NVARCHAR(50)),
               'DELETE',
               SYSTEM_USER,
               '{"AccountId":' + CAST(d.AccountId AS NVARCHAR(20)) + 
               ',"Code":"' + ISNULL(d.Code, '') + 
               '","Name":"' + ISNULL(d.Name, '') + 
               '","AccountType":' + CAST(d.AccountType AS NVARCHAR(10)) + 
               ',"Level":' + CAST(ISNULL(d.Level, 0) AS NVARCHAR(10)) + 
               ',"IsActive":' + CASE WHEN d.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               N'حذف حساب: ' + ISNULL(d.Code, '') + ' - ' + ISNULL(d.Name, ''),
               3 -- Warning level
        FROM deleted d;
    END
END;
GO

PRINT N'✅ Trigger audit برای Accounts ایجاد شد';

-- 4. Trigger برای جدول PostingTemplates
IF OBJECT_ID(N'dbo.TR_PostingTemplates_AuditTrail', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_PostingTemplates_AuditTrail;
GO

CREATE TRIGGER dbo.TR_PostingTemplates_AuditTrail ON dbo.PostingTemplates
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    
    -- INSERT
    IF EXISTS(SELECT * FROM inserted) AND NOT EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, NewValues, Description)
        SELECT 'PostingTemplates', 
               CAST(i.PostingTemplateId AS NVARCHAR(50)), 
               'INSERT', 
               SYSTEM_USER,
               '{"PostingTemplateId":' + CAST(i.PostingTemplateId AS NVARCHAR(20)) + 
               ',"TemplateName":"' + ISNULL(i.TemplateName, '') + 
               '","IsActive":' + CASE WHEN i.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               N'الگوی جدید: ' + ISNULL(i.TemplateName, '')
        FROM inserted i;
    END
    
    -- UPDATE
    IF EXISTS(SELECT * FROM inserted) AND EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, OldValues, NewValues, Description)
        SELECT 'PostingTemplates',
               CAST(i.PostingTemplateId AS NVARCHAR(50)),
               'UPDATE',
               SYSTEM_USER,
               '{"PostingTemplateId":' + CAST(d.PostingTemplateId AS NVARCHAR(20)) + 
               ',"TemplateName":"' + ISNULL(d.TemplateName, '') + 
               '","IsActive":' + CASE WHEN d.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               '{"PostingTemplateId":' + CAST(i.PostingTemplateId AS NVARCHAR(20)) + 
               ',"TemplateName":"' + ISNULL(i.TemplateName, '') + 
               '","IsActive":' + CASE WHEN i.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               N'تغییر الگو: ' + ISNULL(COALESCE(i.TemplateName, d.TemplateName), '')
        FROM inserted i
        JOIN deleted d ON i.PostingTemplateId = d.PostingTemplateId;
    END
    
    -- DELETE
    IF NOT EXISTS(SELECT * FROM inserted) AND EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, OldValues, Description, Severity)
        SELECT 'PostingTemplates',
               CAST(d.PostingTemplateId AS NVARCHAR(50)),
               'DELETE',
               SYSTEM_USER,
               '{"PostingTemplateId":' + CAST(d.PostingTemplateId AS NVARCHAR(20)) + 
               ',"TemplateName":"' + ISNULL(d.TemplateName, '') + 
               '","IsActive":' + CASE WHEN d.IsActive = 1 THEN 'true' ELSE 'false' END + '}',
               N'حذف الگو: ' + ISNULL(d.TemplateName, ''),
               4 -- Critical level
        FROM deleted d;
    END
END;
GO

PRINT N'✅ Trigger audit برای PostingTemplates ایجاد شد';

-- 5. Trigger برای جدول HouseholdReceipts
IF OBJECT_ID(N'dbo.TR_HouseholdReceipts_AuditTrail', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_HouseholdReceipts_AuditTrail;
GO

CREATE TRIGGER dbo.TR_HouseholdReceipts_AuditTrail ON dbo.HouseholdReceipts
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    
    -- فقط تغییرات مهم را ثبت می‌کنیم (Status, AmountTotal)
    -- INSERT
    IF EXISTS(SELECT * FROM inserted) AND NOT EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, NewValues, Description, ProcessId)
        SELECT 'HouseholdReceipts', 
               CAST(i.HouseholdReceiptId AS NVARCHAR(50)), 
               'INSERT', 
               ISNULL(i.CreatedBy, SYSTEM_USER),
               '{"HouseholdReceiptId":' + CAST(i.HouseholdReceiptId AS NVARCHAR(20)) + 
               ',"AmountTotal":' + CAST(i.AmountTotal AS NVARCHAR(20)) + 
               ',"Status":' + CAST(i.Status AS NVARCHAR(10)) + '}',
               N'رسید جدید: ' + CAST(i.AmountTotal AS NVARCHAR(20)) + N' ریال',
               i.ProcessId
        FROM inserted i;
    END
    
    -- UPDATE - فقط تغییرات Status
    IF EXISTS(SELECT * FROM inserted) AND EXISTS(SELECT * FROM deleted)
    BEGIN
        INSERT INTO AuditTrail (TableName, RecordId, Action, UserId, OldValues, NewValues, Description, ProcessId)
        SELECT 'HouseholdReceipts',
               CAST(i.HouseholdReceiptId AS NVARCHAR(50)),
               'UPDATE',
               SYSTEM_USER,
               '{"Status":' + CAST(d.Status AS NVARCHAR(10)) + '}',
               '{"Status":' + CAST(i.Status AS NVARCHAR(10)) + '}',
               N'تغییر وضعیت رسید: ' + 
               CASE d.Status 
                   WHEN 0 THEN N'در انتظار'
                   WHEN 1 THEN N'پردازش شده'
                   ELSE N'نامشخص'
               END + N' → ' +
               CASE i.Status 
                   WHEN 0 THEN N'در انتظار'
                   WHEN 1 THEN N'پردازش شده'
                   ELSE N'نامشخص'
               END,
               ISNULL(i.ProcessId, d.ProcessId)
        FROM inserted i
        JOIN deleted d ON i.HouseholdReceiptId = d.HouseholdReceiptId
        WHERE d.Status != i.Status;
    END
END;
GO

PRINT N'✅ Trigger audit برای HouseholdReceipts ایجاد شد';

-- 6. ایجاد View برای گزارش audit
IF OBJECT_ID(N'dbo.vw_AuditReport', N'V') IS NOT NULL 
    DROP VIEW dbo.vw_AuditReport;
GO

CREATE VIEW dbo.vw_AuditReport AS
SELECT 
    a.AuditId,
    a.TableName,
    a.RecordId,
    a.Action,
    a.UserId,
    a.Timestamp,
    a.ProcessId,
    a.Description,
    CASE a.Severity
        WHEN 1 THEN N'اطلاعات'
        WHEN 2 THEN N'هشدار'
        WHEN 3 THEN N'خطا'
        WHEN 4 THEN N'بحرانی'
        ELSE N'نامشخص'
    END AS SeverityText,
    CASE a.TableName
        WHEN 'Accounts' THEN N'حساب‌ها'
        WHEN 'PostingTemplates' THEN N'الگوهای حسابداری'
        WHEN 'HouseholdReceipts' THEN N'رسیدهای خانوار'
        ELSE a.TableName
    END AS TableDisplayName,
    CASE a.Action
        WHEN 'INSERT' THEN N'ایجاد'
        WHEN 'UPDATE' THEN N'ویرایش'
        WHEN 'DELETE' THEN N'حذف'
        WHEN 'PROCESS' THEN N'پردازش'
        ELSE a.Action
    END AS ActionText
FROM AuditTrail a;
GO

PRINT N'✅ View گزارش audit ایجاد شد';

-- 7. ایجاد Stored Procedure برای پاکسازی audit قدیمی
IF OBJECT_ID(N'dbo.sp_CleanupOldAuditRecords', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_CleanupOldAuditRecords;
GO

CREATE PROCEDURE dbo.sp_CleanupOldAuditRecords
    @RetentionDays INT = 365, -- نگهداری یک ساله
    @BatchSize INT = 1000
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, GETUTCDATE());
    DECLARE @DeletedCount INT = 0;
    DECLARE @TotalDeleted INT = 0;
    
    WHILE 1 = 1
    BEGIN
        DELETE TOP (@BatchSize) FROM AuditTrail 
        WHERE Timestamp < @CutoffDate 
          AND Severity <= 2; -- فقط Info و Warning را حذف می‌کنیم
        
        SET @DeletedCount = @@ROWCOUNT;
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
        
        IF @DeletedCount = 0 BREAK;
        
        WAITFOR DELAY '00:00:01'; -- وقفه کوتاه برای جلوگیری از lock
    END
    
    PRINT N'تعداد ' + CAST(@TotalDeleted AS NVARCHAR(10)) + N' رکورد audit حذف شد';
    
    RETURN @TotalDeleted;
END;
GO

PRINT N'✅ Stored Procedure پاکسازی audit قدیمی ایجاد شد';

PRINT N'=== مرحله 3 با موفقیت تکمیل شد ===';
PRINT N'';
PRINT N'قابلیت‌های جدید:';
PRINT N'• ردیابی کامل تغییرات در Accounts، PostingTemplates، HouseholdReceipts';
PRINT N'• ثبت JSON از مقادیر قبل و بعد تغییر';
PRINT N'• سطح‌بندی اهمیت (Info, Warning, Error, Critical)';
PRINT N'• ارتباط با ProcessId برای ردیابی فرآیندها';
PRINT N'• گزارش‌گیری فارسی از تغییرات';
PRINT N'• پاکسازی خودکار رکوردهای قدیمی';
PRINT N'';
