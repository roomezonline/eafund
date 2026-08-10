IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[HouseholdReceiptAllocations]') AND name = N'IX_HRA_Member_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HRA_Member_Status]
    ON [dbo].[HouseholdReceiptAllocations] ([MemberId] ASC, [Status] ASC)
    INCLUDE ([Amount], [PurposeType]);
END