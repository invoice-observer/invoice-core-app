-- Creates IX_Invoices_Supplier_DueDate on the existing InvoiceDb if it is missing.
-- With -v Action=drop it drops the index instead. EnsureCreated does not change existing tables.
-- The name is the one EF Core generates, so a database created later by EF has the same index.
--
-- Create: sqlcmd -S 127.0.0.1,1433 -U sa -C -b -v Action=create -i demo/index.sql
-- Drop:   sqlcmd -S 127.0.0.1,1433 -U sa -C -b -v Action=drop -i demo/index.sql
-- Action is required, so run this in sqlcmd or in SSMS SQLCMD mode.

USE InvoiceDb;

SET NOCOUNT ON;

DECLARE @Action nvarchar(10) = N'$(Action)';

IF @Action NOT IN (N'create', N'drop')
    THROW 50003, N'Action must be create or drop.', 1;

IF OBJECT_ID(N'dbo.Invoices', N'U') IS NULL
    THROW 50004, N'Table dbo.Invoices does not exist. Start the app first.', 1;

IF @Action = N'create' AND INDEXPROPERTY(OBJECT_ID(N'dbo.Invoices'), N'IX_Invoices_Supplier_DueDate', N'IndexID') IS NULL
BEGIN
    CREATE INDEX IX_Invoices_Supplier_DueDate ON dbo.Invoices (Supplier, DueDate);
    PRINT N'Index created.';
END
ELSE IF @Action = N'drop' AND INDEXPROPERTY(OBJECT_ID(N'dbo.Invoices'), N'IX_Invoices_Supplier_DueDate', N'IndexID') IS NOT NULL
BEGIN
    DROP INDEX IX_Invoices_Supplier_DueDate ON dbo.Invoices;
    PRINT N'Index dropped.';
END
ELSE
    PRINT N'Nothing to do.';
