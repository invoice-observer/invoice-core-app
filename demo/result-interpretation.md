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

## After: index on `Invoices (Supplier, DueDate)` (2026-11-09)

Same 500,000-invoice database, `queries.sql` query 2 run twice (second run counts), `measure.ps1` with 20 calls after 1 warm-up (3 calls for `/api/invoices`).

| Case | Without index | With index |
|---|---|---|
| Query 2 page reads | 6,787 (Invoices 6,042, InvoiceLines 745) | 786 (Invoices 161, InvoiceLines 625) |
| Query 2 elapsed | 20 ms | 1-2 ms |
| `GET /api/invoices/page?supplier=Supplier 042&size=50`, median (min / max) | 11.3 ms (9.8 / 15.7) | 12.5 ms (9.0 / 19.1) |
| `GET /api/invoices/page?supplier=Supplier 042&dueFrom=2026-11-10&dueTo=2026-12-10&size=50`, median (min / max) (2026-11-10) | 93.6 ms (86.9 / 170.1) | 103.5 ms (83.5 / 129.9) |
| `GET /api/invoices`, median | 15.6 s, 176 MB | 15.4 s, 176 MB |

**Findings**
- The index replaces a scan of Invoices with a seek: 6,042 reads become 161, about 38 times fewer. Total reads fall by 88%.
- The paged endpoint shows no measurable change, with or without the 30-day window. 11.3 ms against 12.5 ms is within run-to-run noise.
- The endpoint does not use the index. Its keyset query (`Id > afterId`, `ORDER BY Id`, top 51) walks the primary key and filters, and after the run `sys.dm_db_index_usage_stats` has no entry for `IX_Invoices_Supplier_DueDate`. With the 30-day window (2026-11-10 to 2026-12-10) only 52 of 500,000 invoices match (158 rows with their lines), so the walk reads far more rows and takes about 94 ms instead of 11 ms.
- The index helps the query in `queries.sql` (query 2), not this endpoint. A follow-up could test an index that ends in `Id`, such as `(Supplier, DueDate, Id)`; that is untested.
- `GET /api/invoices` is unaffected by the index. It returns about 176 MB and takes 15 s. Paging is the fix for it, and `/api/invoices/page` answers in about 11 ms with 5.7 KB.
- Single local machine, SQL Server in Docker, client on the same host. Compare reads, not milliseconds.
