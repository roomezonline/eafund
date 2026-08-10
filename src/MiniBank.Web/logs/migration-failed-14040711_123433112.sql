-- Initialize Accounts constraints and indexes (idempotent)
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        -- Drop old unique CONSTRAINT if exists
        IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UK_Accounts_Code' AND parent_object_id = OBJECT_ID(N'[dbo].[Accounts]'))
        BEGIN
            ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [UK_Accounts_Code];
            PRINT 'محدودیت قدیمی UK_Accounts_Code حذف شد';
        END

        -- Drop previous filtered index if exists (rename-safe)
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Accounts_Code_Active' AND object_id = OBJECT_ID(N'[dbo].[Accounts]'))
        BEGIN
            DROP INDEX [IX_Accounts_Code_Active] ON [dbo].[Accounts];
            PRINT 'ایندکس قبلی IX_Accounts_Code_Active حذف شد';
        END

        -- Create filtered UNIQUE INDEX for active rows only
        CREATE UNIQUE NONCLUSTERED INDEX [IX_Accounts_Code_Active]
        ON [dbo].[Accounts]([Code] ASC)
        WHERE ([IsActive] = 1);
        PRINT 'ایندکس یکتای فیلترشده IX_Accounts_Code_Active ایجاد شد';
    END

    -- Add Level column if it doesn't exist
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'Level')
        BEGIN
            ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
            PRINT 'ستون Level به جدول Accounts اضافه شد';
        END
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