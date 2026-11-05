-- Seeds InvoiceDb with InvoiceCount invoices and their lines (1-5 per invoice), in 10 batches.
--
-- Needs the empty tables that EF creates, so start the app and open a page that uses invoices first.
-- Reset by dropping the database.
--
-- Run: sqlcmd -S 127.0.0.1,1433 -U sa -C -b -v InvoiceCount=50000 -i demo/seed.sql
-- InvoiceCount is required, so run this in sqlcmd or in SSMS SQLCMD mode.
--
-- The whole script is one batch on purpose: THROW then stops all of it. Do not add GO.

USE InvoiceDb;

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Invoices', N'U') IS NULL OR OBJECT_ID(N'dbo.InvoiceLines', N'U') IS NULL
    THROW 50001, N'Seed stopped: tables dbo.Invoices and dbo.InvoiceLines do not exist. Start the app and open a page that uses invoices, then run this script again.', 1;

IF EXISTS (SELECT 1 FROM dbo.Invoices) OR EXISTS (SELECT 1 FROM dbo.InvoiceLines)
    THROW 50002, N'Seed stopped: dbo.Invoices or dbo.InvoiceLines is not empty. Nothing was inserted. Drop the database to reset.', 1;

DECLARE @InvoiceCount int = $(InvoiceCount),
        @BatchSize    int,
        @From         int = 1,
        @To           int,
        -- Due dates: from one year ago to two years ahead.
        @DueStart     date = DATEADD(YEAR, -1, CAST(SYSDATETIME() AS date)),
        @DueDays      int,
        -- Multiplicative hashes keep the values reproducible and independent of each other.
        @HashA        bigint = 2654435761,
        @HashB        bigint = 2246822519,
        @HashRange    bigint = 4294967296;

SET @BatchSize = (@InvoiceCount + 9) / 10;
SET @DueDays = DATEDIFF(DAY, @DueStart, DATEADD(YEAR, 3, @DueStart));

WHILE @From <= @InvoiceCount
BEGIN
    SET @To = LEAST(@From + @BatchSize - 1, @InvoiceCount);

    BEGIN TRANSACTION;

    -- Explicit ids let the lines reference their invoice without reading the ids back.
    SET IDENTITY_INSERT dbo.Invoices ON;

    INSERT dbo.Invoices (Id, Description, DueDate, Supplier)
    SELECT s.value,
           CONCAT(N'Invoice ', s.value),
           DATEADD(DAY, (s.value * @HashB % @HashRange) / 4096 % @DueDays, @DueStart),
           CONCAT(N'Supplier ', RIGHT(CONCAT(N'00', (s.value - 1) % 200 + 1), 3))
    FROM GENERATE_SERIES(@From, @To) AS s;

    SET IDENTITY_INSERT dbo.Invoices OFF;

    INSERT dbo.InvoiceLines (Description, Price, Quantity, InvoiceId)
    SELECT CONCAT(N'Line ', l.value, N' of invoice ', s.value),
           (100 + h.Value % 99900) / 100.0,
           1 + h.Value / 16 % 20,
           s.value
    FROM GENERATE_SERIES(@From, @To) AS s
    CROSS JOIN GENERATE_SERIES(1, 5) AS l
    CROSS APPLY (SELECT ((s.value * 5 + l.value) * @HashB % @HashRange) / 256 AS Value) AS h
    WHERE l.value <= 1 + (s.value * @HashA % @HashRange) / 65536 % 5;

    COMMIT TRANSACTION;

    -- Lets SQL Server reuse the log. The log truncates here only while the database has no full
    -- backup (FULL recovery, inherited from model) or uses SIMPLE recovery.
    CHECKPOINT;

    RAISERROR(N'Seeded invoices up to %d', 0, 1, @To) WITH NOWAIT;

    SET @From = @To + 1;
END;

SELECT N'dbo.Invoices' AS TableName, COUNT_BIG(*) AS Row_Count FROM dbo.Invoices
UNION ALL
SELECT N'dbo.InvoiceLines', COUNT_BIG(*) FROM dbo.InvoiceLines;

SELECT name AS FileName,
       type_desc AS FileType,
       CAST(size * 8 / 1024.0 AS decimal(12, 2)) AS SizeMB
FROM sys.database_files;
