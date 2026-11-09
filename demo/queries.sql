-- SQL that EF Core generates, for comparing reads and times in SSMS. Needs a seeded InvoiceDb (see seed.sql).
--
-- Query 2 uses sp_executesql because EF sends parameterized queries that way.
-- Local variables in the query text would give different row estimates and possibly a different plan.
--
-- SSMS spends time on drawing the grid. To exclude it: Query > Query Options > Results > Grid > Discard results after execution.

USE InvoiceDb;

SET STATISTICS IO, TIME ON;

-- Query 1: EfInvoiceService.GetAllAsync
--   _db.Invoices.Include(i => i.InvoiceLines)
SELECT [i].[Id], [i].[Description], [i].[DueDate], [i].[Supplier], [i0].[Id], [i0].[Description], [i0].[InvoiceId], [i0].[Price], [i0].[Quantity]
FROM [Invoices] AS [i]
LEFT JOIN [InvoiceLines] AS [i0] ON [i].[Id] = [i0].[InvoiceId]
ORDER BY [i].[Id];

-- Query 2: demo case. Not in the application code; the SQL comes from this LINQ, via ToQueryString:
--   var from = DateTime.Today;
--   var to = from.AddDays(30);
--   _db.Invoices.Include(i => i.InvoiceLines)
--       .Where(i => i.Supplier == supplier && i.DueDate >= from && i.DueDate < to)
DECLARE @today datetime2 = CAST(CAST(SYSDATETIME() AS date) AS datetime2),
        @in30days datetime2 = DATEADD(DAY, 30, CAST(CAST(SYSDATETIME() AS date) AS datetime2));

EXEC sp_executesql
    N'SELECT [i].[Id], [i].[Description], [i].[DueDate], [i].[Supplier], [i0].[Id], [i0].[Description], [i0].[InvoiceId], [i0].[Price], [i0].[Quantity]
FROM [Invoices] AS [i]
LEFT JOIN [InvoiceLines] AS [i0] ON [i].[Id] = [i0].[InvoiceId]
WHERE [i].[Supplier] = @supplier AND [i].[DueDate] >= @from AND [i].[DueDate] < @to
ORDER BY [i].[Id]',
    N'@supplier nvarchar(100), @from datetime2, @to datetime2',
    @supplier = N'Supplier 042',
    @from = @today,
    @to = @in30days;

SET STATISTICS IO, TIME OFF;
