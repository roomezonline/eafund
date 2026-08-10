-- Localize default Accounts names to Persian (idempotent)
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
        UPDATE dbo.Accounts SET Name = N'نقد و بانک'             WHERE Code = N'1000' AND Name <> N'نقد و بانک';
        UPDATE dbo.Accounts SET Name = N'سپرده‌های اعضا'          WHERE Code = N'2000' AND Name <> N'سپرده‌های اعضا';
        UPDATE dbo.Accounts SET Name = N'بدهی سود پرداختنی'      WHERE Code = N'2100' AND Name <> N'بدهی سود پرداختنی';
        UPDATE dbo.Accounts SET Name = N'حقوق صاحبان سهام'        WHERE Code = N'3000' AND Name <> N'حقوق صاحبان سهام';
        UPDATE dbo.Accounts SET Name = N'درآمد سود'               WHERE Code = N'4000' AND Name <> N'درآمد سود';
        UPDATE dbo.Accounts SET Name = N'درآمد جریمه دیرکرد'      WHERE Code = N'4100' AND Name <> N'درآمد جریمه دیرکرد';
        UPDATE dbo.Accounts SET Name = N'درآمد کارمزد'            WHERE Code = N'4200' AND Name <> N'درآمد کارمزد';
        UPDATE dbo.Accounts SET Name = N'هزینه سود'               WHERE Code = N'5000' AND Name <> N'هزینه سود';
    END

    -- Add Level column to Accounts for Group-Kol-Moeen hierarchy
    IF COL_LENGTH('dbo.Accounts', 'Level') IS NULL
    BEGIN
        ALTER TABLE [dbo].[Accounts] ADD [Level] TINYINT NULL;
        PRINT N'ستون Level به جدول Accounts اضافه شد';
    END

    -- Add hierarchy and code pattern constraints
    IF COL_LENGTH('dbo.Accounts', 'Level') IS NOT NULL
    BEGIN
        -- Hierarchy constraint: Level 1 (Group) has no parent, Level 2-3 (Kol-Moeen) must have parent
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

-- ===== Pending Batch Separator =====

-- Update existing accounts with proper levels based on code pattern
    IF COL_LENGTH('dbo.Accounts', 'Level') IS NOT NULL
    BEGIN
        UPDATE dbo.Accounts SET Level = 1 WHERE Level IS NULL AND Code LIKE '[1-5]' AND LEN(Code) = 1;
        UPDATE dbo.Accounts SET Level = 2 WHERE Level IS NULL AND Code LIKE '[1-5].[0-9][0-9]' AND LEN(Code) = 4;
        UPDATE dbo.Accounts SET Level = 3 WHERE Level IS NULL AND Code LIKE '[1-5].[0-9][0-9].[0-9][0-9][0-9][0-9]' AND LEN(Code) = 9;
        PRINT N'سطوح موجود در Accounts به‌روزرسانی شدند';
    END

-- ===== Pending Batch Separator =====

CREATE VIEW dbo.vw_JournalLinesFlat AS
    SELECT jl.JournalLineId, jl.JournalId, j.OccurredAt, jl.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType, 
           CASE WHEN COL_LENGTH('dbo.Accounts', 'Level') IS NOT NULL THEN a.Level ELSE NULL END AS Level,
           jl.MemberId, jl.Debit, jl.Credit
    FROM dbo.JournalLines jl
    JOIN dbo.Journals j ON j.JournalId = jl.JournalId
    JOIN dbo.Accounts a ON a.AccountId = jl.AccountId;

-- ===== Pending Batch Separator =====

CREATE VIEW dbo.vw_TrialBalance AS
    SELECT a.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType,
           CASE WHEN COL_LENGTH('dbo.Accounts', 'Level') IS NOT NULL THEN a.Level ELSE NULL END AS Level,
           SUM(jl.Debit) AS TotalDebit, SUM(jl.Credit) AS TotalCredit, SUM(jl.Debit - jl.Credit) AS Net
    FROM dbo.JournalLines jl
    JOIN dbo.Journals j ON j.JournalId = jl.JournalId
    JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
    GROUP BY a.AccountId, a.Code, a.Name, a.AccountType, 
             CASE WHEN COL_LENGTH('dbo.Accounts', 'Level') IS NOT NULL THEN a.Level ELSE NULL END;