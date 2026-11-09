# Demo step: baseline with 500,000 invoices

**Setup:** `demo/seed.sql` filled InvoiceDb with about 500,000 invoices and 1,499,961 lines. `demo/queries.sql` was run twice, and the second run is used because the first includes compile time.

| Query | Rows | Page reads | Elapsed |
|---|---|---|---|
| GetAllAsync | 1,499,961 | 23,923 (Invoices 6,042, InvoiceLines 17,881) | 11.0 s |
| Demo case: Supplier 042, next 30 days | 154 | 6,787 (Invoices 6,042, InvoiceLines 745) | 18 ms |

**Findings**
- GetAllAsync scales linearly: 10 times the data gave about 10 times the reads and time.
- It has no paging, and `Include` repeats each invoice's columns for every line.
- The demo query scans the whole Invoices table, 6,042 reads, to return 154 rows, because only the primary key is indexed.
- A parallel plan keeps it at 18 ms, so compare page reads, not milliseconds.

**Per-request floor:** `GET /api/invoices/42` (single invoice, 20 calls via `measure.ps1`) has a median of 10.9 ms, min 8.6 ms and max 15.1 ms. This includes the `EnsureCreated` checks.

**Next step:** create an index on `Invoices (Supplier, DueDate)`, run `queries.sql` again, and compare the reads.
