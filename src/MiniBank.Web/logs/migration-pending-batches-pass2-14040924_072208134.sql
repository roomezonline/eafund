-- HouseholdReceiptAllocations migrations (idempotent)
IF OBJECT_ID(N'dbo.HouseholdReceiptAllocations', N'U') IS NOT NULL
BEGIN
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
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'PurposeType') IS NOT NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP COLUMN [PurposeType];
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'DepositTypeId') IS NOT NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP COLUMN [DepositTypeId];
    IF COL_LENGTH('dbo.HouseholdReceiptAllocations', 'PostingTemplateId') IS NOT NULL
        ALTER TABLE dbo.HouseholdReceiptAllocations DROP COLUMN [PostingTemplateId];

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