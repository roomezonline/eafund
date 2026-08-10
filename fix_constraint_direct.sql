-- Direct SQL script to fix CK_Accounts_Hierarchy constraint
-- This allows Level 4 (subsidiary) accounts to have optional parents

USE [foodir_EmamAli_minibank]
GO

PRINT 'Starting constraint fix for CK_Accounts_Hierarchy...'

-- Check if constraint exists
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy')
BEGIN
    PRINT 'Found existing CK_Accounts_Hierarchy constraint - dropping it...'
    
    -- Drop existing constraint
    ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_Hierarchy];
    PRINT 'Old constraint dropped successfully'
END
ELSE
BEGIN
    PRINT 'CK_Accounts_Hierarchy constraint does not exist'
END

-- Add updated constraint that allows Level 4 accounts with optional parents
PRINT 'Adding new CK_Accounts_Hierarchy constraint...'

ALTER TABLE [dbo].[Accounts] 
ADD CONSTRAINT [CK_Accounts_Hierarchy] 
CHECK (
    (Level = 1 AND ParentAccountId IS NULL) OR
    (Level = 2 AND ParentAccountId IS NOT NULL) OR  
    (Level = 3 AND ParentAccountId IS NOT NULL) OR
    (Level = 4) OR -- Level 4 can have optional parent (both NULL and NOT NULL allowed)
    (Level IS NULL)
);

PRINT 'New constraint added successfully!'
PRINT 'Level 4 accounts can now have optional parents'

-- Verify the constraint was created
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy')
BEGIN
    PRINT 'SUCCESS: CK_Accounts_Hierarchy constraint is now active'
    
    -- Show constraint definition
    SELECT 
        cc.name AS ConstraintName,
        cc.definition AS ConstraintDefinition,
        t.name AS TableName
    FROM sys.check_constraints cc
    JOIN sys.tables t ON cc.parent_object_id = t.object_id
    WHERE cc.name = N'CK_Accounts_Hierarchy'
END
ELSE
BEGIN
    PRINT 'ERROR: Failed to create CK_Accounts_Hierarchy constraint'
END

PRINT 'Constraint fix completed!'
GO
