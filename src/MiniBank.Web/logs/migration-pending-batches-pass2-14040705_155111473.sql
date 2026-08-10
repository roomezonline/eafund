-- Auto-map by TypeCode if present
    IF OBJECT_ID(N'dbo.TransactionTypes', N'U') IS NOT NULL AND COL_LENGTH('dbo.TransactionTypes', 'TypeCode') IS NOT NULL
    BEGIN
        UPDATE tt SET PostingTemplateId = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT')
        WHERE tt.TypeCode = N'MEMBER_DEPOSIT' AND (tt.PostingTemplateId IS NULL OR tt.PostingTemplateId <> (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT'));
        UPDATE tt SET PostingTemplateId = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_INSTALLMENT_PAY')
        WHERE tt.TypeCode = N'LOAN_INSTALLMENT_PAY' AND (tt.PostingTemplateId IS NULL OR tt.PostingTemplateId <> (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_INSTALLMENT_PAY'));
        UPDATE tt SET PostingTemplateId = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_PENALTY_FEE')
        WHERE tt.TypeCode = N'LOAN_PENALTY_FEE' AND (tt.PostingTemplateId IS NULL OR tt.PostingTemplateId <> (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_PENALTY_FEE'));
    END