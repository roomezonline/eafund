IF OBJECT_ID(N'dbo.SmsTemplates', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsTemplates WHERE TemplateKey = N'tx_deposit_credit')
        INSERT INTO dbo.SmsTemplates (TemplateKey, Title, Body, ProviderTemplateId, IsActive)
        VALUES (N'tx_deposit_credit', N'متن پیامک واریز سپرده', N'واریز سپرده شما ثبت شد. مبلغ: {PRICE} بابت: {BABAT} مانده: {MANPRICE}', NULL, 1);
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsTemplates WHERE TemplateKey = N'loan_installment_reminder')
        INSERT INTO dbo.SmsTemplates (TemplateKey, Title, Body, ProviderTemplateId, IsActive)
        VALUES (N'loan_installment_reminder', N'U.O�U+ U_UOOU.Uc UOOO_O�U^O�UO U,O3O� U^OU.', N'UOOO_O�U^O�UO: O3O�O�O3UOO_ U,O3O� U^OU. O'U.O U+O�O_UOUc OO3O�. U.O"U,O�: {PRICE} U.OU+O_U�: {MANPRICE}', NULL, 1);
END