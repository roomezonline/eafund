-- اسکریپت پاک کردن همه جداول (به جز Members, Households, BankAccounts)

USE [foodir_EmamAli_minibank]
GO

-- ابتدا GlAccountId را از BankAccounts null کنیم
UPDATE [dbo].[BankAccounts] SET GlAccountId = NULL WHERE GlAccountId IS NOT NULL

-- حذف همه foreign key constraints
DECLARE @sql NVARCHAR(MAX) = ''
SELECT @sql = @sql + 'ALTER TABLE [' + SCHEMA_NAME(schema_id) + '].[' + OBJECT_NAME(parent_object_id) + '] DROP CONSTRAINT [' + name + ']' + CHAR(13)
FROM sys.foreign_keys 
WHERE OBJECT_NAME(referenced_object_id) NOT IN ('Members', 'Households')
EXEC sp_executesql @sql

-- حذف جداول (به جز Members, Households, BankAccounts)
SET @sql = ''
SELECT @sql = @sql + 'DROP TABLE [' + SCHEMA_NAME(schema_id) + '].[' + name + ']' + CHAR(13)
FROM sys.tables 
WHERE name NOT IN ('Members', 'Households', 'BankAccounts', '__EFMigrationsHistory')
EXEC sp_executesql @sql

-- حذف Views
IF OBJECT_ID('dbo.vw_TrialBalance', 'V') IS NOT NULL DROP VIEW [dbo].[vw_TrialBalance]
IF OBJECT_ID('dbo.vw_JournalLinesFlat', 'V') IS NOT NULL DROP VIEW [dbo].[vw_JournalLinesFlat]
IF OBJECT_ID('dbo.vw_MemberDepositBalances', 'V') IS NOT NULL DROP VIEW [dbo].[vw_MemberDepositBalances]

-- حذف Stored Procedures
IF OBJECT_ID('dbo.sp_BalanceSheet_AsOf', 'P') IS NOT NULL DROP PROCEDURE [dbo].[sp_BalanceSheet_AsOf]

-- حذف Functions
IF OBJECT_ID('dbo.fn_GetAccountBalance', 'FN') IS NOT NULL DROP FUNCTION [dbo].[fn_GetAccountBalance]

-- فعال کردن مجدد foreign key constraints
EXEC sp_MSforeachtable "ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all"
GO

PRINT 'همه جداول به جز Members، Households و BankAccounts حذف شدند.'
PRINT 'حالا می‌توانید برنامه را اجرا کنید تا جداول مجدداً ایجاد شوند.'
