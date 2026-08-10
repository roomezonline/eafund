-- Initialize Accounts with hierarchical structure (simplified for migration)
    DECLARE @AccountsExists BIT = 0;
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL SET @AccountsExists = 1;

    -- Add Level column if table exists and column doesn't exist
    IF @AccountsExists = 1 AND COL_LENGTH('dbo.Accounts', 'Level') IS NULL
    BEGIN
        ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
        PRINT N'ستون Level اضافه شد';
    END

    -- Clean and populate accounts if table exists
    IF @AccountsExists = 1
    BEGIN
        -- Clean existing data
        DELETE FROM dbo.PostingTemplateLines WHERE AccountId IN (SELECT AccountId FROM dbo.Accounts);
        DELETE FROM dbo.Accounts;
        
        -- Insert hierarchical accounts
        INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES 
            (N'1', N'دارایی', 1, 1, NULL),
            (N'2', N'بدهی', 2, 1, NULL),
            (N'3', N'حقوق صاحبان', 3, 1, NULL),
            (N'4', N'درآمد', 4, 1, NULL),
            (N'5', N'هزینه', 5, 1, NULL);
        
        PRINT N'حساب‌های سلسله‌مراتبی ایجاد شدند';
    END

    -- Add constraints if they don't exist
    IF @AccountsExists = 1 AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy')
    BEGIN
        ALTER TABLE [dbo].[Accounts] 
        ADD CONSTRAINT [CK_Accounts_Hierarchy] 
        CHECK ((Level = 1 AND ParentAccountId IS NULL) OR (Level IN (2,3) AND ParentAccountId IS NOT NULL) OR (Level IS NULL));
        PRINT N'محدودیت سلسله‌مراتب اضافه شد';
    END

    IF @AccountsExists = 1 AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodePattern')
    BEGIN
        ALTER TABLE [dbo].[Accounts] 
        ADD CONSTRAINT [CK_Accounts_CodePattern]
        CHECK ((Level = 1 AND Code LIKE '[1-5]') OR (Level = 2 AND Code LIKE '[1-5].[0-9][0-9]') OR (Level = 3 AND Code LIKE '[1-5].[0-9][0-9].[0-9][0-9][0-9][0-9]') OR (Level IS NULL));
        PRINT N'محدودیت الگوی کد اضافه شد';
    END