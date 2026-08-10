-- Drop constraints first if they exist
IF EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_DbBackupSettings_AttachIfSmall')
BEGIN
    ALTER TABLE [dbo].[DbBackupSettings] DROP CONSTRAINT [DF_DbBackupSettings_AttachIfSmall];
END
GO

IF EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_DbBackupSettings_AttachMaxBytes')
BEGIN
    ALTER TABLE [dbo].[DbBackupSettings] DROP CONSTRAINT [DF_DbBackupSettings_AttachMaxBytes];
END
GO

-- Drop columns if they exist
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DbBackupSettings]') AND name = 'AttachIfSmall')
BEGIN
    ALTER TABLE [dbo].[DbBackupSettings] DROP COLUMN [AttachIfSmall];
    PRINT 'Dropped column AttachIfSmall from DbBackupSettings';
END
ELSE
BEGIN
    PRINT 'Column AttachIfSmall does not exist in DbBackupSettings';
END
GO

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DbBackupSettings]') AND name = 'AttachMaxBytes')
BEGIN
    ALTER TABLE [dbo].[DbBackupSettings] DROP COLUMN [AttachMaxBytes];
    PRINT 'Dropped column AttachMaxBytes from DbBackupSettings';
END
ELSE
BEGIN
    PRINT 'Column AttachMaxBytes does not exist in DbBackupSettings';
END
GO

PRINT 'Database schema updated successfully';
GO
