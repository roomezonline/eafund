-- Initialize Accounts with hierarchical structure (no data seeding, non-destructive)
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        -- Drop old unique constraint if exists
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UK_Accounts_Code' AND object_id = OBJECT_ID(N'[dbo].[Accounts]'))
        BEGIN
            ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [UK_Accounts_Code];
            PRINT 'محدودیت قدیمی UK_Accounts_Code حذف شد';
        END

        -- Add new filtered unique constraint for active records only
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'UK_Accounts_Code_Active')
        BEGIN
            ALTER TABLE [dbo].[Accounts] ADD CONSTRAINT [UK_Accounts_Code_Active] UNIQUE NONCLUSTERED ([Code] ASC) WHERE ([IsActive] = 1);
            PRINT 'محدودیت جدید UK_Accounts_Code_Active اضافه شد';
        END

        -- Add Level column if it doesn't exist
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'Level')
        BEGIN
            ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
            PRINT 'ستون Level به جدول Accounts اضافه شد';
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

        -- Recreate flexible code pattern constraint to allow integer-only or dotted forms
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodePattern')
        BEGIN
            ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_CodePattern];
            PRINT N'محدودیت الگوی کد قبلی حذف شد';
        END

        ALTER TABLE [dbo].[Accounts] 
        ADD CONSTRAINT [CK_Accounts_CodePattern]
        CHECK (
            -- Level 1: single digit 1-5
            (Level = 1 AND Code LIKE '[1-5]') OR

            -- Level 2 (Kol): either dotted G.KK or integer GKK/GKKK (flexible 3-4 digits)
            (Level = 2 AND (
                Code LIKE '[1-5].[0-9][0-9]' OR
                Code LIKE '[1-5][0-9][0-9]' OR
                Code LIKE '[1-5][0-9][0-9][0-9]'
            )) OR

            -- Level 3 (Moeen): either dotted G.MMMM (e.g., 1.0001) or integer GMMMM (e.g., 10001)
            (Level = 3 AND (
                Code LIKE '[1-5].[0-9][0-9][0-9][0-9]' OR
                Code LIKE '[1-5][0-9][0-9][0-9][0-9]'
            )) OR

            (Level IS NULL)
        );
        PRINT N'محدودیت الگوی کد انعطاف‌پذیر اضافه شد (عدد صحیح و فرم نقطه‌دار)';
    END