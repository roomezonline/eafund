IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FiscalYears')
BEGIN
    CREATE TABLE dbo.FiscalYears (
        FiscalYearId     INT           IDENTITY(1,1) PRIMARY KEY,
        YearCode         INT           NOT NULL,
        Title            NVARCHAR(100) NOT NULL,
        StartDate        DATE          NOT NULL,
        EndDate          DATE          NOT NULL,
        IsOpen           BIT           NOT NULL DEFAULT 1,
        IsClosed         BIT           NOT NULL DEFAULT 0,
        ClosedAt         DATETIME2     NULL,
        ClosedBy         NVARCHAR(128) NULL,
        Notes            NVARCHAR(500) NULL,
        CreatedAt        DATETIME2     NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE UNIQUE INDEX IX_FiscalYears_YearCode ON dbo.FiscalYears(YearCode);

    PRINT 'Created FiscalYears table';
END
ELSE
BEGIN
    PRINT 'FiscalYears table already exists';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FiscalYearBalances')
BEGIN
    CREATE TABLE dbo.FiscalYearBalances (
        BalanceId      BIGINT        IDENTITY(1,1) PRIMARY KEY,
        FiscalYearId   INT           NOT NULL REFERENCES dbo.FiscalYears(FiscalYearId),
        AccountId      BIGINT        NOT NULL REFERENCES dbo.Accounts(AccountId),
        OpeningDebit   DECIMAL(18,2) NOT NULL DEFAULT 0,
        OpeningCredit  DECIMAL(18,2) NOT NULL DEFAULT 0,
        CONSTRAINT UQ_FiscalYear_Account UNIQUE (FiscalYearId, AccountId)
    );

    CREATE INDEX IX_FiscalYearBalances_FiscalYearId ON dbo.FiscalYearBalances(FiscalYearId);
    CREATE INDEX IX_FiscalYearBalances_AccountId ON dbo.FiscalYearBalances(AccountId);

    PRINT 'Created FiscalYearBalances table';
END
ELSE
BEGIN
    PRINT 'FiscalYearBalances table already exists';
END
GO

PRINT 'Fiscal year migration completed successfully';
GO
