using DataLayer.Models;
using DataLayer.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

namespace Tests.NUnit.Services
{
    /// <summary>
    /// Tests for IInvoiceService.GetPageAsync, run against the mock and the EF (in-memory) service.
    /// </summary>
    [TestFixture]
    public class InvoiceServiceGetPageAsyncTests
    {
        private static readonly DateTime Day = new(2026, 1, 1);

        public static IEnumerable<TestCaseData> Providers()
        {
            yield return new TestCaseData((Func<ServiceProvider>)ServiceTestFixture.CreateMockServiceProvider).SetName("Mock");
            yield return new TestCaseData((Func<ServiceProvider>)ServiceTestFixture.CreateInMemoryServiceProvider).SetName("InMemory");
        }

        // Ids 1..6 (the mock's preloaded invoice, Id 1, is "Acme Corp" due today + 30 days; excluded below via supplier or afterId).
        private static async Task<IInvoiceService> SeedAsync(ServiceProvider provider)
        {
            var service = provider.GetRequiredService<IInvoiceService>();
            if (service is MockInvoiceService)
                await service.DeleteAsync(1);
            (string Supplier, int DayOffset)[] rows = [("A", 0), ("B", 1), ("A", 2), ("A", 3), ("B", 4), ("A", 5)];
            foreach (var (supplier, offset) in rows)
                await service.AddAsync(new Invoice
                {
                    Description = $"{supplier}{offset}", Supplier = supplier, DueDate = Day.AddDays(offset),
                    InvoiceLines =
                    [
                        new InvoiceLine { Description = "x", Price = 2.5, Quantity = 2 },
                        new InvoiceLine { Description = "y", Price = 1, Quantity = 3 }
                    ]
                });
            return service;
        }

        [TestCaseSource(nameof(Providers))]
        public async Task NoFilter_ReturnsFirstPageOrderedById_WithProjection(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);

            var page = await service.GetPageAsync(null, 4, null, null, null);

            Assert.That(page, Has.Count.EqualTo(4));
            Assert.That(page.Select(p => p.Id), Is.Ordered.Ascending);
            Assert.That(page[0].Supplier, Is.EqualTo("A"));
            Assert.That(page[0].Description, Is.EqualTo("A0"));
            Assert.That(page[0].DueDate, Is.EqualTo(Day));
            Assert.That(page[0].Total, Is.EqualTo(8.0)); // 2.5 * 2 + 1 * 3
        }

        [TestCaseSource(nameof(Providers))]
        public async Task AfterId_ContinuesWithoutOverlapOrGap(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);

            var seen = new List<int>();
            int? after = null;
            while (true)
            {
                var page = await service.GetPageAsync(after, 4, null, null, null);
                if (page.Count == 0) break;
                seen.AddRange(page.Select(p => p.Id));
                after = page[^1].Id;
            }

            Assert.That(seen, Is.Unique);
            Assert.That(seen, Is.Ordered.Ascending);
            Assert.That(seen, Has.Count.EqualTo(6));
        }

        [TestCaseSource(nameof(Providers))]
        public async Task LastPage_IsShorterThanSize(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);
            var first = await service.GetPageAsync(null, 4, null, null, null);

            var second = await service.GetPageAsync(first[^1].Id, 4, null, null, null);

            Assert.That(second, Has.Count.EqualTo(2));
        }

        [TestCaseSource(nameof(Providers))]
        public async Task Supplier_FiltersAndPagesWithinSupplier(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);

            var page = await service.GetPageAsync(null, 3, "A", null, null);
            var rest = await service.GetPageAsync(page[^1].Id, 3, "A", null, null);

            Assert.That(page.Select(p => p.Description), Is.EqualTo(new[] { "A0", "A2", "A3" }));
            Assert.That(rest.Select(p => p.Description), Is.EqualTo(new[] { "A5" }));
        }

        [TestCaseSource(nameof(Providers))]
        public async Task DueDates_FromIsInclusive_ToIsExclusive(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);

            var page = await service.GetPageAsync(null, 50, null, Day.AddDays(1), Day.AddDays(4));

            Assert.That(page.Select(p => p.Description), Is.EqualTo(new[] { "B1", "A2", "A3" }));
        }

        [TestCaseSource(nameof(Providers))]
        public async Task AllFilters_Combine(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);

            var page = await service.GetPageAsync(2, 50, "A", Day.AddDays(1), Day.AddDays(5));

            Assert.That(page.Select(p => p.Description), Is.EqualTo(new[] { "A2", "A3" }));
        }

        [TestCaseSource(nameof(Providers))]
        public async Task NoMatch_ReturnsEmpty(Func<ServiceProvider> create)
        {
            await using var provider = create();
            var service = await SeedAsync(provider);

            Assert.That(await service.GetPageAsync(null, 50, "nobody", null, null), Is.Empty);
        }

        [Test]
        public async Task Ef_DoesNotTrackEntities()
        {
            await using var provider = ServiceTestFixture.CreateInMemoryServiceProvider();
            var service = await SeedAsync(provider);
            var db = provider.GetRequiredService<DataLayer.InvoiceDbContext>();
            db.ChangeTracker.Clear();

            await service.GetPageAsync(null, 50, null, null, null);

            Assert.That(db.ChangeTracker.Entries(), Is.Empty);
        }
    }
}
