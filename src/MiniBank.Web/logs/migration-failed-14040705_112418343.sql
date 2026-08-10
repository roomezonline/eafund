-- ایندکس‌ها و محدودیت‌های پیشنهادی برای سرعت و یکتا بودن‌های اختیاری
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_BankName')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BankAccounts_BankName] ON [dbo].[BankAccounts] ([BankName] ASC)
    PRINT N'ایندکس IX_BankAccounts_BankName ایجاد شد'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_IsActive')
    CREATE NONCLUSTERED INDEX [IX_BankAccounts_IsActive] ON [dbo].[BankAccounts] ([IsActive] ASC)
    PRINT N'ایندکس IX_BankAccounts_IsActive ایجاد شد'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_BankAccounts_CreatedAt] ON [dbo].[BankAccounts] ([CreatedAt] DESC)
    PRINT N'ایندکس IX_BankAccounts_CreatedAt ایجاد شد'
END