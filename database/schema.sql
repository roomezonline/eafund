-- ==================================================
-- MiniBank Database Schema - SQL Server
-- ==================================================
-- این فایل شامل تمامی جداول، ایندکس‌ها و تغییرات بانک اطلاعاتی است
-- تاریخ ایجاد: 2025-01-18
-- آخرین بروزرسانی: 2025-01-18
-- ==================================================

-- ==================================================
-- جدول خانوارها (Households)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Households]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Households] (
        [HouseholdId] BIGINT IDENTITY(1,1) NOT NULL,
        [HouseholdCode] NVARCHAR(50) NOT NULL,
        [Name] NVARCHAR(200) NOT NULL,
        [OwnerMemberId] BIGINT NULL,
        [IsActive] BIT NOT NULL DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
        
        CONSTRAINT [PK_Households] PRIMARY KEY CLUSTERED ([HouseholdId] ASC),
        CONSTRAINT [UK_Households_HouseholdCode] UNIQUE NONCLUSTERED ([HouseholdCode] ASC)
    )
    
    PRINT 'جدول Households ایجاد شد'
END
ELSE
BEGIN
    PRINT 'جدول Households از قبل موجود است'
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CreditHolds]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[CreditHolds] (
        [HoldId] BIGINT IDENTITY(1,1) NOT NULL,
        [LoanId] BIGINT NOT NULL,
        [MemberId] BIGINT NOT NULL,
        [MemberCode] NVARCHAR(32) NULL,
        [Role] TINYINT NOT NULL, -- 1=Applicant,2=Guarantor
        [LockPercent] TINYINT NOT NULL,
        [BaseCapitalAmount] DECIMAL(18,2) NOT NULL,
        [LockedAmount] DECIMAL(18,2) NOT NULL,
        [Status] TINYINT NOT NULL, -- 1=Active,2=Released,3=Cancelled
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_CreditHolds_CreatedAt DEFAULT(GETUTCDATE()),
        [ReleasedAt] DATETIME2(7) NULL,
        CONSTRAINT [PK_CreditHolds] PRIMARY KEY CLUSTERED ([HoldId] ASC)
    );
    PRINT N'جدول CreditHolds ایجاد شد';
END
GO


-- (moved) Backfill block will run after DepositTypes creation and column migrations

-- ==================================================
-- جدول انواع سپرده داخلی صندوق (DepositRatePlans)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DepositRatePlans]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[DepositRatePlans] (
        [PlanId] BIGINT IDENTITY(1,1) NOT NULL,
        [Mode] TINYINT NOT NULL, -- 1=Single, 2=Tiered
        [EffectiveFrom] DATE NOT NULL,
        [EffectiveTo] DATE NULL,
        [SingleRatePercent] DECIMAL(5,2) NULL,
        [Notes] NVARCHAR(400) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_DepositRatePlans_IsActive DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_DepositRatePlans_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_DepositRatePlans] PRIMARY KEY CLUSTERED ([PlanId] ASC)
    )
    PRINT N'جدول DepositRatePlans ایجاد شد'
END
ELSE
BEGIN
    PRINT N'جدول DepositRatePlans از قبل موجود است'
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DepositRateTiers]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[DepositRateTiers] (
        [TierId] BIGINT IDENTITY(1,1) NOT NULL,
        [PlanId] BIGINT NOT NULL,
        [OrderNo] INT NOT NULL,
        [FromAmount] DECIMAL(18,2) NOT NULL,
        [ToAmount] DECIMAL(18,2) NULL,
        [RatePercent] DECIMAL(5,2) NOT NULL,
        CONSTRAINT [PK_DepositRateTiers] PRIMARY KEY CLUSTERED ([TierId] ASC),
        CONSTRAINT [FK_DepositRateTiers_DepositRatePlans] FOREIGN KEY ([PlanId]) REFERENCES [dbo].[DepositRatePlans]([PlanId]) ON DELETE CASCADE
    )
    PRINT N'جدول DepositRateTiers ایجاد شد'
END
ELSE
BEGIN
    PRINT N'جدول DepositRateTiers از قبل موجود است'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[DepositRateTiers]') AND name = N'IX_DepositRateTiers_PlanId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DepositRateTiers_PlanId] ON [dbo].[DepositRateTiers] ([PlanId] ASC);
    PRINT N'ایندکس IX_DepositRateTiers_PlanId ایجاد شد'
END
GO

-- Migration: drop DepositTypes, DepositInterestRates, MemberDepositAccounts and related foreign keys/columns in an idempotent way
-- ==================================================
IF OBJECT_ID(N'dbo.DepositInterestRates', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DepositInterestRates_DepositTypes')
        ALTER TABLE [dbo].[DepositInterestRates] DROP CONSTRAINT [FK_DepositInterestRates_DepositTypes];
    DROP TABLE [dbo].[DepositInterestRates];
END
GO

IF OBJECT_ID(N'dbo.DepositTypes', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Transactions_DepositTypes')
        ALTER TABLE [dbo].[Transactions] DROP CONSTRAINT [FK_Transactions_DepositTypes];
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MDA_DepositTypes')
        ALTER TABLE [dbo].[MemberDepositAccounts] DROP CONSTRAINT [FK_MDA_DepositTypes];
    DROP TABLE [dbo].[DepositTypes];
END
GO

-- Ensure there is no overlapping EffectiveFrom/EffectiveTo per DepositType (helper check can be added in app layer)


-- ==================================================
-- Migration: Ensure Members.HouseholdCode exists (no backfill)
-- ==================================================
IF COL_LENGTH('dbo.Members', 'HouseholdCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members]
    ADD [HouseholdCode] NVARCHAR(50) NULL;
    PRINT 'ستون HouseholdCode به جدول Members اضافه شد';

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_HouseholdCode')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_Members_HouseholdCode] ON [dbo].[Members] ([HouseholdCode] ASC);
        PRINT 'ایندکس IX_Members_HouseholdCode ایجاد شد';
    END

    IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = N'FK_Members_Households_ByCode')
    BEGIN
        ALTER TABLE [dbo].[Members]
        ADD CONSTRAINT [FK_Members_Households_ByCode] FOREIGN KEY ([HouseholdCode]) REFERENCES [dbo].[Households] ([HouseholdCode]);
        PRINT 'Foreign Key FK_Members_Households_ByCode ایجاد شد';
    END
END
GO

-- Ensure FatherName exists on existing databases as well (idempotent)
IF COL_LENGTH('dbo.Members', 'FatherName') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members]
    ADD [FatherName] NVARCHAR(100) NULL;
    PRINT 'ستون FatherName به جدول Members اضافه شد (post-create)'
END
GO

-- ==================================================
-- ایندکس‌های جدول خانوارها
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Households]') AND name = N'IX_Households_Name')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Households_Name] ON [dbo].[Households] ([Name] ASC)
    PRINT 'ایندکس IX_Households_Name ایجاد شد'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Households]') AND name = N'IX_Households_IsActive')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Households_IsActive] ON [dbo].[Households] ([IsActive] ASC)
    PRINT 'ایندکس IX_Households_IsActive ایجاد شد'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Households]') AND name = N'IX_Households_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Households_CreatedAt] ON [dbo].[Households] ([CreatedAt] DESC)
    PRINT 'ایندکس IX_Households_CreatedAt ایجاد شد'
END
GO

-- ==================================================
-- داده‌های اولیه (فقط در صورت درخواست)
-- ==================================================
-- داده‌های اولیه فقط در صورت نیاز و درخواست صریح اضافه خواهد شد
GO

-- ==================================================
-- جدول اعضای خانوار (Members)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Members] (
        [MemberId] BIGINT  NOT NULL,
        [FirstName] NVARCHAR(100) NOT NULL,
        [FatherName] NVARCHAR(100) NULL,
        [LastName] NVARCHAR(100) NOT NULL,
        [NationalCode] NVARCHAR(10) NOT NULL,
        [MobileNumber] NVARCHAR(15) NOT NULL,
        [HouseholdCode] NVARCHAR(50) NOT NULL,
        [IsActive] BIT NOT NULL DEFAULT(1),
        [JoinedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
        [CreatedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
        
        CONSTRAINT [PK_Members] PRIMARY KEY CLUSTERED ([MemberId] ASC),
        CONSTRAINT [UK_Members_NationalCode] UNIQUE NONCLUSTERED ([NationalCode] ASC),
        CONSTRAINT [FK_Members_HouseholdCode] FOREIGN KEY ([HouseholdCode]) REFERENCES [dbo].[Households] ([HouseholdCode])
    )
    
    PRINT 'جدول Members ایجاد شد'
END
ELSE
BEGIN
    PRINT 'جدول Members از قبل موجود است'
END
GO

-- ==================================================
-- ایندکس‌های جدول اعضا
-- ==================================================
-- (Deprecated) IX_Members_HouseholdId removed: relation uses HouseholdCode only

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_Name')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Members_Name] ON [dbo].[Members] ([FirstName] ASC, [LastName] ASC)
    PRINT 'ایندکس IX_Members_Name ایجاد شد'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_IsActive')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Members_IsActive] ON [dbo].[Members] ([IsActive] ASC)
    PRINT 'ایندکس IX_Members_IsActive ایجاد شد'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_JoinedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Members_JoinedAt] ON [dbo].[Members] ([JoinedAt] DESC)
    PRINT 'ایندکس IX_Members_JoinedAt ایجاد شد'
END
GO

-- ==================================================
-- Foreign Key برای OwnerMemberId در جدول Households
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Households_Members_Owner]') AND parent_object_id = OBJECT_ID(N'[dbo].[Households]'))
BEGIN
    ALTER TABLE [dbo].[Households]
    ADD CONSTRAINT [FK_Households_Members_Owner] FOREIGN KEY ([OwnerMemberId]) REFERENCES [dbo].[Members] ([MemberId])
    PRINT 'Foreign Key FK_Households_Members_Owner ایجاد شد'
END
GO

-- ==================================================
-- تاریخچه تغییرات
-- ==================================================
-- v1.0 - 2025-01-18: ایجاد جدول اولیه خانوارها
-- v1.1 - 2025-01-18: ایجاد جدول اعضای خانوار و ارتباط با خانوارها
-- 
-- جداول آینده:
-- - Deposits (سپرده‌ها)
-- - Loans (وام‌ها)
-- - Transactions (تراکنش‌ها)
-- ==================================================

-- ==================================================
-- جدول حساب‌های بانکی صندوق (BankAccounts)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[BankAccounts] (
        [AccountId] BIGINT IDENTITY(1,1) NOT NULL,
        [BankName] NVARCHAR(200) NOT NULL,
        [AccountNumber] NVARCHAR(50) NOT NULL,
        [CardNumber] NVARCHAR(20) NULL,
        [Shaba] NVARCHAR(26) NULL,
        [AccountType] NVARCHAR(50) NULL,
        [InterestRate] DECIMAL(5,2) NULL,
        [IsActive] BIT NOT NULL DEFAULT(1),
        [OpenedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
        [CreatedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_BankAccounts] PRIMARY KEY CLUSTERED ([AccountId] ASC)
    )

    PRINT N'جدول BankAccounts ایجاد شد'
END
ELSE
BEGIN
    PRINT N'جدول BankAccounts از قبل موجود است'
END
GO

-- ایندکس‌ها و محدودیت‌های پیشنهادی برای سرعت و یکتا بودن‌های اختیاری
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_BankName')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BankAccounts_BankName] ON [dbo].[BankAccounts] ([BankName] ASC)
    PRINT N'ایندکس IX_BankAccounts_BankName ایجاد شد'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_IsActive')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BankAccounts_IsActive] ON [dbo].[BankAccounts] ([IsActive] ASC)
    PRINT N'ایندکس IX_BankAccounts_IsActive ایجاد شد'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BankAccounts_CreatedAt] ON [dbo].[BankAccounts] ([CreatedAt] DESC)
    PRINT N'ایندکس IX_BankAccounts_CreatedAt ایجاد شد'
END
GO

-- Link BankAccounts to GL Accounts (معین) via GlAccountId
IF COL_LENGTH('dbo.BankAccounts', 'GlAccountId') IS NULL
BEGIN
    ALTER TABLE [dbo].[BankAccounts] ADD [GlAccountId] BIGINT NULL;
    PRINT N'ستون GlAccountId به جدول BankAccounts اضافه شد';
END

IF COL_LENGTH('dbo.BankAccounts', 'GlAccountId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BankAccounts_GLAccount')
    BEGIN
        ALTER TABLE [dbo].[BankAccounts]
        ADD CONSTRAINT [FK_BankAccounts_GLAccount]
        FOREIGN KEY ([GlAccountId]) REFERENCES [dbo].[Accounts]([AccountId]);
        PRINT N'کلید خارجی FK_BankAccounts_GLAccount ایجاد شد';
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_GlAccountId')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_BankAccounts_GlAccountId] ON [dbo].[BankAccounts]([GlAccountId]);
        PRINT N'ایندکس IX_BankAccounts_GlAccountId ایجاد شد';
    END
END
GO

-- ==================================================
-- جدول کارت‌های بانکی اطلاع‌رسانی برای اعضا (BankCards)
-- مستقل از حساب‌های بانکی حسابداری (BankAccounts)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[BankCards]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[BankCards] (
        [CardId]        BIGINT IDENTITY(1,1) NOT NULL,
        [BankName]      NVARCHAR(200) NOT NULL,
        [CardNumber]    NVARCHAR(32)  NOT NULL,
        [AccountNumber] NVARCHAR(50)  NULL,
        [Shaba]         NVARCHAR(32)  NULL,
        [IsActive]      BIT NOT NULL DEFAULT(1),
        [CreatedAt]     DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
        [UpdatedAt]     DATETIME2(7) NULL,
        CONSTRAINT [PK_BankCards] PRIMARY KEY CLUSTERED ([CardId] ASC)
    )

    PRINT N'جدول BankCards ایجاد شد'
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankCards]') AND name = N'IX_BankCards_IsActive')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BankCards_IsActive] ON [dbo].[BankCards] ([IsActive] ASC, [CreatedAt] DESC);
    PRINT N'ایندکس IX_BankCards_IsActive ایجاد شد'
END
GO

-- ==================================================
-- Legacy TransactionGroups and TransactionTypes removed
-- Using double-entry accounting with Chart of Accounts instead
-- ==================================================

-- TransactionTypes table removed - using PostingTemplates instead
GO

-- ==================================================
-- HouseholdReceipts + HouseholdReceiptAllocations
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceipts]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[HouseholdReceipts] (
        [HouseholdReceiptId]        BIGINT IDENTITY(1,1) NOT NULL,
        [TargetHouseholdId]         BIGINT NULL,
        [DocumentType]              TINYINT NOT NULL CONSTRAINT DF_HouseholdReceipts_DocumentType DEFAULT(1),
        [TransactionType]           NVARCHAR(50) NULL,
        [CounterpartyName]          NVARCHAR(200) NULL,
        [AmountTotal]               DECIMAL(18,2) NOT NULL CHECK (AmountTotal > 0),
        [PayChannel]                TINYINT NOT NULL CONSTRAINT DF_HouseholdReceipts_PayChannel DEFAULT(1),
        [OccurredAt]                DATETIME2(7) NOT NULL,
        [CreatedAt]                 DATETIME2(7) NOT NULL CONSTRAINT DF_HouseholdReceipts_CreatedAt DEFAULT(GETUTCDATE()),
        [CreatedBy]                 NVARCHAR(128) NOT NULL,
        [Status]                    TINYINT NOT NULL CONSTRAINT DF_HouseholdReceipts_Status DEFAULT(0),
        [Notes]                     NVARCHAR(500) NULL,
        [ExternalRef]               NVARCHAR(128) NULL,
        CONSTRAINT [PK_HouseholdReceipts] PRIMARY KEY CLUSTERED ([HouseholdReceiptId] ASC),
        CONSTRAINT [FK_HR_TargetHousehold] FOREIGN KEY ([TargetHouseholdId]) REFERENCES [dbo].[Households] ([HouseholdId])
    );
END
GO

-- HouseholdReceipts migrations (idempotent)
IF OBJECT_ID(N'dbo.HouseholdReceipts', N'U') IS NOT NULL
BEGIN
    -- Ensure TargetHouseholdId is nullable for non-member receipts
    IF COL_LENGTH('dbo.HouseholdReceipts', 'TargetHouseholdId') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HR_TargetHousehold')
        BEGIN
            ALTER TABLE dbo.HouseholdReceipts DROP CONSTRAINT [FK_HR_TargetHousehold];
            PRINT N'FK_HR_TargetHousehold موقتاً حذف شد برای تغییر نوع ستون';
        END

        BEGIN TRY
            ALTER TABLE dbo.HouseholdReceipts ALTER COLUMN [TargetHouseholdId] BIGINT NULL;
            PRINT N'ستون TargetHouseholdId در HouseholdReceipts nullable شد';
        END TRY
        BEGIN CATCH
            PRINT N'عدم موفقیت در ALTER COLUMN TargetHouseholdId (ممکن است از قبل nullable باشد)';
        END CATCH

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HR_TargetHousehold')
        BEGIN
            ALTER TABLE dbo.HouseholdReceipts
            ADD CONSTRAINT [FK_HR_TargetHousehold] FOREIGN KEY ([TargetHouseholdId]) REFERENCES [dbo].[Households] ([HouseholdId]);
            PRINT N'FK_HR_TargetHousehold مجدداً ایجاد شد';
        END
    END

    -- Add new columns if missing
    IF COL_LENGTH('dbo.HouseholdReceipts', 'DocumentType') IS NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts ADD [DocumentType] TINYINT NOT NULL CONSTRAINT DF_HouseholdReceipts_DocumentType DEFAULT(1);
        PRINT N'ستون DocumentType به HouseholdReceipts اضافه شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'TransactionType') IS NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts ADD [TransactionType] NVARCHAR(50) NULL;
        PRINT N'ستون TransactionType به HouseholdReceipts اضافه شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'CounterpartyName') IS NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts ADD [CounterpartyName] NVARCHAR(200) NULL;
        PRINT N'ستون CounterpartyName به HouseholdReceipts اضافه شد';
    END

    -- Drop legacy foreign keys if present
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HR_PayerMember')
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP CONSTRAINT [FK_HR_PayerMember];
        PRINT N'FK_HR_PayerMember حذف شد';
    END

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HR_BankAccount')
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP CONSTRAINT [FK_HR_BankAccount];
        PRINT N'FK_HR_BankAccount حذف شد';
    END

    -- Helper: drop default constraint for a column (if any)
    DECLARE @sql NVARCHAR(MAX);

    -- Drop legacy columns if they exist
    IF COL_LENGTH('dbo.HouseholdReceipts', 'PayerType') IS NOT NULL
    BEGIN
        SELECT @sql = 'ALTER TABLE dbo.HouseholdReceipts DROP CONSTRAINT [' + dc.name + '];'
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.HouseholdReceipts') AND c.name = 'PayerType';
        IF @sql IS NOT NULL EXEC sp_executesql @sql;
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [PayerType];
        PRINT N'ستون PayerType حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'PayerMemberId') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [PayerMemberId];
        PRINT N'ستون PayerMemberId حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'PayerExternalFullName') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [PayerExternalFullName];
        PRINT N'ستون PayerExternalFullName حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'PayerExternalNationalId') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [PayerExternalNationalId];
        PRINT N'ستون PayerExternalNationalId حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'PayerExternalMobile') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [PayerExternalMobile];
        PRINT N'ستون PayerExternalMobile حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'BankAccountId') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [BankAccountId];
        PRINT N'ستون BankAccountId حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'SubsidiaryAccountId') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [SubsidiaryAccountId];
        PRINT N'ستون SubsidiaryAccountId حذف شد';
    END

    IF COL_LENGTH('dbo.HouseholdReceipts', 'AllowCrossHouseholdAllocations') IS NOT NULL
    BEGIN
        SELECT @sql = 'ALTER TABLE dbo.HouseholdReceipts DROP CONSTRAINT [' + dc.name + '];'
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.HouseholdReceipts') AND c.name = 'AllowCrossHouseholdAllocations';
        IF @sql IS NOT NULL EXEC sp_executesql @sql;
        ALTER TABLE dbo.HouseholdReceipts DROP COLUMN [AllowCrossHouseholdAllocations];
        PRINT N'ستون AllowCrossHouseholdAllocations حذف شد';
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceipts]') AND name = N'IX_HR_TargetHousehold_OccurredAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HR_TargetHousehold_OccurredAt]
    ON [dbo].[HouseholdReceipts] ([TargetHouseholdId] ASC, [OccurredAt] DESC)
    INCLUDE ([Status], [AmountTotal]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceiptAllocations]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[HouseholdReceiptAllocations] (
        [AllocationId]          BIGINT IDENTITY(1,1) NOT NULL,
        [HouseholdReceiptId]    BIGINT NOT NULL,
        [MemberId]              BIGINT NULL,
        [Amount]                DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
        [PurposeType]           TINYINT NOT NULL CONSTRAINT DF_HRA_PurposeType DEFAULT(0),
        [PostingTemplateId]     INT NULL,
        [AmountPrincipal]       DECIMAL(18,2) NULL,
        [AmountInterest]        DECIMAL(18,2) NULL,
        [AmountPenalty]         DECIMAL(18,2) NULL,
        [Status]                TINYINT NOT NULL CONSTRAINT DF_HRA_Status DEFAULT(0),
        [SortOrder]             INT NOT NULL CONSTRAINT DF_HRA_SortOrder DEFAULT(0),
        [Notes]                 NVARCHAR(500) NULL,
        CONSTRAINT [PK_HouseholdReceiptAllocations] PRIMARY KEY CLUSTERED ([AllocationId] ASC),
        CONSTRAINT [FK_HRA_Receipt] FOREIGN KEY ([HouseholdReceiptId]) REFERENCES [dbo].[HouseholdReceipts] ([HouseholdReceiptId])
    );
END
GO

-- HouseholdReceiptAllocations migrations (idempotent)
IF OBJECT_ID(N'dbo.HouseholdReceiptAllocations', N'U') IS NOT NULL
BEGIN
    -- Drop legacy indexes that depend on legacy columns (must run before DROP COLUMN)
    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceiptAllocations]')
          AND name = N'IX_HRA_Member_Status'
    )
    BEGIN
        DROP INDEX [IX_HRA_Member_Status] ON [dbo].[HouseholdReceiptAllocations];
        PRINT N'ایندکس IX_HRA_Member_Status حذف شد';
    END

    -- Drop legacy foreign keys if present
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HRA_BeneficiaryHousehold')
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP CONSTRAINT [FK_HRA_BeneficiaryHousehold];
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HRA_Member')
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP CONSTRAINT [FK_HRA_Member];
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_HRA_DepositType')
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP CONSTRAINT [FK_HRA_DepositType];

    -- Drop legacy columns if they exist
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'BeneficiaryHouseholdId') IS NOT NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP COLUMN [BeneficiaryHouseholdId];
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'DepositTypeId') IS NOT NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP COLUMN [DepositTypeId];

    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'PurposeType') IS NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations ADD [PurposeType] TINYINT NOT NULL CONSTRAINT DF_HRA_PurposeType DEFAULT(0);
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'PostingTemplateId') IS NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations ADD [PostingTemplateId] INT NULL;
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'AmountPrincipal') IS NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations ADD [AmountPrincipal] DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'AmountInterest') IS NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations ADD [AmountInterest] DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'AmountPenalty') IS NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations ADD [AmountPenalty] DECIMAL(18,2) NULL;

    -- Allow MemberId to be NULL in new model
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'MemberId') IS NOT NULL
    BEGIN
        BEGIN TRY
            ALTER TABLE dbo.HouseholdReceiptAllocations ALTER COLUMN [MemberId] BIGINT NULL;
        END TRY
        BEGIN CATCH
        END CATCH
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceiptAllocations]') AND name = N'IX_HRA_Receipt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HRA_Receipt]
    ON [dbo].[HouseholdReceiptAllocations] ([HouseholdReceiptId] ASC);
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceiptAllocations]') AND name = N'IX_HRA_Member_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HRA_Member_Status]
    ON [dbo].[HouseholdReceiptAllocations] ([MemberId] ASC, [Status] ASC)
    INCLUDE ([Amount]);
END
GO

-- ==================================================
-- Transactions (تراکنش‌ها)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Transactions] (
        [TransactionId]    BIGINT IDENTITY(1,1) NOT NULL,
        [TypeId]           BIGINT NOT NULL,
        [HouseholdId]      BIGINT NULL,
        [MemberId]         BIGINT NULL,
        [AmountTotal]      DECIMAL(18,2) NOT NULL CHECK (AmountTotal >= 0),
        [AmountPrincipal]  DECIMAL(18,2) NULL,
        [AmountInterest]   DECIMAL(18,2) NULL,
        [AmountPenalty]    DECIMAL(18,2) NULL,
        [AmountFee]        DECIMAL(18,2) NULL,
        [AmountTax]        DECIMAL(18,2) NULL,
        [OccurredAt]       DATETIME2(7) NOT NULL,
        [CreatedAt]        DATETIME2(7) NOT NULL CONSTRAINT DF_Transactions_CreatedAt DEFAULT(GETUTCDATE()),
        [CreatedBy]        NVARCHAR(128) NOT NULL,
        [Status]           TINYINT NOT NULL CONSTRAINT DF_Transactions_Status DEFAULT(0),
        [ParentReceiptId]  BIGINT NULL,
        [AllocationId]     BIGINT NULL,
        [Notes]            NVARCHAR(500) NULL,
        [ExternalRef]      NVARCHAR(128) NULL,
        [IdempotencyKey]   NVARCHAR(128) NULL,
        CONSTRAINT [PK_Transactions] PRIMARY KEY CLUSTERED ([TransactionId] ASC)
    );

    -- FKs (در صورت وجود جداول مرجع)
    IF OBJECT_ID(N'dbo.TransactionTypes', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Transactions_Types')
        ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [FK_Transactions_Types] FOREIGN KEY ([TypeId]) REFERENCES [dbo].[TransactionTypes] ([TypeId]);
    IF OBJECT_ID(N'dbo.Households', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Transactions_Households')
        ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [FK_Transactions_Households] FOREIGN KEY ([HouseholdId]) REFERENCES [dbo].[Households] ([HouseholdId]);
    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Transactions_Members')
        ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [FK_Transactions_Members] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members] ([MemberId]);
    IF OBJECT_ID(N'dbo.HouseholdReceipts', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Transactions_ParentReceipt')
        ALTER TABLE [dbo].[Transactions] ADD CONSTRAINT [FK_Transactions_ParentReceipt] FOREIGN KEY ([ParentReceiptId]) REFERENCES [dbo].[HouseholdReceipts] ([HouseholdReceiptId]);

    -- ایندکس‌ها
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND name = N'UQ_Transactions_Idempotency')
        CREATE UNIQUE INDEX [UQ_Transactions_Idempotency] ON [dbo].[Transactions] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL;
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND name = N'IX_Transactions_Type_OccurredAt')
        CREATE NONCLUSTERED INDEX [IX_Transactions_Type_OccurredAt] ON [dbo].[Transactions] ([TypeId] ASC, [OccurredAt] DESC) INCLUDE ([AmountTotal], [Status]);
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND name = N'IX_Transactions_Member_OccurredAt')
        CREATE NONCLUSTERED INDEX [IX_Transactions_Member_OccurredAt] ON [dbo].[Transactions] ([MemberId] ASC, [OccurredAt] DESC) INCLUDE ([AmountTotal], [Status]);
END
GO

-- ==================================================
-- Persian demo seed data (idempotent) - DISABLED by default
-- Enable by setting @EnableDemoSeed = 1
DECLARE @EnableDemoSeed BIT = 0;
IF (@EnableDemoSeed = 1)
BEGIN
    -- Sample household
    IF OBJECT_ID(N'dbo.Households', N'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Households WHERE HouseholdCode = N'H001')
        BEGIN
            INSERT INTO dbo.Households (HouseholdCode, Name, OwnerMemberId, IsActive, CreatedAt)
            VALUES (N'H001', N'خانوار حسینی', NULL, 1, GETUTCDATE());
        END
    END

    -- Sample members (use fixed MemberId to stay idempotent)
    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Members WHERE MemberId = 1001)
        BEGIN
            INSERT INTO dbo.Members (MemberId, FirstName, FatherName, LastName, NationalCode, MobileNumber, HouseholdCode, IsActive, JoinedAt, CreatedAt)
            VALUES (1001, N'سید مصطفی', N'سید جواد', N'حسینی', N'1911756311', N'09120000001', N'H001', 1, GETUTCDATE(), GETUTCDATE());
        END
        IF NOT EXISTS (SELECT 1 FROM dbo.Members WHERE MemberId = 1002)
        BEGIN
            INSERT INTO dbo.Members (MemberId, FirstName, FatherName, LastName, NationalCode, MobileNumber, HouseholdCode, IsActive, JoinedAt, CreatedAt)
            VALUES (1002, N'زهرا', N'محمدرضا', N'کریمی', N'0045678901', N'09120000002', N'H001', 1, GETUTCDATE(), GETUTCDATE());
        END
    END

    -- Sample bank account
    IF OBJECT_ID(N'dbo.BankAccounts', N'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.BankAccounts WHERE BankName=N'بانک ملت' AND AccountNumber=N'6037991234567890')
        BEGIN
            INSERT INTO dbo.BankAccounts (BankName, AccountNumber, CardNumber, Shaba, AccountType, InterestRate, IsActive, OpenedAt, CreatedAt)
            VALUES (N'بانک ملت', N'6037991234567890', N'6037991234567890', N'IR820120000000000000000000', N'جاری', NULL, 1, GETUTCDATE(), GETUTCDATE());
        END
    END
END

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
        [MemberId]        BIGINT NULL,
        [IsActive]        BIT NOT NULL CONSTRAINT DF_Accounts_IsActive DEFAULT(1),
        [CreatedAt]       DATETIME2(7) NOT NULL CONSTRAINT DF_Accounts_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([AccountId] ASC)
        -- Uniqueness on Code will be applied via filtered UNIQUE INDEX after table creation
    );
END
GO

IF OBJECT_ID(N'[dbo].[Accounts]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Accounts_Parent')
    BEGIN
        ALTER TABLE [dbo].[Accounts]
        ADD CONSTRAINT [FK_Accounts_Parent] FOREIGN KEY ([ParentAccountId]) REFERENCES [dbo].[Accounts]([AccountId]);
    END

    IF COL_LENGTH('dbo.Accounts', 'MemberId') IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Accounts_Member')
        BEGIN
            ALTER TABLE [dbo].[Accounts]
            ADD CONSTRAINT [FK_Accounts_Member]
            FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]);
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'IX_Accounts_MemberId')
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_Accounts_MemberId] ON [dbo].[Accounts]([MemberId]);
        END
    END
END
GO

-- 2) Journals
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Journals]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Journals] (
        [JournalId]     BIGINT IDENTITY(1,1) NOT NULL,
        [TransactionId] BIGINT NOT NULL,
        [OccurredAt]    DATETIME2(7) NOT NULL,
        [CreatedAt]     DATETIME2(7) NOT NULL CONSTRAINT DF_Journals_CreatedAt DEFAULT(GETUTCDATE()),
        [CreatedBy]     NVARCHAR(128) NOT NULL,
        [Notes]         NVARCHAR(500) NULL,
        [Status]        TINYINT NOT NULL CONSTRAINT DF_Journals_Status DEFAULT 2,
        [IsDeleted]     BIT NOT NULL CONSTRAINT DF_Journals_IsDeleted DEFAULT 0,
        [SerialNumber]  INT NULL,
        [FiscalYearId]  INT NULL,
        [DocumentNumber] INT NULL,
        CONSTRAINT [PK_Journals] PRIMARY KEY CLUSTERED ([JournalId] ASC)
    );
END
GO

IF OBJECT_ID(N'[dbo].[Journals]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Journals_Transactions')
    BEGIN
        ALTER TABLE [dbo].[Journals]
        ADD CONSTRAINT [FK_Journals_Transactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions]([TransactionId]) ON DELETE CASCADE;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Journals]') AND name = N'IX_Journals_OccurredAt')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_Journals_OccurredAt] ON [dbo].[Journals] ([OccurredAt] DESC);
    END
END
GO

-- Migration: Allow manual journals (TransactionId NULL) (idempotent)
IF OBJECT_ID(N'dbo.Journals', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        WHERE o.object_id = OBJECT_ID(N'dbo.Journals')
          AND c.name = N'TransactionId'
          AND c.is_nullable = 0
    )
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Journals_Transactions')
        BEGIN
            ALTER TABLE [dbo].[Journals] DROP CONSTRAINT [FK_Journals_Transactions];
        END

        ALTER TABLE [dbo].[Journals] ALTER COLUMN [TransactionId] BIGINT NULL;
        PRINT N'ستون Journals.TransactionId nullable شد';

        IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Journals_Transactions')
        BEGIN
            ALTER TABLE [dbo].[Journals]
            ADD CONSTRAINT [FK_Journals_Transactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions]([TransactionId]) ON DELETE CASCADE;
        END
    END
END
GO

-- FK_Journals_FiscalYear (V006)
IF OBJECT_ID(N'dbo.Journals', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.FiscalYears', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Journals_FiscalYear')
BEGIN
    ALTER TABLE dbo.Journals ADD CONSTRAINT FK_Journals_FiscalYear
        FOREIGN KEY (FiscalYearId) REFERENCES dbo.FiscalYears(FiscalYearId);
    PRINT N'FK_Journals_FiscalYear اضافه شد';
END
GO

-- SerialCounters table (V006)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SerialCounters')
BEGIN
    CREATE TABLE dbo.SerialCounters (
        FiscalYearId INT NOT NULL PRIMARY KEY REFERENCES FiscalYears(FiscalYearId),
        LastDocumentNumber INT NOT NULL DEFAULT 0
    );
    PRINT N'جدول SerialCounters ایجاد شد';
END
GO

-- TR_Journals_SetFiscalYear trigger (V006)
IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'TR_Journals_SetFiscalYear')
BEGIN
    EXEC ('
    CREATE TRIGGER TR_Journals_SetFiscalYear ON dbo.Journals AFTER INSERT AS
    BEGIN
        SET NOCOUNT ON;
        UPDATE j SET FiscalYearId = f.FiscalYearId
        FROM dbo.Journals j
        JOIN dbo.FiscalYears f ON j.OccurredAt >= f.StartDate 
                               AND j.OccurredAt < DATEADD(DAY, 1, f.EndDate)
        WHERE j.JournalId IN (SELECT JournalId FROM inserted)
          AND j.FiscalYearId IS NULL;
    END
    ');
    PRINT N'تریگر TR_Journals_SetFiscalYear ایجاد شد';
END
GO

-- IX_Journals_FY_DocNum unique index (V006)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Journals_FY_DocNum')
BEGIN
    CREATE UNIQUE INDEX IX_Journals_FY_DocNum 
        ON dbo.Journals(FiscalYearId, DocumentNumber) 
        WHERE DocumentNumber IS NOT NULL;
    PRINT N'ایندکس IX_Journals_FY_DocNum ایجاد شد';
END
GO

    -- 3) JournalLines (with optional MemberId for member-liability lines)
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[JournalLines]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[JournalLines] (
            [JournalLineId] BIGINT IDENTITY(1,1) NOT NULL,
            [JournalId]     BIGINT NOT NULL,
            [LineNo]        INT NOT NULL,
            [AccountId]     BIGINT NOT NULL,
            [MemberId]      BIGINT NULL,
            [Debit]         DECIMAL(18,2) NOT NULL CONSTRAINT DF_JournalLines_Debit DEFAULT(0),
            [Credit]        DECIMAL(18,2) NOT NULL CONSTRAINT DF_JournalLines_Credit DEFAULT(0),
            [Description]   NVARCHAR(300) NULL,
            CONSTRAINT [PK_JournalLines] PRIMARY KEY CLUSTERED ([JournalLineId] ASC)
        );
    END
    GO

-- Migration: Ensure JournalLines.ParentAccountId exists (idempotent)
IF OBJECT_ID(N'dbo.JournalLines', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.JournalLines', 'ParentAccountId') IS NULL
    BEGIN
        ALTER TABLE [dbo].[JournalLines] ADD [ParentAccountId] BIGINT NULL;
        PRINT N'ستون JournalLines.ParentAccountId اضافه شد';
    END

    IF COL_LENGTH('dbo.JournalLines', 'ParentAccountId') IS NOT NULL
       AND OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalLines_ParentAccount')
    BEGIN
        ALTER TABLE [dbo].[JournalLines]
        ADD CONSTRAINT [FK_JournalLines_ParentAccount] FOREIGN KEY ([ParentAccountId]) REFERENCES [dbo].[Accounts]([AccountId]);
        PRINT N'Foreign Key FK_JournalLines_ParentAccount ایجاد شد';
    END

    IF COL_LENGTH('dbo.JournalLines', 'ParentAccountId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[JournalLines]') AND name = N'IX_JournalLines_ParentAccount')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_JournalLines_ParentAccount] ON [dbo].[JournalLines]([ParentAccountId]);
        PRINT N'ایندکس IX_JournalLines_ParentAccount ایجاد شد';
    END
END
GO

-- ==================================================
-- Deposit Interest Calc History (DepositInterestRuns / DepositInterestRunItems)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DepositInterestRuns]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[DepositInterestRuns] (
        [RunId] BIGINT IDENTITY(1,1) NOT NULL,
        [PeriodStartDate] DATE NOT NULL,
        [PostingOccurredAt] DATETIME2(7) NOT NULL,
        [AnnualRatePercent] DECIMAL(9,4) NOT NULL,
        [DaysInMonth] INT NOT NULL,
        [CreditsCutoffToExclusive] DATETIME2(7) NOT NULL,
        [DebitsCutoffToExclusive] DATETIME2(7) NOT NULL,
        [SumPrincipal] DECIMAL(18,2) NOT NULL,
        [SumInterest] DECIMAL(18,2) NOT NULL,
        [SumNet] DECIMAL(18,2) NOT NULL,
        [SumProfit] DECIMAL(18,2) NOT NULL,
        [MemberCount] INT NOT NULL,
        [NeutralCount] INT NOT NULL,
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_DepositInterestRuns_CreatedAt DEFAULT(GETUTCDATE()),
        [CreatedBy] NVARCHAR(128) NOT NULL,
        CONSTRAINT [PK_DepositInterestRuns] PRIMARY KEY CLUSTERED ([RunId] ASC)
    );
    PRINT N'جدول DepositInterestRuns ایجاد شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[DepositInterestRuns]') AND name = N'UX_DepositInterestRuns_PeriodStartDate')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UX_DepositInterestRuns_PeriodStartDate] ON [dbo].[DepositInterestRuns]([PeriodStartDate]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DepositInterestRunItems]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[DepositInterestRunItems] (
        [RunItemId] BIGINT IDENTITY(1,1) NOT NULL,
        [RunId] BIGINT NOT NULL,
        [MemberId] BIGINT NOT NULL,
        [MemberCode] NVARCHAR(50) NULL,
        [MemberName] NVARCHAR(250) NOT NULL,
        [NetPrincipal] DECIMAL(18,2) NOT NULL,
        [NetInterest] DECIMAL(18,2) NOT NULL,
        [TotalNet] DECIMAL(18,2) NOT NULL,
        [MonthProfit] DECIMAL(18,2) NOT NULL,
        [IsNeutral] BIT NOT NULL,
        [JournalId] BIGINT NULL,
        CONSTRAINT [PK_DepositInterestRunItems] PRIMARY KEY CLUSTERED ([RunItemId] ASC),
        CONSTRAINT [FK_DepositInterestRunItems_Run] FOREIGN KEY ([RunId]) REFERENCES [dbo].[DepositInterestRuns]([RunId]) ON DELETE CASCADE
    );
    PRINT N'جدول DepositInterestRunItems ایجاد شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[DepositInterestRunItems]') AND name = N'UX_DepositInterestRunItems_Run_Member')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UX_DepositInterestRunItems_Run_Member] ON [dbo].[DepositInterestRunItems]([RunId],[MemberId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[DepositInterestRunItems]') AND name = N'IX_DepositInterestRunItems_JournalId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DepositInterestRunItems_JournalId] ON [dbo].[DepositInterestRunItems]([JournalId]);
END
GO

    IF OBJECT_ID(N'[dbo].[JournalLines]', N'U') IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'dbo.Journals', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalLines_Journal')
        BEGIN
            ALTER TABLE [dbo].[JournalLines]
            ADD CONSTRAINT [FK_JournalLines_Journal] FOREIGN KEY ([JournalId]) REFERENCES [dbo].[Journals]([JournalId]) ON DELETE CASCADE;
        END
        IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalLines_Account')
        BEGIN
            ALTER TABLE [dbo].[JournalLines]
            ADD CONSTRAINT [FK_JournalLines_Account] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts]([AccountId]);
        END
        IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalLines_Member')
        BEGIN
            ALTER TABLE [dbo].[JournalLines]
            ADD CONSTRAINT [FK_JournalLines_Member] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_JournalLines_Positive')
        BEGIN
            ALTER TABLE [dbo].[JournalLines]
            ADD CONSTRAINT [CK_JournalLines_Positive] CHECK (Debit >= 0 AND Credit >= 0);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_JournalLines_OneSide')
        BEGIN
            ALTER TABLE [dbo].[JournalLines]
            ADD CONSTRAINT [CK_JournalLines_OneSide] CHECK (NOT (Debit > 0 AND Credit > 0));
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[JournalLines]') AND name = N'IX_JournalLines_Account')
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_JournalLines_Account] ON [dbo].[JournalLines]([AccountId]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[JournalLines]') AND name = N'IX_JournalLines_Member')
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_JournalLines_Member] ON [dbo].[JournalLines]([MemberId]);
        END
    END
    GO

    -- Balance enforcement trigger
    IF OBJECT_ID(N'[dbo].[TR_Journals_EnforceBalance]', N'TR') IS NOT NULL
        DROP TRIGGER [dbo].[TR_Journals_EnforceBalance];
    GO
    EXEC(N'
    CREATE TRIGGER [dbo].[TR_Journals_EnforceBalance] ON [dbo].[JournalLines]
    AFTER INSERT, UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        -- Prevent modifications to Final (Status=3) journals
        IF EXISTS (
            SELECT 1
            FROM dbo.Journals j
            WHERE j.Status = 3
            AND j.JournalId IN (
                SELECT DISTINCT JournalId FROM (
                    SELECT JournalId FROM inserted
                    UNION ALL
                    SELECT JournalId FROM deleted
                ) x WHERE JournalId IS NOT NULL
            )
        )
        BEGIN
            RAISERROR(''اسناد قطعی قابل ویرایش یا حذف نیستند.'', 16, 1);
            ROLLBACK;
            RETURN;
        END
        -- Enforce balance for Permanent (Status>=2) journals only
        ;WITH impacted AS (
            SELECT DISTINCT JournalId FROM (
                SELECT JournalId FROM inserted
                UNION ALL
                SELECT JournalId FROM deleted
            ) x WHERE JournalId IS NOT NULL
        )
        SELECT 1/0
        FROM impacted i
        INNER JOIN dbo.Journals j ON j.JournalId = i.JournalId
        CROSS APPLY (
            SELECT SUM(Debit) AS SumDebit, SUM(Credit) AS SumCredit
            FROM dbo.JournalLines jl WHERE jl.JournalId = i.JournalId
        ) s
        WHERE j.Status >= 2
        AND ISNULL(s.SumDebit, 0) <> ISNULL(s.SumCredit, 0);
    END');
    GO

    -- 4) Posting Templates
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplates]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[PostingTemplates] (
            [PostingTemplateId] INT IDENTITY(1,1) NOT NULL,
            [TemplateName]      NVARCHAR(200) NOT NULL,
            [IsActive]          BIT NOT NULL CONSTRAINT DF_PostingTemplates_IsActive DEFAULT(1),
            CONSTRAINT [PK_PostingTemplates] PRIMARY KEY CLUSTERED ([PostingTemplateId] ASC)
        );
    END
    GO

    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplateLines]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[PostingTemplateLines] (
            [PostingTemplateLineId] INT IDENTITY(1,1) NOT NULL,
            [PostingTemplateId]     INT NOT NULL,
            [LineNo]                INT NOT NULL,
            [AccountId]             BIGINT NOT NULL,
            [Side]                  TINYINT NOT NULL,  -- 1=Debit, 2=Credit
            [AmountSource]          TINYINT NOT NULL,  -- 1=Total, 2=Principal, 3=Interest, 4=Fee, 5=Tax
            [FixedAmount]           DECIMAL(18,2) NULL,
            [AllowMemberDimension]  BIT NOT NULL CONSTRAINT DF_PTL_AllowMemberDimension DEFAULT(0),
            CONSTRAINT [PK_PostingTemplateLines] PRIMARY KEY CLUSTERED ([PostingTemplateLineId] ASC)
        );
    END
    GO

    IF OBJECT_ID(N'[dbo].[PostingTemplateLines]', N'U') IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'dbo.PostingTemplates', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PTL_Template')
        BEGIN
            ALTER TABLE [dbo].[PostingTemplateLines]
            ADD CONSTRAINT [FK_PTL_Template] FOREIGN KEY ([PostingTemplateId]) REFERENCES [dbo].[PostingTemplates]([PostingTemplateId]) ON DELETE CASCADE;
        END
        IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PTL_Account')
        BEGIN
            ALTER TABLE [dbo].[PostingTemplateLines]
            ADD CONSTRAINT [FK_PTL_Account] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts]([AccountId]);
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplateLines]') AND name = N'IX_PTL_Template')
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_PTL_Template] ON [dbo].[PostingTemplateLines]([PostingTemplateId], [LineNo]);
        END
        
        -- Add FixedAmount column if it doesn't exist
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplateLines]') AND name = N'FixedAmount')
        BEGIN
            ALTER TABLE [dbo].[PostingTemplateLines]
            ADD [FixedAmount] DECIMAL(18,2) NULL;
        END
        
        -- Add OperationalAccountId column if it doesn't exist
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplateLines]') AND name = N'OperationalAccountId')
        BEGIN
            ALTER TABLE [dbo].[PostingTemplateLines]
            ADD [OperationalAccountId] BIGINT NULL;
            PRINT N'ستون OperationalAccountId به جدول PostingTemplateLines اضافه شد';
        END
    END
    GO

    IF OBJECT_ID(N'dbo.TransactionTypes', N'U') IS NOT NULL
       AND COL_LENGTH('dbo.TransactionTypes', 'PostingTemplateId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TransactionTypes_PostingTemplates')
    BEGIN
        ALTER TABLE [dbo].[TransactionTypes]
        ADD CONSTRAINT [FK_TransactionTypes_PostingTemplates]
        FOREIGN KEY ([PostingTemplateId]) REFERENCES [dbo].[PostingTemplates]([PostingTemplateId]);
    END
    GO

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

    GO

    -- Add Level column if it doesn't exist
    IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    BEGIN
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
                (Level = 4) OR -- Level 4 can have optional parent
                (Level IS NULL)
            );
            PRINT N'محدودیت سلسله‌مراتب اضافه شد (شامل تفضیلی)';
        END

        -- Remove code pattern constraint entirely so app does NOT enforce any formatting
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_CodePattern')
        BEGIN
            ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_CodePattern];
            PRINT N'محدودیت الگوی کد حذف شد تا هر فرمتی مجاز باشد';
        END
    END
    GO

    -- Ensure AccountTypes table exists (Account Type Management)
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AccountTypes]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[AccountTypes] (
            [AccountTypeId] INT IDENTITY(1,1) NOT NULL,
            [Code] NVARCHAR(10) NOT NULL,
            [Name] NVARCHAR(100) NOT NULL,
            [Description] NVARCHAR(500) NULL,
            [IsActive] BIT NOT NULL CONSTRAINT DF_AccountTypes_IsActive DEFAULT(1),
            [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_AccountTypes_CreatedAt DEFAULT(GETUTCDATE()),
            CONSTRAINT [PK_AccountTypes] PRIMARY KEY CLUSTERED ([AccountTypeId] ASC),
            CONSTRAINT [UK_AccountTypes_Code] UNIQUE NONCLUSTERED ([Code] ASC)
        );
        PRINT N'جدول AccountTypes ایجاد شد';
    END

    -- Seed AccountTypes with default values
    IF OBJECT_ID(N'dbo.AccountTypes', N'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.AccountTypes WHERE Code = N'1') INSERT INTO dbo.AccountTypes (Code, Name, Description) VALUES (N'1', N'دارایی', N'Assets - دارایی‌های شرکت شامل نقد، حساب‌های دریافتنی و سایر دارایی‌ها');
        IF NOT EXISTS (SELECT 1 FROM dbo.AccountTypes WHERE Code = N'2') INSERT INTO dbo.AccountTypes (Code, Name, Description) VALUES (N'2', N'بدهی', N'Liabilities - بدهی‌های شرکت شامل حساب‌های پرداختنی و سایر تعهدات');
        IF NOT EXISTS (SELECT 1 FROM dbo.AccountTypes WHERE Code = N'3') INSERT INTO dbo.AccountTypes (Code, Name, Description) VALUES (N'3', N'حقوق صاحبان', N'Equity - حقوق صاحبان سهام و سرمایه شرکت');
        IF NOT EXISTS (SELECT 1 FROM dbo.AccountTypes WHERE Code = N'4') INSERT INTO dbo.AccountTypes (Code, Name, Description) VALUES (N'4', N'درآمد', N'Income - درآمدهای شرکت از فروش کالا و خدمات');
        IF NOT EXISTS (SELECT 1 FROM dbo.AccountTypes WHERE Code = N'5') INSERT INTO dbo.AccountTypes (Code, Name, Description) VALUES (N'5', N'هزینه', N'Expense - هزینه‌های عملیاتی و غیرعملیاتی شرکت');
    END

    -- Ensure PostingTemplates and PostingTemplateLines tables exist (Accounting Posting Templates)
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplates]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[PostingTemplates] (
            [PostingTemplateId] INT IDENTITY(1,1) NOT NULL,
            [TemplateName] NVARCHAR(200) NOT NULL,
            [IsActive] BIT NOT NULL CONSTRAINT DF_PostingTemplates_IsActive DEFAULT(1),
            [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_PostingTemplates_CreatedAt DEFAULT(GETUTCDATE()),
            [UpdatedAt] DATETIME2(7) NULL,
            CONSTRAINT [PK_PostingTemplates] PRIMARY KEY CLUSTERED ([PostingTemplateId] ASC),
            CONSTRAINT [UK_PostingTemplates_TemplateName] UNIQUE NONCLUSTERED ([TemplateName] ASC)
        );
        PRINT N'جدول PostingTemplates ایجاد شد';
    END

    -- Migration: Add CategoryCode for template categorization (idempotent)
    IF COL_LENGTH('dbo.PostingTemplates', 'CategoryCode') IS NULL
    BEGIN
        ALTER TABLE [dbo].[PostingTemplates] ADD [CategoryCode] TINYINT NULL;
        PRINT N'ستون CategoryCode به جدول PostingTemplates اضافه شد';
    END

    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PostingTemplateLines]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[PostingTemplateLines] (
            [PostingTemplateLineId] INT IDENTITY(1,1) NOT NULL,
            [PostingTemplateId] INT NOT NULL,
            [LineNo] INT NOT NULL,
            [AccountId] BIGINT NOT NULL,
            [Side] TINYINT NOT NULL, -- 1=Debit, 2=Credit
            [AmountSource] TINYINT NOT NULL, -- 1=Total, 2=Principal, 3=Interest, 4=Fee, 5=Tax
            [AllowMemberDimension] BIT NOT NULL CONSTRAINT DF_PTL_AllowMemberDimension DEFAULT(0),
            CONSTRAINT [PK_PostingTemplateLines] PRIMARY KEY CLUSTERED ([PostingTemplateLineId] ASC),
            CONSTRAINT [FK_PTL_Template] FOREIGN KEY ([PostingTemplateId]) REFERENCES [dbo].[PostingTemplates] ([PostingTemplateId]) ON DELETE CASCADE
        );
        PRINT N'جدول PostingTemplateLines ایجاد شد';
    END

    IF OBJECT_ID(N'dbo.PostingTemplates', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PostingTemplateLines', N'U') IS NOT NULL
    BEGIN
        DECLARE @ptId_Deposit INT, @ptId_Installment INT, @ptId_Penalty INT;

        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT')
            INSERT INTO dbo.PostingTemplates (TemplateName) VALUES (N'MEMBER_DEPOSIT');
        SET @ptId_Deposit = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT');
        -- Note: PostingTemplateLines will be created later after proper accounts exist
        -- Templates are created but lines will be added manually through UI
    END
    GO

    -- Auto-map by TypeCode if present (simplified for new hierarchy)
    IF OBJECT_ID(N'dbo.TransactionTypes', N'U') IS NOT NULL AND COL_LENGTH('dbo.TransactionTypes', 'TypeCode') IS NOT NULL
    BEGIN
        UPDATE tt SET PostingTemplateId = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT')
        FROM dbo.TransactionTypes tt
        WHERE tt.TypeCode = N'MEMBER_DEPOSIT' AND (tt.PostingTemplateId IS NULL OR tt.PostingTemplateId <> (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT'));
    END
    GO

    -- Views
    IF OBJECT_ID(N'dbo.vw_ApprovedJournals', N'V') IS NOT NULL DROP VIEW dbo.vw_ApprovedJournals;
    GO
    CREATE VIEW dbo.vw_ApprovedJournals AS
    SELECT JournalId, TransactionId, OccurredAt, CreatedAt, CreatedBy, Notes, Status, IsDeleted, SerialNumber, FiscalYearId, DocumentNumber
    FROM dbo.Journals
    WHERE Status >= 2 AND IsDeleted = 0;
    GO

    IF OBJECT_ID(N'dbo.vw_JournalLinesFlat', N'V') IS NOT NULL DROP VIEW dbo.vw_JournalLinesFlat;
    GO
    CREATE VIEW dbo.vw_JournalLinesFlat AS
    SELECT jl.JournalLineId, jl.JournalId, j.OccurredAt, jl.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType, jl.MemberId, jl.Debit, jl.Credit
    FROM dbo.JournalLines jl
    JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
    JOIN dbo.Accounts a ON a.AccountId = jl.AccountId;
    GO

    IF OBJECT_ID(N'dbo.vw_TrialBalance', N'V') IS NOT NULL DROP VIEW dbo.vw_TrialBalance;
    GO
    CREATE VIEW dbo.vw_TrialBalance AS
    SELECT a.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType,
           SUM(jl.Debit) AS TotalDebit, SUM(jl.Credit) AS TotalCredit, SUM(jl.Debit - jl.Credit) AS Net
    FROM dbo.JournalLines jl
    JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
    JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
    GROUP BY a.AccountId, a.Code, a.Name, a.AccountType;
    GO

    IF OBJECT_ID(N'dbo.vw_MemberDepositBalances', N'V') IS NOT NULL DROP VIEW dbo.vw_MemberDepositBalances;
    GO
    CREATE VIEW dbo.vw_MemberDepositBalances AS
    SELECT jl.MemberId, SUM(CASE WHEN a.Code = N'2000' THEN (jl.Credit - jl.Debit) ELSE 0 END) AS DepositLiability
    FROM dbo.JournalLines jl
    JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
    JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
    WHERE a.Code = N'2000' AND jl.MemberId IS NOT NULL
    GROUP BY jl.MemberId;
    GO

    IF OBJECT_ID(N'dbo.sp_BalanceSheet_AsOf', N'P') IS NULL EXEC('CREATE PROCEDURE dbo.sp_BalanceSheet_AsOf AS BEGIN SET NOCOUNT ON; END');
    GO
    ALTER PROCEDURE dbo.sp_BalanceSheet_AsOf @AsOf DATETIME2
    AS
    BEGIN
        SET NOCOUNT ON;
        ;WITH sums AS (
            SELECT a.AccountType, SUM(jl.Debit) AS Debit, SUM(jl.Credit) AS Credit, SUM(jl.Debit - jl.Credit) AS Net
            FROM dbo.JournalLines jl
            JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
            JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
            WHERE j.OccurredAt <= @AsOf
            GROUP BY a.AccountType
        )
        SELECT ISNULL((SELECT Net FROM sums WHERE AccountType=1),0) AS Assets,
               ISNULL((SELECT -Net FROM sums WHERE AccountType=2),0) AS Liabilities,
               ISNULL((SELECT -Net FROM sums WHERE AccountType=3),0) AS Equity,
               ISNULL((SELECT Net FROM sums WHERE AccountType=4),0) AS Income,
               ISNULL((SELECT -Net FROM sums WHERE AccountType=5),0) AS Expense;
END
GO

-- Migration: Ensure JournalLines.LoanId exists (idempotent)
IF OBJECT_ID(N'dbo.JournalLines', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.JournalLines', 'LoanId') IS NULL
    BEGIN
        ALTER TABLE [dbo].[JournalLines] ADD [LoanId] BIGINT NULL;
        PRINT N'ستون JournalLines.LoanId اضافه شد';
    END

    IF COL_LENGTH('dbo.JournalLines', 'LoanId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[JournalLines]') AND name = N'IX_JournalLines_LoanId')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_JournalLines_LoanId] ON [dbo].[JournalLines]([LoanId]);
        PRINT N'ایندکس IX_JournalLines_LoanId ایجاد شد';
    END
END
GO

-- ==================================================
    -- TransactionAudits: processing/audit log per transaction
    -- ==================================================
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TransactionAudits]') AND type = N'U')
    BEGIN
        CREATE TABLE [dbo].[TransactionAudits] (
            [AuditId]       BIGINT IDENTITY(1,1) NOT NULL,
            [TransactionId] BIGINT NOT NULL,
            [Stage]         NVARCHAR(64) NOT NULL,   -- e.g., Created, PostingStarted, PostingCompleted, Error
            [Message]       NVARCHAR(500) NULL,
            [CreatedAt]     DATETIME2(7) NOT NULL CONSTRAINT DF_TransactionAudits_CreatedAt DEFAULT(GETUTCDATE()),
            [CreatedBy]     NVARCHAR(64) NULL,
            CONSTRAINT [PK_TransactionAudits] PRIMARY KEY CLUSTERED ([AuditId] ASC)
        );
    END
    GO

    IF OBJECT_ID(N'dbo.TransactionAudits', N'U') IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TransactionAudits_Transactions')
        BEGIN
            ALTER TABLE [dbo].[TransactionAudits]
            ADD CONSTRAINT [FK_TransactionAudits_Transactions]
            FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions]([TransactionId]) ON DELETE CASCADE;
        END
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[TransactionAudits]') AND name = N'IX_TA_Tx_CreatedAt')
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_TA_Tx_CreatedAt]
            ON [dbo].[TransactionAudits] ([TransactionId] ASC, [CreatedAt] ASC)
            INCLUDE ([Stage]);
        END
    END
    GO

-- ==================================================
-- Additional Migrations: Per-Row Template Selection for Receipts (Idempotent)
-- ==================================================
IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'PostingTemplateId') IS NULL
BEGIN
    ALTER TABLE [dbo].[HouseholdReceiptAllocations] ADD [PostingTemplateId] INT NULL;
    PRINT N'ستون PostingTemplateId به جدول HouseholdReceiptAllocations اضافه شد';
END

IF COL_LENGTH('dbo.Transactions', 'PostingTemplateId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Transactions] ADD [PostingTemplateId] INT NULL;
    PRINT N'ستون PostingTemplateId به جدول Transactions اضافه شد';
END
GO

-- ==================================================
-- Loans: Loan Rates + Contracts + Installments + Payments (Idempotent)
-- ==================================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LoanRatePlans]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[LoanRatePlans] (
        [PlanId] BIGINT IDENTITY(1,1) NOT NULL,
        [EffectiveFrom] DATE NOT NULL,
        [EffectiveTo] DATE NULL,
        [AnnualRatePercent] DECIMAL(9,4) NOT NULL,
        [PenaltyAnnualRatePercent] DECIMAL(9,4) NOT NULL,
        [Notes] NVARCHAR(500) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_LoanRatePlans_IsActive DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_LoanRatePlans_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_LoanRatePlans] PRIMARY KEY CLUSTERED ([PlanId] ASC)
    );
    PRINT N'جدول LoanRatePlans ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.LoanRatePlans', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanRatePlans]') AND name = N'IX_LoanRatePlans_EffectiveFrom')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_LoanRatePlans_EffectiveFrom]
        ON [dbo].[LoanRatePlans]([EffectiveFrom] DESC, [PlanId] DESC)
        INCLUDE ([IsActive], [AnnualRatePercent], [PenaltyAnnualRatePercent], [EffectiveTo]);
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Loans]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Loans] (
        [LoanId] BIGINT IDENTITY(1,1) NOT NULL,
        [MemberId] BIGINT NOT NULL,
        [LoanRatePlanId] BIGINT NOT NULL,
        [Principal] DECIMAL(18,2) NOT NULL,
        [TermMonths] INT NOT NULL,
        [DisbursedAt] DATE NOT NULL,
        [FirstDueDate] DATE NOT NULL,
        [AnnualRatePercent] DECIMAL(9,4) NOT NULL,
        [PenaltyAnnualRatePercent] DECIMAL(9,4) NOT NULL,
        [EmiAmount] DECIMAL(18,2) NOT NULL,
        [OutstandingPrincipal] DECIMAL(18,2) NOT NULL,
        [Status] TINYINT NOT NULL, -- 1=Draft,2=Approved,3=Disbursed,4=Active,5=Settled,6=Cancelled
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_Loans_CreatedAt DEFAULT(GETUTCDATE()),
        [UpdatedAt] DATETIME2(7) NULL,
        CONSTRAINT [PK_Loans] PRIMARY KEY CLUSTERED ([LoanId] ASC)
    );
    PRINT N'جدول Loans ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Loans', 'MemberCode') IS NULL
    BEGIN
        ALTER TABLE [dbo].[Loans] ADD [MemberCode] NVARCHAR(32) NULL;
        PRINT N'ستون MemberCode به جدول Loans اضافه شد';
    END

    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Loans_Members')
    BEGIN
        ALTER TABLE [dbo].[Loans]
        ADD CONSTRAINT [FK_Loans_Members] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]);
    END

    IF OBJECT_ID(N'dbo.LoanRatePlans', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Loans_LoanRatePlans')
    BEGIN
        ALTER TABLE [dbo].[Loans]
        ADD CONSTRAINT [FK_Loans_LoanRatePlans] FOREIGN KEY ([LoanRatePlanId]) REFERENCES [dbo].[LoanRatePlans]([PlanId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Loans]') AND name = N'IX_Loans_Member_Status')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_Loans_Member_Status]
        ON [dbo].[Loans]([MemberId] ASC, [Status] ASC, [LoanId] DESC)
        INCLUDE ([DisbursedAt], [Principal], [OutstandingPrincipal]);
    END
END
GO

IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL AND COL_LENGTH('dbo.Loans', 'MemberCode') IS NOT NULL
BEGIN
    UPDATE l
    SET l.MemberCode = sub.Code
    FROM dbo.Loans l
    OUTER APPLY (
        SELECT TOP 1 a.Code
        FROM dbo.Accounts a
        WHERE a.MemberId = l.MemberId
          AND a.IsActive = 1
          AND a.Code LIKE N'M-%'
        ORDER BY a.AccountId DESC
    ) sub
    WHERE (l.MemberCode IS NULL OR LTRIM(RTRIM(l.MemberCode)) = N'')
      AND sub.Code IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.Loans', 'MemberCode') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Loans]') AND name = N'IX_Loans_MemberCode_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Loans_MemberCode_Status]
    ON [dbo].[Loans]([MemberCode] ASC, [Status] ASC, [LoanId] DESC)
    INCLUDE ([DisbursedAt], [Principal], [OutstandingPrincipal]);
END
GO

IF OBJECT_ID(N'dbo.CreditHolds', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CreditHolds_Loans')
    BEGIN
        ALTER TABLE [dbo].[CreditHolds]
        ADD CONSTRAINT [FK_CreditHolds_Loans] FOREIGN KEY ([LoanId]) REFERENCES [dbo].[Loans]([LoanId]) ON DELETE CASCADE;
    END

    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CreditHolds_Members')
    BEGIN
        ALTER TABLE [dbo].[CreditHolds]
        ADD CONSTRAINT [FK_CreditHolds_Members] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[CreditHolds]') AND name = N'IX_CreditHolds_MemberCode_Status')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_CreditHolds_MemberCode_Status]
        ON [dbo].[CreditHolds]([MemberCode] ASC, [Status] ASC, [HoldId] DESC)
        INCLUDE ([LoanId], [Role], [LockPercent], [LockedAmount]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[CreditHolds]') AND name = N'IX_CreditHolds_Loan_Status')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_CreditHolds_Loan_Status]
        ON [dbo].[CreditHolds]([LoanId] ASC, [Status] ASC, [HoldId] DESC)
        INCLUDE ([MemberId], [MemberCode], [Role], [LockedAmount]);
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LoanInstallments]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[LoanInstallments] (
        [InstallmentId] BIGINT IDENTITY(1,1) NOT NULL,
        [LoanId] BIGINT NOT NULL,
        [InstallmentNo] INT NOT NULL,
        [DueDate] DATE NOT NULL,
        [OpeningBalance] DECIMAL(18,2) NOT NULL,
        [EmiAmount] DECIMAL(18,2) NOT NULL,
        [InterestAmount] DECIMAL(18,2) NOT NULL,
        [PrincipalAmount] DECIMAL(18,2) NOT NULL,
        [ClosingBalance] DECIMAL(18,2) NOT NULL,
        [PaidPrincipal] DECIMAL(18,2) NOT NULL CONSTRAINT DF_LI_PaidPrincipal DEFAULT(0),
        [PaidInterest] DECIMAL(18,2) NOT NULL CONSTRAINT DF_LI_PaidInterest DEFAULT(0),
        [PaidPenalty] DECIMAL(18,2) NOT NULL CONSTRAINT DF_LI_PaidPenalty DEFAULT(0),
        [Status] TINYINT NOT NULL, -- 1=Unpaid,2=PartiallyPaid,3=Paid
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_LI_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_LoanInstallments] PRIMARY KEY CLUSTERED ([InstallmentId] ASC)
    );
    PRINT N'جدول LoanInstallments ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.LoanInstallments', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LoanInstallments_Loans')
    BEGIN
        ALTER TABLE [dbo].[LoanInstallments]
        ADD CONSTRAINT [FK_LoanInstallments_Loans] FOREIGN KEY ([LoanId]) REFERENCES [dbo].[Loans]([LoanId]) ON DELETE CASCADE;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanInstallments]') AND name = N'IX_LI_Loan_Due')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_LI_Loan_Due]
        ON [dbo].[LoanInstallments]([LoanId] ASC, [DueDate] ASC)
        INCLUDE ([Status], [EmiAmount], [PaidPrincipal], [PaidInterest], [PaidPenalty]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanInstallments]') AND name = N'UX_LI_Loan_No')
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX [UX_LI_Loan_No]
        ON [dbo].[LoanInstallments]([LoanId] ASC, [InstallmentNo] ASC);
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LoanPayments]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[LoanPayments] (
        [PaymentId] BIGINT IDENTITY(1,1) NOT NULL,
        [LoanId] BIGINT NOT NULL,
        [MemberId] BIGINT NOT NULL,
        [PaidAt] DATETIME2(7) NOT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [IsApproved] BIT NOT NULL CONSTRAINT DF_LP_IsApproved DEFAULT(0),
        [ApprovedAt] DATETIME2(7) NULL,
        [ApprovedBy] NVARCHAR(64) NULL,
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_LP_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_LoanPayments] PRIMARY KEY CLUSTERED ([PaymentId] ASC)
    );
    PRINT N'جدول LoanPayments ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.LoanPayments', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LoanPayments_Loans')
    BEGIN
        ALTER TABLE [dbo].[LoanPayments]
        ADD CONSTRAINT [FK_LoanPayments_Loans] FOREIGN KEY ([LoanId]) REFERENCES [dbo].[Loans]([LoanId]) ON DELETE CASCADE;
    END

    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LoanPayments_Members')
    BEGIN
        ALTER TABLE [dbo].[LoanPayments]
        ADD CONSTRAINT [FK_LoanPayments_Members] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanPayments]') AND name = N'IX_LP_Loan_PaidAt')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_LP_Loan_PaidAt]
        ON [dbo].[LoanPayments]([LoanId] ASC, [PaidAt] DESC, [PaymentId] DESC)
        INCLUDE ([Amount], [IsApproved]);
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LoanPaymentAllocations]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[LoanPaymentAllocations] (
        [AllocationId] BIGINT IDENTITY(1,1) NOT NULL,
        [PaymentId] BIGINT NOT NULL,
        [InstallmentId] BIGINT NULL,
        [Type] TINYINT NOT NULL, -- 1=Penalty,2=Interest,3=Principal,4=Prepayment
        [Amount] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_LPA_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_LoanPaymentAllocations] PRIMARY KEY CLUSTERED ([AllocationId] ASC)
    );
    PRINT N'جدول LoanPaymentAllocations ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.LoanPaymentAllocations', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.LoanPayments', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LPA_Payments')
    BEGIN
        ALTER TABLE [dbo].[LoanPaymentAllocations]
        ADD CONSTRAINT [FK_LPA_Payments] FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[LoanPayments]([PaymentId]) ON DELETE CASCADE;
    END

    IF OBJECT_ID(N'dbo.LoanInstallments', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LPA_Installments')
    BEGIN
        ALTER TABLE [dbo].[LoanPaymentAllocations]
        ADD CONSTRAINT [FK_LPA_Installments] FOREIGN KEY ([InstallmentId]) REFERENCES [dbo].[LoanInstallments]([InstallmentId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanPaymentAllocations]') AND name = N'IX_LPA_Payment')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_LPA_Payment]
        ON [dbo].[LoanPaymentAllocations]([PaymentId] ASC)
        INCLUDE ([Type], [Amount], [InstallmentId]);
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LoanGuarantees]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[LoanGuarantees] (
        [GuaranteeId] BIGINT IDENTITY(1,1) NOT NULL,
        [LoanId] BIGINT NOT NULL,
        [Type] TINYINT NOT NULL, -- 1=GuarantorMember,2=DepositHold,3=Cheque,4=Promissory
        [GuarantorMemberId] BIGINT NULL,
        [DepositAccountId] BIGINT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_LG_IsActive DEFAULT(1),
        [Notes] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_LG_CreatedAt DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_LoanGuarantees] PRIMARY KEY CLUSTERED ([GuaranteeId] ASC)
    );
    PRINT N'جدول LoanGuarantees ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.LoanGuarantees', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.LoanGuarantees', 'GuarantorMemberCode') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LoanGuarantees] ADD [GuarantorMemberCode] NVARCHAR(32) NULL;
        PRINT N'ستون GuarantorMemberCode به جدول LoanGuarantees اضافه شد';
    END

    IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LoanGuarantees_Loans')
    BEGIN
        ALTER TABLE [dbo].[LoanGuarantees]
        ADD CONSTRAINT [FK_LoanGuarantees_Loans] FOREIGN KEY ([LoanId]) REFERENCES [dbo].[Loans]([LoanId]) ON DELETE CASCADE;
    END

    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LoanGuarantees_Guarantor')
    BEGIN
        ALTER TABLE [dbo].[LoanGuarantees]
        ADD CONSTRAINT [FK_LoanGuarantees_Guarantor] FOREIGN KEY ([GuarantorMemberId]) REFERENCES [dbo].[Members]([MemberId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanGuarantees]') AND name = N'IX_LG_Loan')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_LG_Loan]
        ON [dbo].[LoanGuarantees]([LoanId] ASC)
        INCLUDE ([Type], [Amount], [IsActive]);
    END
END
GO

IF OBJECT_ID(N'dbo.LoanGuarantees', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.LoanGuarantees', 'GuarantorMemberCode') IS NOT NULL
BEGIN
    UPDATE lg
    SET lg.GuarantorMemberCode = sub.Code
    FROM dbo.LoanGuarantees lg
    OUTER APPLY (
        SELECT TOP 1 a.Code
        FROM dbo.Accounts a
        WHERE a.MemberId = lg.GuarantorMemberId
          AND a.IsActive = 1
          AND a.Code LIKE N'M-%'
        ORDER BY a.AccountId DESC
    ) sub
    WHERE lg.GuarantorMemberId IS NOT NULL
      AND (lg.GuarantorMemberCode IS NULL OR LTRIM(RTRIM(lg.GuarantorMemberCode)) = N'')
      AND sub.Code IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.LoanGuarantees', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.LoanGuarantees', 'GuarantorMemberCode') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[LoanGuarantees]') AND name = N'IX_LG_GuarantorMemberCode')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LG_GuarantorMemberCode]
    ON [dbo].[LoanGuarantees]([GuarantorMemberCode] ASC)
    INCLUDE ([LoanId], [Type], [Amount], [IsActive]);
END
GO

-- ==================================================
-- Auth (Independent) - Users / Roles / Page Permissions
-- ==================================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuthRoles]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AuthRoles] (
        [RoleId] BIGINT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(64) NOT NULL,
        [IsSystem] BIT NOT NULL CONSTRAINT [DF_AuthRoles_IsSystem] DEFAULT(0),
        [IsActive] BIT NOT NULL CONSTRAINT [DF_AuthRoles_IsActive] DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuthRoles_CreatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_AuthRoles] PRIMARY KEY CLUSTERED ([RoleId] ASC),
        CONSTRAINT [UK_AuthRoles_Name] UNIQUE NONCLUSTERED ([Name] ASC)
    );
    PRINT N'جدول AuthRoles ایجاد شد';
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuthUsers]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AuthUsers] (
        [UserId] BIGINT IDENTITY(1,1) NOT NULL,
        [Username] NVARCHAR(64) NOT NULL,
        [PasswordEnc] NVARCHAR(512) NOT NULL,
        [PasswordViewEnc] NVARCHAR(512) NULL,
        [RoleId] BIGINT NOT NULL,
        [MemberId] BIGINT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_AuthUsers_IsActive] DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuthUsers_CreatedAt] DEFAULT(GETUTCDATE()),
        [LastLoginAt] DATETIME2(7) NULL,
        [LastSeenAt] DATETIME2(7) NULL,
        CONSTRAINT [PK_AuthUsers] PRIMARY KEY CLUSTERED ([UserId] ASC),
        CONSTRAINT [UK_AuthUsers_Username] UNIQUE NONCLUSTERED ([Username] ASC)
    );
    PRINT N'جدول AuthUsers ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.AuthUsers', N'U') IS NOT NULL AND COL_LENGTH('dbo.AuthUsers', 'PasswordViewEnc') IS NULL
BEGIN
    ALTER TABLE [dbo].[AuthUsers] ADD [PasswordViewEnc] NVARCHAR(512) NULL;
    PRINT N'ستون PasswordViewEnc به جدول AuthUsers اضافه شد';
END
GO

IF OBJECT_ID(N'dbo.AuthUsers', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.AuthRoles', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AuthUsers_Role')
    BEGIN
        ALTER TABLE [dbo].[AuthUsers]
        ADD CONSTRAINT [FK_AuthUsers_Role] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AuthRoles]([RoleId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AuthUsers]') AND name = N'IX_AuthUsers_Role')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_AuthUsers_Role]
        ON [dbo].[AuthUsers]([RoleId] ASC)
        INCLUDE ([IsActive], [MemberId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AuthUsers]') AND name = N'IX_AuthUsers_Member')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_AuthUsers_Member]
        ON [dbo].[AuthUsers]([MemberId] ASC)
        INCLUDE ([RoleId], [IsActive]);
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuthPages]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AuthPages] (
        [PageId] BIGINT IDENTITY(1,1) NOT NULL,
        [PageKey] NVARCHAR(128) NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [Path] NVARCHAR(200) NOT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_AuthPages_IsActive] DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuthPages_CreatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_AuthPages] PRIMARY KEY CLUSTERED ([PageId] ASC),
        CONSTRAINT [UK_AuthPages_PageKey] UNIQUE NONCLUSTERED ([PageKey] ASC)
    );
    PRINT N'جدول AuthPages ایجاد شد';
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuthRolePagePermissions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AuthRolePagePermissions] (
        [RoleId] BIGINT NOT NULL,
        [PageId] BIGINT NOT NULL,
        [Allowed] BIT NOT NULL CONSTRAINT [DF_AuthRolePagePermissions_Allowed] DEFAULT(0),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuthRolePagePermissions_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_AuthRolePagePermissions] PRIMARY KEY CLUSTERED ([RoleId] ASC, [PageId] ASC)
    );
    PRINT N'جدول AuthRolePagePermissions ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.AuthRolePagePermissions', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.AuthRoles', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ARPP_Role')
    BEGIN
        ALTER TABLE [dbo].[AuthRolePagePermissions]
        ADD CONSTRAINT [FK_ARPP_Role] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AuthRoles]([RoleId]) ON DELETE CASCADE;
    END

    IF OBJECT_ID(N'dbo.AuthPages', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ARPP_Page')
    BEGIN
        ALTER TABLE [dbo].[AuthRolePagePermissions]
        ADD CONSTRAINT [FK_ARPP_Page] FOREIGN KEY ([PageId]) REFERENCES [dbo].[AuthPages]([PageId]) ON DELETE CASCADE;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AuthRolePagePermissions]') AND name = N'IX_ARPP_Page')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_ARPP_Page]
        ON [dbo].[AuthRolePagePermissions]([PageId] ASC)
        INCLUDE ([Allowed], [UpdatedAt]);
    END
END
GO

-- Seed system roles (idempotent)
IF OBJECT_ID(N'dbo.AuthRoles', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.AuthRoles WHERE Name = N'Developer')
        INSERT INTO dbo.AuthRoles ([Name], [IsSystem], [IsActive]) VALUES (N'Developer', 1, 1);
    IF NOT EXISTS (SELECT 1 FROM dbo.AuthRoles WHERE Name = N'Admin')
        INSERT INTO dbo.AuthRoles ([Name], [IsSystem], [IsActive]) VALUES (N'Admin', 1, 1);
    IF NOT EXISTS (SELECT 1 FROM dbo.AuthRoles WHERE Name = N'Accountant')
        INSERT INTO dbo.AuthRoles ([Name], [IsSystem], [IsActive]) VALUES (N'Accountant', 1, 1);
    IF NOT EXISTS (SELECT 1 FROM dbo.AuthRoles WHERE Name = N'Member')
        INSERT INTO dbo.AuthRoles ([Name], [IsSystem], [IsActive]) VALUES (N'Member', 1, 1);
END
GO

-- ==================================================
-- پنل پیامک (SmsSettings / SmsFeatures / SmsTemplates)
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SmsSettings]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SmsSettings] (
        [SettingsId] INT NOT NULL,
        [IsEnabled] BIT NOT NULL CONSTRAINT [DF_SmsSettings_IsEnabled] DEFAULT(0),
        [ProviderName] NVARCHAR(50) NULL,
        [VerifyTemplateId] NVARCHAR(64) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsSettings_CreatedAt] DEFAULT(GETUTCDATE()),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsSettings_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_SmsSettings] PRIMARY KEY CLUSTERED ([SettingsId] ASC)
    );
    PRINT N'جدول SmsSettings ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.SmsSettings', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.SmsSettings', 'VerifyTemplateId') IS NULL
    BEGIN
        ALTER TABLE [dbo].[SmsSettings] ADD [VerifyTemplateId] NVARCHAR(64) NULL;
        PRINT N'ستون VerifyTemplateId به جدول SmsSettings اضافه شد';
    END

    IF COL_LENGTH('dbo.SmsSettings', 'CreditRial') IS NULL
    BEGIN
        ALTER TABLE [dbo].[SmsSettings] ADD [CreditRial] BIGINT NOT NULL CONSTRAINT [DF_SmsSettings_CreditRial] DEFAULT(0);
        PRINT N'ستون CreditRial به جدول SmsSettings اضافه شد';
    END

    IF COL_LENGTH('dbo.SmsSettings', 'PartCostRial') IS NULL
    BEGIN
        ALTER TABLE [dbo].[SmsSettings] ADD [PartCostRial] INT NOT NULL CONSTRAINT [DF_SmsSettings_PartCostRial] DEFAULT(0);
        PRINT N'ستون PartCostRial به جدول SmsSettings اضافه شد';
    END

    IF COL_LENGTH('dbo.SmsSettings', 'LowCreditThresholdRial') IS NULL
    BEGIN
        ALTER TABLE [dbo].[SmsSettings] ADD [LowCreditThresholdRial] BIGINT NOT NULL CONSTRAINT [DF_SmsSettings_LowCreditThresholdRial] DEFAULT(20000);
        PRINT N'ستون LowCreditThresholdRial به جدول SmsSettings اضافه شد';
    END
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SmsFeatures]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SmsFeatures] (
        [FeatureKey] NVARCHAR(64) NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [IsEnabled] BIT NOT NULL CONSTRAINT [DF_SmsFeatures_IsEnabled] DEFAULT(0),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsFeatures_CreatedAt] DEFAULT(GETUTCDATE()),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsFeatures_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_SmsFeatures] PRIMARY KEY CLUSTERED ([FeatureKey] ASC)
    );
    PRINT N'جدول SmsFeatures ایجاد شد';
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SmsTemplates]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SmsTemplates] (
        [TemplateKey] NVARCHAR(64) NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [Body] NVARCHAR(MAX) NOT NULL,
        [ProviderTemplateId] NVARCHAR(64) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_SmsTemplates_IsActive] DEFAULT(1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsTemplates_CreatedAt] DEFAULT(GETUTCDATE()),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsTemplates_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_SmsTemplates] PRIMARY KEY CLUSTERED ([TemplateKey] ASC)
    );
    PRINT N'جدول SmsTemplates ایجاد شد';
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SmsCreditTopups]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SmsCreditTopups] (
        [TopupId] BIGINT IDENTITY(1,1) NOT NULL,
        [AmountRial] BIGINT NOT NULL,
        [Notes] NVARCHAR(300) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsCreditTopups_CreatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_SmsCreditTopups] PRIMARY KEY CLUSTERED ([TopupId] ASC)
    );
    PRINT N'جدول SmsCreditTopups ایجاد شد';
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SmsMessageLogs]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SmsMessageLogs] (
        [LogId] BIGINT IDENTITY(1,1) NOT NULL,
        [FeatureKey] NVARCHAR(64) NULL,

        [HouseholdId] BIGINT NULL,
        [HeadMemberId] BIGINT NULL,
        [MemberIdsJson] NVARCHAR(MAX) NULL,

        [RecipientMobile] NVARCHAR(20) NULL,
        [TemplateId] INT NULL,
        [ParamsJson] NVARCHAR(MAX) NULL,
        [MessageText] NVARCHAR(MAX) NULL,

        [ProviderMessageId] BIGINT NULL,

        [ApiStatus] INT NULL,
        [ApiMessage] NVARCHAR(250) NULL,
        [DeliveryState] TINYINT NULL,
        [DeliveryDateTime] DATETIME2(7) NULL,

        [IsSuccess] BIT NULL,
        [ErrorMessage] NVARCHAR(500) NULL,
        [RawResponse] NVARCHAR(MAX) NULL,

        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SmsMessageLogs_CreatedAt] DEFAULT(GETUTCDATE()),
        [SentAt] DATETIME2(7) NULL,
        CONSTRAINT [PK_SmsMessageLogs] PRIMARY KEY CLUSTERED ([LogId] ASC)
    );
    PRINT N'جدول SmsMessageLogs ایجاد شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SmsMessageLogs]') AND name = N'IX_SmsMessageLogs_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SmsMessageLogs_CreatedAt] ON [dbo].[SmsMessageLogs] ([CreatedAt] DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SmsMessageLogs]') AND name = N'IX_SmsMessageLogs_RecipientMobile')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SmsMessageLogs_RecipientMobile] ON [dbo].[SmsMessageLogs] ([RecipientMobile] ASC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SmsMessageLogs]') AND name = N'IX_SmsMessageLogs_HeadMemberId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SmsMessageLogs_HeadMemberId] ON [dbo].[SmsMessageLogs] ([HeadMemberId] ASC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SmsCreditTopups]') AND name = N'IX_SmsCreditTopups_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SmsCreditTopups_CreatedAt] ON [dbo].[SmsCreditTopups] ([CreatedAt] DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SmsFeatures]') AND name = N'IX_SmsFeatures_IsEnabled')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SmsFeatures_IsEnabled] ON [dbo].[SmsFeatures] ([IsEnabled] ASC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SmsTemplates]') AND name = N'IX_SmsTemplates_IsActive')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SmsTemplates_IsActive] ON [dbo].[SmsTemplates] ([IsActive] ASC);
END
GO

-- Seed (idempotent)
IF OBJECT_ID(N'dbo.SmsSettings', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsSettings WHERE SettingsId = 1)
        INSERT INTO dbo.SmsSettings (SettingsId, IsEnabled, ProviderName, VerifyTemplateId, CreditRial, PartCostRial, LowCreditThresholdRial)
        VALUES (1, 0, NULL, NULL, 0, 0, 20000);
END
GO

IF OBJECT_ID(N'dbo.SmsFeatures', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsFeatures WHERE FeatureKey = N'deposit_credit')
        INSERT INTO dbo.SmsFeatures (FeatureKey, Title, IsEnabled) VALUES (N'deposit_credit', N'ارسال پیامک واریز سپرده', 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsFeatures WHERE FeatureKey = N'loan_installment_reminder')
        INSERT INTO dbo.SmsFeatures (FeatureKey, Title, IsEnabled) VALUES (N'loan_installment_reminder', N'ارسال پیامک یادآوری قسط وام', 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsFeatures WHERE FeatureKey = N'loan_payment')
        INSERT INTO dbo.SmsFeatures (FeatureKey, Title, IsEnabled) VALUES (N'loan_payment', N'ارسال پیامک ثبت پرداخت قسط', 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsFeatures WHERE FeatureKey = N'deposit_interest_run')
        INSERT INTO dbo.SmsFeatures (FeatureKey, Title, IsEnabled) VALUES (N'deposit_interest_run', N'ارسال پیامک محاسبه سود سپرده', 0);
END
GO

IF OBJECT_ID(N'dbo.SmsTemplates', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsTemplates WHERE TemplateKey = N'tx_deposit_credit')
        INSERT INTO dbo.SmsTemplates (TemplateKey, Title, Body, ProviderTemplateId, IsActive)
        VALUES (N'tx_deposit_credit', N'متن پیامک واریز سپرده', N'واریز سپرده شما ثبت شد. مبلغ: {PRICE} بابت: {BABAT} مانده: {MANPRICE}', NULL, 1);
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsTemplates WHERE TemplateKey = N'loan_installment_reminder')
        INSERT INTO dbo.SmsTemplates (TemplateKey, Title, Body, ProviderTemplateId, IsActive)
        VALUES (N'loan_installment_reminder', N'متن پیامک یادآوری اقساط', N'یادآوری اقساط: مهلت پرداخت قسط شما فرا رسیده است. مبلغ: {PRICE} مانده: {MANPRICE}', NULL, 1);
END
GO

-- ==================================================
-- Fiscal Year Tables
-- ==================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FiscalYears')
BEGIN
    CREATE TABLE dbo.FiscalYears (
        FiscalYearId     INT           IDENTITY(1,1) PRIMARY KEY,
        YearCode         INT           NOT NULL,
        Title            NVARCHAR(100) NOT NULL,
        StartDate        DATE          NOT NULL,
        EndDate          DATE          NOT NULL,
        IsOpen           BIT           NOT NULL DEFAULT 1,
        IsClosed         BIT           NOT NULL DEFAULT 0,
        ClosedAt         DATETIME2     NULL,
        ClosedBy         NVARCHAR(128) NULL,
        Notes            NVARCHAR(500) NULL,
        CreatedAt        DATETIME2     NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE UNIQUE INDEX IX_FiscalYears_YearCode ON dbo.FiscalYears(YearCode);
    PRINT 'FiscalYears table created';
END
ELSE
BEGIN
    PRINT 'FiscalYears table already exists';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FiscalYearBalances')
BEGIN
    CREATE TABLE dbo.FiscalYearBalances (
        BalanceId      BIGINT        IDENTITY(1,1) PRIMARY KEY,
        FiscalYearId   INT           NOT NULL REFERENCES dbo.FiscalYears(FiscalYearId),
        AccountId      BIGINT        NOT NULL REFERENCES dbo.Accounts(AccountId),
        OpeningDebit   DECIMAL(18,2) NOT NULL DEFAULT 0,
        OpeningCredit  DECIMAL(18,2) NOT NULL DEFAULT 0,
        CONSTRAINT UQ_FiscalYear_Account UNIQUE (FiscalYearId, AccountId)
    );

    CREATE INDEX IX_FiscalYearBalances_FiscalYearId ON dbo.FiscalYearBalances(FiscalYearId);
    CREATE INDEX IX_FiscalYearBalances_AccountId ON dbo.FiscalYearBalances(AccountId);
    PRINT 'FiscalYearBalances table created';
END
ELSE
BEGIN
    PRINT 'FiscalYearBalances table already exists';
END
GO

-- ==================================================
-- Bale (پیامرسان بله) + Unified Notifications
-- ==================================================
IF OBJECT_ID(N'dbo.BaleBotSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BaleBotSettings] (
        [SettingsId] INT NOT NULL,
        [IsEnabled] BIT NOT NULL CONSTRAINT [DF_BaleBotSettings_IsEnabled] DEFAULT(0),
        [BotToken] NVARCHAR(MAX) NULL,
        [BotUsername] NVARCHAR(100) NULL,
        [WebhookSecret] NVARCHAR(400) NULL,
        [ApiBaseUrl] NVARCHAR(200) NOT NULL CONSTRAINT [DF_BaleBotSettings_ApiBaseUrl] DEFAULT(N'https://tapi.bale.ai'),
        [RequireBaleConnectionForMembers] BIT NOT NULL CONSTRAINT [DF_BaleBotSettings_RequireBale] DEFAULT(0),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_BaleBotSettings_CreatedAt] DEFAULT(GETUTCDATE()),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_BaleBotSettings_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_BaleBotSettings] PRIMARY KEY CLUSTERED ([SettingsId] ASC)
    );
    PRINT N'جدول BaleBotSettings ایجاد شد';
END
GO

IF COL_LENGTH('dbo.BaleBotSettings', 'RequireBaleConnectionForMembers') IS NULL
BEGIN
    ALTER TABLE dbo.BaleBotSettings
    ADD [RequireBaleConnectionForMembers] BIT NOT NULL CONSTRAINT [DF_BaleBotSettings_RequireBale] DEFAULT(0);
    PRINT N'ستون RequireBaleConnectionForMembers به BaleBotSettings اضافه شد';
END
GO

IF OBJECT_ID(N'dbo.BaleSubscribers', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BaleSubscribers] (
        [SubscriberId] BIGINT IDENTITY(1,1) NOT NULL,
        [MemberId] BIGINT NOT NULL,
        [ChatId] BIGINT NOT NULL,
        [BaleUserId] BIGINT NULL,
        [Username] NVARCHAR(100) NULL,
        [FirstName] NVARCHAR(150) NULL,
        [LastName] NVARCHAR(150) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_BaleSubscribers_IsActive] DEFAULT(1),
        [LinkedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_BaleSubscribers_LinkedAt] DEFAULT(GETUTCDATE()),
        [LastSeenAt] DATETIME2(7) NULL,
        [OptedOutAt] DATETIME2(7) NULL,
        CONSTRAINT [PK_BaleSubscribers] PRIMARY KEY CLUSTERED ([SubscriberId] ASC)
    );
    PRINT N'جدول BaleSubscribers ایجاد شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleSubscribers]') AND name = N'UX_BaleSubscribers_ActiveMemberId')
BEGIN
    CREATE UNIQUE INDEX [UX_BaleSubscribers_ActiveMemberId] ON [dbo].[BaleSubscribers] ([MemberId] ASC) WHERE [IsActive] = 1;
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleSubscribers]') AND name = N'UX_BaleSubscribers_ActiveChatId')
BEGIN
    DROP INDEX [UX_BaleSubscribers_ActiveChatId] ON [dbo].[BaleSubscribers];
    PRINT N'ایندکس یکتای چت حذف شد تا یک شماره/چت بتواند چند عضو را پوشش دهد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleSubscribers]') AND name = N'IX_BaleSubscribers_ChatId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BaleSubscribers_ChatId] ON [dbo].[BaleSubscribers] ([ChatId] ASC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleSubscribers]') AND name = N'IX_BaleSubscribers_MemberId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BaleSubscribers_MemberId] ON [dbo].[BaleSubscribers] ([MemberId] ASC);
END
GO

IF OBJECT_ID(N'dbo.BaleLinkTokens', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BaleLinkTokens] (
        [TokenId] BIGINT IDENTITY(1,1) NOT NULL,
        [Token] NVARCHAR(64) NOT NULL,
        [MemberId] BIGINT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_BaleLinkTokens_CreatedAt] DEFAULT(GETUTCDATE()),
        [ExpiresAt] DATETIME2(7) NOT NULL,
        [UsedAt] DATETIME2(7) NULL,
        [UsedChatId] BIGINT NULL,
        CONSTRAINT [PK_BaleLinkTokens] PRIMARY KEY CLUSTERED ([TokenId] ASC)
    );
    PRINT N'جدول BaleLinkTokens ایجاد شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleLinkTokens]') AND name = N'UX_BaleLinkTokens_Token')
BEGIN
    CREATE UNIQUE INDEX [UX_BaleLinkTokens_Token] ON [dbo].[BaleLinkTokens] ([Token] ASC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleLinkTokens]') AND name = N'IX_BaleLinkTokens_MemberId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BaleLinkTokens_MemberId] ON [dbo].[BaleLinkTokens] ([MemberId] ASC, [UsedAt] ASC);
END
GO

IF OBJECT_ID(N'dbo.BaleMessageLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BaleMessageLogs] (
        [LogId] BIGINT IDENTITY(1,1) NOT NULL,
        [NotificationId] BIGINT NULL,
        [MemberId] BIGINT NULL,
        [ChatId] BIGINT NULL,
        [FeatureKey] NVARCHAR(64) NULL,
        [MessageText] NVARCHAR(MAX) NULL,
        [ProviderMessageId] BIGINT NULL,
        [ApiStatus] INT NULL,
        [ApiMessage] NVARCHAR(250) NULL,
        [IsSuccess] BIT NULL,
        [ErrorMessage] NVARCHAR(500) NULL,
        [RawResponse] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_BaleMessageLogs_CreatedAt] DEFAULT(GETUTCDATE()),
        [SentAt] DATETIME2(7) NULL,
        [ReadAt] DATETIME2(7) NULL,
        CONSTRAINT [PK_BaleMessageLogs] PRIMARY KEY CLUSTERED ([LogId] ASC)
    );
    PRINT N'جدول BaleMessageLogs ایجاد شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleMessageLogs]') AND name = N'IX_BaleMessageLogs_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BaleMessageLogs_CreatedAt] ON [dbo].[BaleMessageLogs] ([CreatedAt] DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleMessageLogs]') AND name = N'IX_BaleMessageLogs_MemberId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BaleMessageLogs_MemberId] ON [dbo].[BaleMessageLogs] ([MemberId] ASC);
END
GO

IF COL_LENGTH(N'dbo.BaleMessageLogs', N'ReadAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[BaleMessageLogs] ADD [ReadAt] DATETIME2(7) NULL;
    EXEC(N'UPDATE [dbo].[BaleMessageLogs] SET [ReadAt] = [CreatedAt] WHERE [ReadAt] IS NULL;');
    PRINT N'ستون ReadAt به BaleMessageLogs اضافه شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BaleMessageLogs]') AND name = N'IX_BaleMessageLogs_MemberId_ReadAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BaleMessageLogs_MemberId_ReadAt]
        ON [dbo].[BaleMessageLogs] ([MemberId] ASC, [ReadAt] ASC, [CreatedAt] DESC)
        INCLUDE ([IsSuccess]);
END
GO

IF OBJECT_ID(N'dbo.BaleUpdates', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BaleUpdates] (
        [UpdateId] BIGINT NOT NULL,
        [ChatId] BIGINT NULL,
        [MessageText] NVARCHAR(MAX) NULL,
        [ProcessedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_BaleUpdates_ProcessedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_BaleUpdates] PRIMARY KEY CLUSTERED ([UpdateId] ASC)
    );
    PRINT N'جدول BaleUpdates ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.NotificationFeatures', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NotificationFeatures] (
        [FeatureKey] NVARCHAR(64) NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [IsEnabled] BIT NOT NULL CONSTRAINT [DF_NotificationFeatures_IsEnabled] DEFAULT(0),
        [ChannelMask] TINYINT NOT NULL CONSTRAINT [DF_NotificationFeatures_ChannelMask] DEFAULT(1),
        [ApprovalMode] TINYINT NOT NULL CONSTRAINT [DF_NotificationFeatures_ApprovalMode] DEFAULT(0),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_NotificationFeatures_CreatedAt] DEFAULT(GETUTCDATE()),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_NotificationFeatures_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_NotificationFeatures] PRIMARY KEY CLUSTERED ([FeatureKey] ASC)
    );
    PRINT N'جدول NotificationFeatures ایجاد شد';
END
GO

IF OBJECT_ID(N'dbo.NotificationQueue', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NotificationQueue] (
        [NotificationId] BIGINT IDENTITY(1,1) NOT NULL,
        [EventKey] NVARCHAR(200) NULL,
        [FeatureKey] NVARCHAR(64) NOT NULL,
        [MemberId] BIGINT NULL,
        [HouseholdId] BIGINT NULL,
        [HeadMemberId] BIGINT NULL,
        [MemberIdsJson] NVARCHAR(MAX) NULL,
        [RecipientMobile] NVARCHAR(20) NULL,
        [RecipientName] NVARCHAR(200) NULL,
        [SmsTemplateId] INT NULL,
        [SmsParamsJson] NVARCHAR(MAX) NULL,
        [MessageText] NVARCHAR(MAX) NOT NULL,
        [BaleMessageText] NVARCHAR(MAX) NULL,
        [ChannelMask] TINYINT NOT NULL,
        [Status] TINYINT NOT NULL CONSTRAINT [DF_NotificationQueue_Status] DEFAULT(0),
        [CreatedBy] NVARCHAR(128) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_NotificationQueue_CreatedAt] DEFAULT(GETUTCDATE()),
        [ScheduledAt] DATETIME2(7) NULL,
        [ApprovedBy] NVARCHAR(128) NULL,
        [ApprovedAt] DATETIME2(7) NULL,
        [Attempts] INT NOT NULL CONSTRAINT [DF_NotificationQueue_Attempts] DEFAULT(0),
        [NextAttemptAt] DATETIME2(7) NULL,
        [LastError] NVARCHAR(500) NULL,
        [SmsLogId] BIGINT NULL,
        [SmsSentAt] DATETIME2(7) NULL,
        [BaleLogId] BIGINT NULL,
        [BaleSentAt] DATETIME2(7) NULL,
        [SentAt] DATETIME2(7) NULL,
        [CancelledAt] DATETIME2(7) NULL,
        [CancelledBy] NVARCHAR(128) NULL,
        [CancelReason] NVARCHAR(300) NULL,
        CONSTRAINT [PK_NotificationQueue] PRIMARY KEY CLUSTERED ([NotificationId] ASC)
    );
    PRINT N'جدول NotificationQueue ایجاد شد';
END
GO

IF COL_LENGTH(N'dbo.NotificationQueue', N'BaleMessageText') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationQueue] ADD [BaleMessageText] NVARCHAR(MAX) NULL;
    PRINT N'ستون BaleMessageText به جدول NotificationQueue اضافه شد';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[NotificationQueue]') AND name = N'UX_NotificationQueue_EventKey')
BEGIN
    CREATE UNIQUE INDEX [UX_NotificationQueue_EventKey] ON [dbo].[NotificationQueue] ([EventKey] ASC) WHERE [EventKey] IS NOT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[NotificationQueue]') AND name = N'IX_NotificationQueue_Status_NextAttemptAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_NotificationQueue_Status_NextAttemptAt] ON [dbo].[NotificationQueue] ([Status] ASC, [NextAttemptAt] ASC) INCLUDE ([ChannelMask], [Attempts]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[NotificationQueue]') AND name = N'IX_NotificationQueue_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_NotificationQueue_CreatedAt] ON [dbo].[NotificationQueue] ([CreatedAt] DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[NotificationQueue]') AND name = N'IX_NotificationQueue_MemberId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_NotificationQueue_MemberId] ON [dbo].[NotificationQueue] ([MemberId] ASC);
END
GO

IF OBJECT_ID(N'dbo.NotificationAutoSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NotificationAutoSettings] (
        [SettingsId] INT NOT NULL,
        [LoanReminderEnabled] BIT NOT NULL CONSTRAINT [DF_NotificationAutoSettings_LoanReminderEnabled] DEFAULT(0),
        [DaysBeforeDue] INT NOT NULL CONSTRAINT [DF_NotificationAutoSettings_DaysBeforeDue] DEFAULT(2),
        [IncludeOverdue] BIT NOT NULL CONSTRAINT [DF_NotificationAutoSettings_IncludeOverdue] DEFAULT(1),
        [RunHour] TINYINT NOT NULL CONSTRAINT [DF_NotificationAutoSettings_RunHour] DEFAULT(9),
        [LastRunDate] DATE NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_NotificationAutoSettings_UpdatedAt] DEFAULT(GETUTCDATE()),
        CONSTRAINT [PK_NotificationAutoSettings] PRIMARY KEY CLUSTERED ([SettingsId] ASC)
    );
    PRINT N'جدول NotificationAutoSettings ایجاد شد';
END
GO

-- Seeds (idempotent)
IF OBJECT_ID(N'dbo.BaleBotSettings', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.BaleBotSettings WHERE SettingsId = 1)
        INSERT INTO dbo.BaleBotSettings (SettingsId, IsEnabled, BotToken, BotUsername, WebhookSecret, ApiBaseUrl)
        VALUES (1, 0, NULL, NULL, NULL, N'https://tapi.bale.ai');
END
GO

IF OBJECT_ID(N'dbo.NotificationFeatures', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = N'deposit_credit')
        INSERT INTO dbo.NotificationFeatures (FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode)
        VALUES (N'deposit_credit', N'اطلاع‌رسانی واریز سپرده', 1, 1, 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = N'loan_payment')
        INSERT INTO dbo.NotificationFeatures (FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode)
        VALUES (N'loan_payment', N'اطلاع‌رسانی پرداخت قسط وام', 1, 1, 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = N'loan_installment_reminder')
        INSERT INTO dbo.NotificationFeatures (FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode)
        VALUES (N'loan_installment_reminder', N'یادآوری سررسید اقساط', 1, 1, 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = N'member_updated')
        INSERT INTO dbo.NotificationFeatures (FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode)
        VALUES (N'member_updated', N'اطلاع‌رسانی ویرایش اطلاعات عضو', 0, 2, 0);
    IF NOT EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = N'deposit_interest_run')
        INSERT INTO dbo.NotificationFeatures (FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode)
        VALUES (N'deposit_interest_run', N'اطلاع‌رسانی واریز سود سپرده', 1, 2, 0);
    ELSE
        UPDATE dbo.NotificationFeatures
        SET Title = N'اطلاع‌رسانی واریز سود سپرده',
            ChannelMask = 2,
            ApprovalMode = 0,
            UpdatedAt = GETUTCDATE()
        WHERE FeatureKey = N'deposit_interest_run';

    -- این کلید فعلاً نقطه فراخوانی ندارد و در تنظیمات نمایش داده نمی‌شود.
    IF EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = N'member_created')
       AND NOT EXISTS (SELECT 1 FROM dbo.NotificationQueue WHERE FeatureKey = N'member_created')
        DELETE FROM dbo.NotificationFeatures WHERE FeatureKey = N'member_created';
END
GO

IF OBJECT_ID(N'dbo.NotificationAutoSettings', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.NotificationAutoSettings WHERE SettingsId = 1)
        INSERT INTO dbo.NotificationAutoSettings (SettingsId, LoanReminderEnabled, DaysBeforeDue, IncludeOverdue, RunHour)
        VALUES (1, 0, 2, 1, 9);
END
GO
