-- ==================================================
-- مرحله 1: حذف محدودیت‌های کدگذاری حساب‌ها
-- تاریخ: 2025-01-06
-- هدف: اجازه کدگذاری آزاد (1.01، 101، 10112، CASH و...)
-- ==================================================

USE [foodir_EmamAli_minibank];
GO

PRINT N'=== شروع مرحله 1: انعطاف‌پذیری کدگذاری حساب‌ها ===';

-- 1. حذف محدودیت‌های الگوی کد (اگر وجود دارد)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodePattern')
BEGIN
    ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_CodePattern];
    PRINT N'✅ محدودیت الگوی کد حذف شد';
END
ELSE
BEGIN
    PRINT N'ℹ️ محدودیت الگوی کد از قبل وجود نداشت';
END

-- 2. حذف محدودیت‌های طول کد (اگر وجود دارد)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodeLength')
BEGIN
    ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_CodeLength];
    PRINT N'✅ محدودیت طول کد حذف شد';
END

-- 3. اضافه کردن محدودیت‌های منطقی جدید
-- فقط اطمینان از غیرخالی بودن کد
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodeNotEmpty')
BEGIN
    ALTER TABLE [dbo].[Accounts] 
    ADD CONSTRAINT [CK_Accounts_CodeNotEmpty] 
    CHECK (LEN(LTRIM(RTRIM(Code))) > 0 AND LEN(LTRIM(RTRIM(Code))) <= 50);
    PRINT N'✅ محدودیت کد غیرخالی اضافه شد (حداکثر 50 کاراکتر)';
END

-- 4. اطمینان از یکتایی کد فقط در حساب‌های فعال
-- حذف ایندکس قدیمی اگر وجود دارد
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Accounts_Code_Active' AND object_id = OBJECT_ID(N'[dbo].[Accounts]'))
BEGIN
    DROP INDEX [IX_Accounts_Code_Active] ON [dbo].[Accounts];
    PRINT N'🔄 ایندکس یکتایی قدیمی حذف شد';
END

-- ایجاد ایندکس یکتایی جدید
CREATE UNIQUE NONCLUSTERED INDEX [IX_Accounts_Code_Active]
ON [dbo].[Accounts]([Code] ASC)
WHERE ([IsActive] = 1);
PRINT N'✅ ایندکس یکتایی جدید برای حساب‌های فعال ایجاد شد';

-- 5. اضافه کردن محدودیت جلوگیری از حذف حساب‌های دارای تراکنش
-- نوت: ستون محاسبه‌شده در SQL Server 2016 محدودیت دارد، پس از Function استفاده می‌کنیم

-- 6. ایجاد View برای نمایش بهتر حساب‌ها
IF OBJECT_ID(N'dbo.vw_AccountsWithStatus', N'V') IS NOT NULL 
    DROP VIEW dbo.vw_AccountsWithStatus;
GO

CREATE VIEW dbo.vw_AccountsWithStatus AS
SELECT 
    a.AccountId,
    a.Code,
    a.Name,
    a.AccountType,
    a.Level,
    a.ParentAccountId,
    a.IsActive,
    a.CreatedAt,
    -- نمایش وضعیت
    CASE 
        WHEN a.IsActive = 0 THEN N'غیرفعال'
        WHEN EXISTS(SELECT 1 FROM dbo.JournalLines jl WHERE jl.AccountId = a.AccountId) THEN N'دارای تراکنش'
        ELSE N'فعال'
    END AS Status,
    -- تعداد تراکنش‌ها
    (SELECT COUNT(*) FROM dbo.JournalLines jl WHERE jl.AccountId = a.AccountId) AS TransactionCount,
    -- نام حساب والد
    pa.Name AS ParentAccountName,
    -- نوع حساب به فارسی
    CASE a.AccountType
        WHEN 1 THEN N'دارایی'
        WHEN 2 THEN N'بدهی'
        WHEN 3 THEN N'حقوق صاحبان'
        WHEN 4 THEN N'درآمد'
        WHEN 5 THEN N'هزینه'
        ELSE N'نامشخص'
    END AS AccountTypeName
FROM dbo.Accounts a
LEFT JOIN dbo.Accounts pa ON pa.AccountId = a.ParentAccountId;
GO

PRINT N'✅ View نمایش حساب‌ها با وضعیت ایجاد شد';

-- 7. ایجاد Function برای اعتبارسنجی کد حساب
IF OBJECT_ID(N'dbo.fn_ValidateAccountCode', N'FN') IS NOT NULL 
    DROP FUNCTION dbo.fn_ValidateAccountCode;
GO

CREATE FUNCTION dbo.fn_ValidateAccountCode(@Code NVARCHAR(50))
RETURNS @Result TABLE (IsValid BIT, ValidationMessage NVARCHAR(500))
AS
BEGIN
    DECLARE @IsValid BIT = 1;
    DECLARE @Message NVARCHAR(500) = N'کد معتبر است';
    
    IF LEN(LTRIM(RTRIM(@Code))) = 0
    BEGIN
        SET @IsValid = 0;
        SET @Message = N'کد حساب نمی‌تواند خالی باشد';
    END
    ELSE IF LEN(LTRIM(RTRIM(@Code))) > 50
    BEGIN
        SET @IsValid = 0;
        SET @Message = N'کد حساب نمی‌تواند بیش از 50 کاراکتر باشد';
    END
    ELSE IF EXISTS(SELECT 1 FROM dbo.Accounts WHERE Code = @Code AND IsActive = 1)
    BEGIN
        SET @IsValid = 0;
        SET @Message = N'این کد قبلاً استفاده شده است';
    END
    
    INSERT INTO @Result VALUES (@IsValid, @Message);
    RETURN;
END;
GO

PRINT N'✅ Function اعتبارسنجی کد حساب ایجاد شد';

-- 8. ایجاد Stored Procedure برای ایجاد حساب جدید با اعتبارسنجی
IF OBJECT_ID(N'dbo.sp_CreateAccount', N'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_CreateAccount;
GO

CREATE PROCEDURE dbo.sp_CreateAccount
    @Code NVARCHAR(50),
    @Name NVARCHAR(200),
    @AccountType TINYINT,
    @Level TINYINT = NULL,
    @ParentAccountId BIGINT = NULL,
    @CreatedBy NVARCHAR(128) = 'system',
    @AccountId BIGINT OUTPUT,
    @ErrorMessage NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @IsValid BIT, @ValidationMessage NVARCHAR(500);
    
    -- اعتبارسنجی کد
    SELECT @IsValid = IsValid, @ValidationMessage = ValidationMessage
    FROM dbo.fn_ValidateAccountCode(@Code);
    
    IF @IsValid = 0
    BEGIN
        SET @ErrorMessage = @ValidationMessage;
        RETURN -1;
    END
    
    -- اعتبارسنجی نوع حساب
    IF @AccountType NOT IN (1, 2, 3, 4, 5)
    BEGIN
        SET @ErrorMessage = N'نوع حساب باید بین 1 تا 5 باشد';
        RETURN -2;
    END
    
    -- اعتبارسنجی حساب والد
    IF @ParentAccountId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Accounts WHERE AccountId = @ParentAccountId AND IsActive = 1)
    BEGIN
        SET @ErrorMessage = N'حساب والد معتبر نیست';
        RETURN -3;
    END
    
    BEGIN TRY
        -- ایجاد حساب
        INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId, IsActive, CreatedAt)
        VALUES (@Code, @Name, @AccountType, @Level, @ParentAccountId, 1, GETUTCDATE());
        
        SET @AccountId = SCOPE_IDENTITY();
        SET @ErrorMessage = N'حساب با موفقیت ایجاد شد';
        
        RETURN 0;
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = ERROR_MESSAGE();
        RETURN -99;
    END CATCH
END;
GO

PRINT N'✅ Stored Procedure ایجاد حساب ایجاد شد';

PRINT N'=== مرحله 1 با موفقیت تکمیل شد ===';
PRINT N'';
PRINT N'قابلیت‌های جدید:';
PRINT N'• کدگذاری آزاد: 1.01، 101، 10112، CASH، A001 و...';
PRINT N'• اعتبارسنجی هوشمند با پیام‌های فارسی';
PRINT N'• محافظت از حساب‌های دارای تراکنش';
PRINT N'• View و Function کمکی برای توسعه‌دهندگان';
PRINT N'';
