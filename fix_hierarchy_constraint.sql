-- Fix hierarchy constraint to support Level=4 (تفضیلی)
USE [foodir_EmamAli_minibank];
GO

-- Drop existing constraint if it exists
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy')
BEGIN
    ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_Hierarchy];
    PRINT N'محدودیت قدیمی حذف شد';
END

-- Add updated constraint that supports Level=4
ALTER TABLE [dbo].[Accounts] 
ADD CONSTRAINT [CK_Accounts_Hierarchy] 
CHECK (
    (Level = 1 AND ParentAccountId IS NULL) OR
    (Level = 2 AND ParentAccountId IS NOT NULL) OR  
    (Level = 3 AND ParentAccountId IS NOT NULL) OR
    (Level = 4 AND ParentAccountId IS NOT NULL) OR
    (Level IS NULL)
);

PRINT N'محدودیت جدید با پشتیبانی از تفضیلی (Level=4) اضافه شد';

-- Verify constraint exists
SELECT 
    name, 
    definition 
FROM sys.check_constraints 
WHERE name = 'CK_Accounts_Hierarchy';

PRINT N'بررسی محدودیت کامل شد';
