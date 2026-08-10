IF OBJECT_ID(N'dbo.Loans', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Loans', 'MemberCode') IS NULL
    BEGIN
        ALTER TABLE [dbo].[Loans] ADD [MemberCode] NVARCHAR(32) NULL;
        PRINT N'ستون MemberCode به جدول Loans اضافه شد';
    END

    IF COL_LENGTH('dbo.Loans', 'MemberCode') IS NOT NULL
    BEGIN
        UPDATE l
        SET l.MemberCode = sub.Code
        FROM dbo.Loans l
        OUTER APPLY (
            SELECT TOP 1 a.Code
            FROM dbo.Accounts a
            WHERE a.MemberId = l.MemberId
              AND a.IsActive = 1
              AND a.Code LIKE N'M-%'
            ORDER BY a.AccountId DESC
        ) sub
        WHERE (l.MemberCode IS NULL OR LTRIM(RTRIM(l.MemberCode)) = N'')
          AND sub.Code IS NOT NULL;
    END

    IF OBJECT_ID(N'dbo.Members', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Loans_Members')
    BEGIN
        ALTER TABLE [dbo].[Loans]
        ADD CONSTRAINT [FK_Loans_Members] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]);
    END

    IF OBJECT_ID(N'dbo.LoanRatePlans', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Loans_LoanRatePlans')
    BEGIN
        ALTER TABLE [dbo].[Loans]
        ADD CONSTRAINT [FK_Loans_LoanRatePlans] FOREIGN KEY ([LoanRatePlanId]) REFERENCES [dbo].[LoanRatePlans]([PlanId]);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Loans]') AND name = N'IX_Loans_Member_Status')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_Loans_Member_Status]
        ON [dbo].[Loans]([MemberId] ASC, [Status] ASC, [LoanId] DESC)
        INCLUDE ([DisbursedAt], [Principal], [OutstandingPrincipal]);
    END

    IF COL_LENGTH('dbo.Loans', 'MemberCode') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Loans]') AND name = N'IX_Loans_MemberCode_Status')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_Loans_MemberCode_Status]
        ON [dbo].[Loans]([MemberCode] ASC, [Status] ASC, [LoanId] DESC)
        INCLUDE ([DisbursedAt], [Principal], [OutstandingPrincipal]);
    END
END