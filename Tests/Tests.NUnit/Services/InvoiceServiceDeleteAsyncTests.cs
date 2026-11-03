using DataLayer;
using DataLayer.Models;
using DataLayer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Tests.NUnit.Services
{
    [TestFixture]
    public class InvoiceServiceDeleteAsyncTests
    {
        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        public async Task SqLiteService_DeletesInvoiceAndItsLines(int lineCount, bool loadBeforeDelete)
        {
            await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<InvoiceDbContext>().UseSqlite(connection).Options;
            await using var db = new InvoiceDbContext(options);
            var publisher = new Mock<IMessagePublisherService>();
            var service = new SqLiteInvoiceService(db, publisher.Object, Mock.Of<IMessageReadiness>(r => r.State == MessageReadinessState.Ready && r.CanWriteInvoices));
            var invoice = new Invoice
            {
                Description = "Invoice to delete",
                InvoiceLines = Enumerable.Range(0, lineCount)
                    .Select(i => new InvoiceLine { Description = $"Line {i}" }).ToList()
            };
            var otherInvoice = new Invoice
            {
                Description = "Invoice to keep",
                InvoiceLines = [new InvoiceLine { Description = "Line to keep" }]
            };
            db.Invoices.AddRange(invoice, otherInvoice);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            if (loadBeforeDelete)
            {
                await service.GetByIdAsync(invoice.Id);
            }

            await service.DeleteAsync(invoice.Id);
            db.ChangeTracker.Clear();

            Assert.That(await db.Invoices.AnyAsync(i => i.Id == invoice.Id), Is.False);
            var remaining = await db.Invoices.Include(i => i.InvoiceLines).SingleAsync();
            Assert.That(remaining.Id, Is.EqualTo(otherInvoice.Id));
            Assert.That(remaining.InvoiceLines, Has.Count.EqualTo(1));
            Assert.That(await db.InvoiceLines.CountAsync(), Is.EqualTo(1));
            publisher.Verify(p => p.PublishInvoiceEventAsync("deleted", It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task SqLiteService_MissingInvoiceDoesNotPublishDeletion()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<InvoiceDbContext>().UseSqlite(connection).Options;
            await using var db = new InvoiceDbContext(options);
            var publisher = new Mock<IMessagePublisherService>();
            var service = new SqLiteInvoiceService(db, publisher.Object, Mock.Of<IMessageReadiness>(r => r.State == MessageReadinessState.Ready && r.CanWriteInvoices));

            await service.DeleteAsync(123);

            publisher.Verify(p => p.PublishInvoiceEventAsync(It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
