-- Add Level column to Accounts for Group-Kol-Moeen hierarchy FIRST
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL AND COL_LENGTH('dbo.Accounts', 'Level') IS NULL
    BEGIN
        ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
        PRINT N'ستون Level به جدول Accounts اضافه شد';
    END

    -- Seeds: Create hierarchical account groups (Level 1)
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        -- Clear any existing accounts that don't match new hierarchy (handle foreign keys)
        IF EXISTS (SELECT 1 FROM dbo.Accounts WHERE Code NOT LIKE '[1-5]' OR (Code LIKE '[1-5]' AND LEN(Code) > 1))
        BEGIN
            -- Delete dependent records first
            DELETE FROM dbo.PostingTemplateLines WHERE AccountId IN (SELECT AccountId FROM dbo.Accounts WHERE Code NOT LIKE '[1-5]' OR (Code LIKE '[1-5]' AND LEN(Code) > 1));
            DELETE FROM dbo.Accounts WHERE Code NOT LIKE '[1-5]' OR (Code LIKE '[1-5]' AND LEN(Code) > 1);
            PRINT N'حساب‌های قدیمی پاک شدند';
        END
        
        -- Insert Group accounts (Level 1) with proper hierarchy
        IF NOT EXISTS (SELECT 1 FROM dbo.Accounts WHERE Code = N'1') 
            INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES (N'1', N'دارایی', 1, 1, NULL);
        IF NOT EXISTS (SELECT 1 FROM dbo.Accounts WHERE Code = N'2') 
            INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES (N'2', N'بدهی', 2, 1, NULL);
        IF NOT EXISTS (SELECT 1 FROM dbo.Accounts WHERE Code = N'3') 
            INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES (N'3', N'حقوق صاحبان', 3, 1, NULL);
        IF NOT EXISTS (SELECT 1 FROM dbo.Accounts WHERE Code = N'4') 
            INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES (N'4', N'درآمد', 4, 1, NULL);
        IF NOT EXISTS (SELECT 1 FROM dbo.Accounts WHERE Code = N'5') 
            INSERT INTO dbo.Accounts (Code, Name, AccountType, Level, ParentAccountId) VALUES (N'5', N'هزینه', 5, 1, NULL);
        
        PRINT N'حساب‌های گروه (سطح 1) ایجاد شدند';
    END

    -- Add constraints after data is properly set up
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        -- Add hierarchy constraint: Level 1 (Group) has no parent, Level 2-3 (Kol-Moeen) must have parent
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
            PRINT N'محدودیت سلسله‌مراتب CK_Accounts_Hierarchy اضافه شد';
        END

        -- Code pattern constraint: G for Group, G.KK for Kol, G.KK.MMSS for Moeen
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
            PRINT N'محدودیت الگوی کد CK_Accounts_CodePattern اضافه شد';
        END
    END