-- Initialize Accounts with hierarchical structure (all in one block)
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        -- Add Level column if not exists
        IF COL_LENGTH('dbo.Accounts', 'Level') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
            PRINT N'ستون Level به جدول Accounts اضافه شد';
        END

        -- Clean up existing data that conflicts with new hierarchy (guard dependent tables)
        IF EXISTS (SELECT 1 FROM dbo.Accounts)
        BEGIN
            -- Remove all PostingTemplateLines first to avoid FK conflicts (guard existence to avoid name resolution)
            IF OBJECT_ID(N'dbo.PostingTemplateLines', N'U') IS NOT NULL
                EXEC(N'DELETE FROM dbo.PostingTemplateLines');

            -- Remove all existing accounts
            DELETE FROM dbo.Accounts;
            PRINT N'داده‌های قدیمی پاک شدند';
        END
        
        -- Insert Group accounts (Level 1) with proper hierarchy
        INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES 
            (N'1', N'دارایی', 1, 1, NULL),
            (N'2', N'بدهی', 2, 1, NULL),
            (N'3', N'حقوق صاحبان', 3, 1, NULL),
            (N'4', N'درآمد', 4, 1, NULL),
            (N'5', N'هزینه', 5, 1, NULL);
        
        PRINT N'حساب‌های گروه (سطح 1) ایجاد شدند';

        -- Add constraints
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy')
        BEGIN
            ALTER TABLE [dbo].[Accounts] 
            ADD CONSTRAINT [CK_Accounts_Hierarchy] 
            CHECK (
                (Level = 1 AND ParentAccountId IS NULL) OR
                (Level = 2 AND ParentAccountId IS NOT NULL) OR  
                (Level = 3 AND ParentAccountId IS NOT NULL) OR
                (Level IS NULL)
            );
            PRINT N'محدودیت سلسله‌مراتب اضافه شد';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodePattern')
        BEGIN
            ALTER TABLE [dbo].[Accounts] 
            ADD CONSTRAINT [CK_Accounts_CodePattern]
            CHECK (
                (Level = 1 AND Code LIKE '[1-5]') OR
                (Level = 2 AND Code LIKE '[1-5].[0-9][0-9]') OR
                (Level = 3 AND Code LIKE '[1-5].[0-9][0-9].[0-9][0-9][0-9][0-9]') OR
                (Level IS NULL)
            );
            PRINT N'محدودیت الگوی کد اضافه شد';
        END
    END