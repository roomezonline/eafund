-- ============================================================
-- V006: Fiscal Year Integration + Document Number per Fiscal Year
-- ============================================================
-- این اسکریپت در یک تراکنش اجرا می‌شود
-- ============================================================
BEGIN TRANSACTION;
BEGIN TRY
    PRINT '=== V006: شروع Migration ===';

    -- 1. افزودن ستون FiscalYearId (nullable در ابتدا)
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Journals') AND name = 'FiscalYearId')
    BEGIN
        ALTER TABLE dbo.Journals ADD FiscalYearId INT NULL;
        PRINT '  [OK] ستون FiscalYearId اضافه شد';
    END
    ELSE PRINT '  [SKIP] FiscalYearId از قبل وجود دارد';

    -- 2. افزودن ستون DocumentNumber
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Journals') AND name = 'DocumentNumber')
    BEGIN
        ALTER TABLE dbo.Journals ADD DocumentNumber INT NULL;
        PRINT '  [OK] ستون DocumentNumber اضافه شد';
    END
    ELSE PRINT '  [SKIP] DocumentNumber از قبل وجود دارد';

    -- 3. FK به FiscalYears
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Journals_FiscalYear')
    BEGIN
        ALTER TABLE dbo.Journals ADD CONSTRAINT FK_Journals_FiscalYear
            FOREIGN KEY (FiscalYearId) REFERENCES dbo.FiscalYears(FiscalYearId);
        PRINT '  [OK] FK_Journals_FiscalYear اضافه شد';
    END
    ELSE PRINT '  [SKIP] FK_Journals_FiscalYear از قبل وجود دارد';

    -- 4. ایجاد جدول SerialCounters
    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SerialCounters')
    BEGIN
        CREATE TABLE dbo.SerialCounters (
            FiscalYearId INT NOT NULL PRIMARY KEY REFERENCES FiscalYears(FiscalYearId),
            LastDocumentNumber INT NOT NULL DEFAULT 0
        );
        PRINT '  [OK] جدول SerialCounters ایجاد شد';
    END
    ELSE PRINT '  [SKIP] SerialCounters از قبل وجود دارد';

    -- 5. Backfill FiscalYearId برای اسناد موجود
    UPDATE j SET FiscalYearId = f.FiscalYearId
    FROM dbo.Journals j
    JOIN dbo.FiscalYears f ON j.OccurredAt >= f.StartDate 
                           AND j.OccurredAt < DATEADD(DAY, 1, f.EndDate)
    WHERE j.FiscalYearId IS NULL;

    DECLARE @updatedFY INT = @@ROWCOUNT;
    PRINT '  [OK] ' + CAST(@updatedFY AS NVARCHAR) + ' سند FiscalYearId گرفتند';

    -- 6. چک: اسنادی که FiscalYearId نگرفتند (خارج از بازه)
    DECLARE @orphans INT;
    SELECT @orphans = COUNT(*) FROM dbo.Journals WHERE FiscalYearId IS NULL;
    IF @orphans > 0
    BEGIN
        PRINT '  [WARNING] ' + CAST(@orphans AS NVARCHAR) + ' سند خارج از بازه سال‌های مالی هستند!';
        PRINT '  [WARNING] این اسناد:';
        SELECT JournalId, OccurredAt FROM dbo.Journals WHERE FiscalYearId IS NULL;
    END
    ELSE PRINT '  [OK] همه اسناد در بازه سال مالی هستند';

    -- 7. Backfill DocumentNumber بر اساس ترتیب JournalId (برای Status>=2)
    ;WITH numbered AS (
        SELECT JournalId, 
               ROW_NUMBER() OVER (PARTITION BY FiscalYearId ORDER BY JournalId) AS rn
        FROM dbo.Journals 
        WHERE Status >= 2 AND FiscalYearId IS NOT NULL AND DocumentNumber IS NULL
    )
    UPDATE j SET DocumentNumber = n.rn
    FROM dbo.Journals j 
    JOIN numbered n ON j.JournalId = n.JournalId;

    DECLARE @updatedDN INT = @@ROWCOUNT;
    PRINT '  [OK] ' + CAST(@updatedDN AS NVARCHAR) + ' سند DocumentNumber گرفتند';

    -- 8. مقداردهی SerialCounters
    INSERT INTO SerialCounters (FiscalYearId, LastDocumentNumber)
    SELECT FiscalYearId, MAX(DocumentNumber)
    FROM dbo.Journals 
    WHERE DocumentNumber IS NOT NULL
    GROUP BY FiscalYearId;

    DECLARE @counters INT = @@ROWCOUNT;
    PRINT '  [OK] ' + CAST(@counters AS NVARCHAR) + ' ردیف در SerialCounters مقداردهی شد';

    -- 9. تریگر خودکار تعیین FiscalYearId
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
        PRINT '  [OK] تریگر TR_Journals_SetFiscalYear ایجاد شد';
    END
    ELSE PRINT '  [SKIP] TR_Journals_SetFiscalYear از قبل وجود دارد';

    -- 10. ایندکس یکتا روی (FiscalYearId, DocumentNumber)
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Journals_FY_DocNum')
    BEGIN
        CREATE UNIQUE INDEX IX_Journals_FY_DocNum 
            ON dbo.Journals(FiscalYearId, DocumentNumber) 
            WHERE DocumentNumber IS NOT NULL;
        PRINT '  [OK] ایندکس IX_Journals_FY_DocNum ایجاد شد';
    END
    ELSE PRINT '  [SKIP] IX_Journals_FY_DocNum از قبل وجود دارد';

    PRINT '=== V006: Migration با موفقیت پایان یافت ===';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    DECLARE @errMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @errLine INT = ERROR_LINE();
    PRINT '  [ERROR] خطا در خط ' + CAST(@errLine AS NVARCHAR) + ': ' + @errMsg;
    ROLLBACK TRANSACTION;
    PRINT '=== V006: Migration برگشت خورد (ROLLBACK) ===';
    THROW;
END CATCH;
