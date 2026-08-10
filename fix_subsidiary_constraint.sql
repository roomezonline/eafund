-- Fix CK_Accounts_Hierarchy constraint to allow Level 4 accounts with optional parents
-- This script updates the existing constraint in the database

USE [foodir_EmamAli_minibank]
GO

-- Drop existing constraint if it exists
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy')
BEGIN
    ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_Hierarchy];
    PRINT N'محدودیت قدیمی CK_Accounts_Hierarchy حذف شد';
END

-- Add updated constraint that allows Level 4 accounts with optional parents
ALTER TABLE [dbo].[Accounts] 
ADD CONSTRAINT [CK_Accounts_Hierarchy] 
CHECK (
    (Level = 1 AND ParentAccountId IS NULL) OR
    (Level = 2 AND ParentAccountId IS NOT NULL) OR  
    (Level = 3 AND ParentAccountId IS NOT NULL) OR
    (Level = 4) OR -- Level 4 can have optional parent
    (Level IS NULL)
);

PRINT N'محدودیت جدید CK_Accounts_Hierarchy اضافه شد - تفضیلی می‌تواند بدون والد باشد';
GO
