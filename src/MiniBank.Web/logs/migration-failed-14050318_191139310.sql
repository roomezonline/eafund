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