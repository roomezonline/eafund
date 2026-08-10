-- Initialize Accounts with hierarchical structure (no data seeding, non-destructive)
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        -- Add Level column if not exists
        IF COL_LENGTH('dbo.Accounts', 'Level') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
            PRINT N'ستون Level به جدول Accounts اضافه شد';
        END

        -- Add constraints (idempotent)
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