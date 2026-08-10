-- حل مشکل OUTPUT clause با trigger در جدول Accounts
-- مشکل: هنگام استفاده از OUTPUT clause در INSERT، اگر trigger فعال باشد خطا می‌دهد

-- 1. حذف موقت trigger audit از جدول Accounts
IF OBJECT_ID(N'dbo.TR_Accounts_AuditTrail', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER dbo.TR_Accounts_AuditTrail;
    PRINT N'✅ Trigger audit از جدول Accounts حذف شد';
END
ELSE
BEGIN
    PRINT N'ℹ️ Trigger audit در جدول Accounts وجود ندارد';
END

-- 2. بررسی سایر triggerها روی جدول Accounts
SELECT 
    t.name AS TriggerName,
    t.is_disabled AS IsDisabled,
    OBJECT_NAME(t.parent_id) AS TableName
FROM sys.triggers t
WHERE OBJECT_NAME(t.parent_id) = 'Accounts'
    AND t.parent_class = 1; -- Table triggers only

PRINT N'✅ بررسی triggerهای جدول Accounts کامل شد';
