IF OBJECT_ID(N'dbo.PostingTemplates', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PostingTemplateLines', N'U') IS NOT NULL
    BEGIN
        DECLARE @ptId_Deposit INT, @ptId_Installment INT, @ptId_Penalty INT;

        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT')
            INSERT INTO dbo.PostingTemplates (TemplateName) VALUES (N'MEMBER_DEPOSIT');
        SET @ptId_Deposit = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'MEMBER_DEPOSIT');
        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplateLines WHERE PostingTemplateId=@ptId_Deposit)
        BEGIN
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Deposit, 1, a.AccountId, 1, 1, 0 FROM dbo.Accounts a WHERE a.Code = N'1000'; -- DR Cash Total
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Deposit, 2, a.AccountId, 2, 1, 1 FROM dbo.Accounts a WHERE a.Code = N'2000'; -- CR Members Deposits Total (per member)
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_INSTALLMENT_PAY')
            INSERT INTO dbo.PostingTemplates (TemplateName) VALUES (N'LOAN_INSTALLMENT_PAY');
        SET @ptId_Installment = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_INSTALLMENT_PAY');
        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplateLines WHERE PostingTemplateId=@ptId_Installment)
        BEGIN
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Installment, 1, a.AccountId, 1, 1, 0 FROM dbo.Accounts a WHERE a.Code = N'1000'; -- DR Cash Total
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Installment, 2, a.AccountId, 2, 2, 0 FROM dbo.Accounts a WHERE a.Code = N'1100'; -- CR Loan Receivable Principal
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Installment, 3, a.AccountId, 2, 3, 0 FROM dbo.Accounts a WHERE a.Code = N'4000'; -- CR Interest Income
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_PENALTY_FEE')
            INSERT INTO dbo.PostingTemplates (TemplateName) VALUES (N'LOAN_PENALTY_FEE');
        SET @ptId_Penalty = (SELECT PostingTemplateId FROM dbo.PostingTemplates WHERE TemplateName = N'LOAN_PENALTY_FEE');
        IF NOT EXISTS (SELECT 1 FROM dbo.PostingTemplateLines WHERE PostingTemplateId=@ptId_Penalty)
        BEGIN
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Penalty, 1, a.AccountId, 1, 1, 0 FROM dbo.Accounts a WHERE a.Code = N'1000'; -- DR Cash Total
            INSERT INTO dbo.PostingTemplateLines (PostingTemplateId, LineNo, AccountId, Side, AmountSource, AllowMemberDimension)
            SELECT @ptId_Penalty, 2, a.AccountId, 2, 1, 0 FROM dbo.Accounts a WHERE a.Code = N'4100'; -- CR Penalty Income
        END
    END