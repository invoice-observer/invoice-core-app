using DataLayer.Models;
using DataLayer.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

#pragma warning disable NUnit2045

namespace Tests.NUnit.Services
{
    /// <summary>
    /// Tests for IInvoiceService.AddAsync
    /// </summary>
    [TestFixture]
    public class InvoiceServiceAddAsyncTests
    {
        #region Mock Implementation Tests

        [Test]
        public async Task MockService_AddsInvoiceSuccessfully()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateMockServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();
            
            var newInvoice = new Invoice
            {
                Description = "New Mock Invoice",
                DueDate = DateTime.Today.AddDays(30),
                Supplier = "Mock Supplier",
                InvoiceLines = [new InvoiceLine { Description = "Mock Item", Price = 250.00, Quantity = 2 }]
            };

            // Act
            await invoiceService.AddAsync(newInvoice);
            var allInvoices = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(newInvoice.Id, Is.GreaterThan(0)); // MockService assigns ID
            Assert.That(allInvoices, Has.Count.EqualTo(2)); // 1 preloaded + 1 added
            Assert.That(allInvoices.Any(i => i.Id == newInvoice.Id), Is.True);
            Assert.That(allInvoices.Any(i => i.Description == "New Mock Invoice"), Is.True);
        }

        #endregion

        #region SQLite Implementation Tests

        [Test]
        public async Task SqLiteService_PersistsInvoiceToDatabase()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateSqLiteServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();
            
            var invoice = new Invoice
            {
                Description = "SQLite Persistence Test",
                DueDate = DateTime.Today.AddDays(25),
                Supplier = "SQLite Test Supplier",
                InvoiceLines = [new InvoiceLine { Description = "SQLite Item", Price = 199.99, Quantity = 1 }]
            };

            // Act
            await invoiceService.AddAsync(invoice);
            var allInvoices = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(invoice.Id, Is.GreaterThan(0)); // EF assigns database ID
            Assert.That(allInvoices, Has.Count.EqualTo(1));
            
            var persistedInvoice = allInvoices[0];
            Assert.That(persistedInvoice.Description, Is.EqualTo("SQLite Persistence Test"));
            Assert.That(persistedInvoice.Supplier, Is.EqualTo("SQLite Test Supplier"));
            Assert.That(persistedInvoice.DueDate.Date, Is.EqualTo(DateTime.Today.AddDays(25)));
            Assert.That(persistedInvoice.InvoiceLines, Has.Count.EqualTo(1));
        }

        #endregion
    }
}