-- ==================================================
    -- Double-Entry Layer (with Member dimension on liability lines)
    -- ==================================================

    -- 1) Accounts
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[Accounts] (
            [AccountId]       BIGINT IDENTITY(1,1) NOT NULL,
            [Code]            NVARCHAR(50) NOT NULL,
            [Name]            NVARCHAR(200) NOT NULL,
            [AccountType]     TINYINT NOT NULL, -- 1=Asset, 2=Liability, 3=Equity, 4=Income, 5=Expense
            [ParentAccountId] BIGINT NULL,
            [IsActive]        BIT NOT NULL CONSTRAINT DF_Accounts_IsActive DEFAULT(1),
            [CreatedAt]       DATETIME2(7) NOT NULL CONSTRAINT DF_Accounts_CreatedAt DEFAULT(GETUTCDATE()),
            CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([AccountId] ASC),
            CONSTRAINT [UK_Accounts_Code_Active] UNIQUE NONCLUSTERED ([Code] ASC) WHERE ([IsActive] = 1)
        );
    END