using DataLayer.Models;
using DataLayer.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

#pragma warning disable NUnit2045

namespace Tests.NUnit.Services
{
    /// <summary>
    /// Tests for IInvoiceService.GetAllAsync
    /// </summary>
    [TestFixture]
    public class InvoiceServiceGetAllAsyncTests
    {
        #region Mock Implementation Tests

        [Test]
        public async Task MockService_ReturnsPreloadedInvoices()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateMockServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();

            // Act
            var result = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1)); // MockInvoiceService has 1 preloaded invoice
            Assert.That(result[0].Description, Is.EqualTo("Office Supplies"));
            Assert.That(result[0].Supplier, Is.EqualTo("Acme Corp"));
            Assert.That(result[0].InvoiceLines, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task MockService_AfterAddingInvoice_ReturnsUpdatedList()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateMockServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();
            
            var newInvoice = new Invoice
            {
                Description = "Test Invoice",
                DueDate = DateTime.Today.AddDays(7),
                Supplier = "Test Supplier",
                InvoiceLines = [new InvoiceLine { Description = "Test Item", Price = 100.0, Quantity = 1 }]
            };

            // Act
            await invoiceService.AddAsync(newInvoice);
            var result = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2)); // 1 preloaded + 1 added
            Assert.That(result.Any(i => i.Description == "Test Invoice"), Is.True);
        }

        [Test]
        public async Task MockService_MultipleInvoices_ReturnsAllInvoices()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateMockServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();
            
            var invoices = new[]
            {
                new Invoice { Description = "Invoice 1", DueDate = DateTime.Today, Supplier = "Supplier 1", InvoiceLines = [new InvoiceLine()] },
                new Invoice { Description = "Invoice 2", DueDate = DateTime.Today, Supplier = "Supplier 2", InvoiceLines = [new InvoiceLine()] },
                new Invoice { Description = "Invoice 3", DueDate = DateTime.Today, Supplier = "Supplier 3", InvoiceLines = [new InvoiceLine()] }
            };

            // Act
            foreach (var invoice in invoices)
            {
                await invoiceService.AddAsync(invoice);
            }
            var result = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(4)); // 1 preloaded + 3 added
            Assert.That(result.Count(i => i.Description.StartsWith("Invoice")), Is.EqualTo(3));
        }

        #endregion

        #region SQLite Implementation Tests

        [Test]
        public async Task SqLiteService_EmptyDatabase_ReturnsEmptyList()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateSqLiteServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();

            // Act
            var result = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(0));
        }

        [Test]
        public async Task SqLiteService_AfterAddingInvoices_ReturnsAllInvoices()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateSqLiteServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();
            
            var invoice1 = new Invoice
            {
                Description = "SQLite Test Invoice 1",
                DueDate = DateTime.Today.AddDays(5),
                Supplier = "SQLite Supplier 1",
                InvoiceLines = [new InvoiceLine { Description = "SQLite Item 1", Price = 150.0, Quantity = 2 }]
            };
            
            var invoice2 = new Invoice
            {
                Description = "SQLite Test Invoice 2",
                DueDate = DateTime.Today.AddDays(10),
                Supplier = "SQLite Supplier 2",
                InvoiceLines = [new InvoiceLine { Description = "SQLite Item 2", Price = 200.0, Quantity = 1 }]
            };

            // Act
            await invoiceService.AddAsync(invoice1);
            await invoiceService.AddAsync(invoice2);
            var result = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.All(i => i.Id > 0), Is.True); // SQLite assigns IDs
            Assert.That(result.All(i => i.InvoiceLines.Count > 0), Is.True); // Include related data
            Assert.That(result.Any(i => i.Description == "SQLite Test Invoice 1"), Is.True);
            Assert.That(result.Any(i => i.Description == "SQLite Test Invoice 2"), Is.True);
        }

        [Test]
        public async Task SqLiteService_IncludesInvoiceLines()
        {
            // Arrange
            await using var serviceProvider = ServiceTestFixture.CreateSqLiteServiceProvider();
            var invoiceService = serviceProvider.GetRequiredService<IInvoiceService>();
            
            var invoice = new Invoice
            {
                Description = "Invoice with Multiple Lines",
                DueDate = DateTime.Today.AddDays(15),
                Supplier = "Multi-Line Supplier",
                InvoiceLines = 
                [
                    new InvoiceLine { Description = "Line 1", Price = 50.0, Quantity = 2 },
                    new InvoiceLine { Description = "Line 2", Price = 75.0, Quantity = 1 },
                    new InvoiceLine { Description = "Line 3", Price = 25.0, Quantity = 4 }
                ]
            };

            // Act
            await invoiceService.AddAsync(invoice);
            var result = await invoiceService.GetAllAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            var returnedInvoice = result[0];
            Assert.That(returnedInvoice.InvoiceLines, Has.Count.EqualTo(3));
            Assert.That(returnedInvoice.InvoiceLines.All(line => line.Description.StartsWith("Line")), Is.True);
            Assert.That(returnedInvoice.InvoiceLines.Sum(line => line.Price * line.Quantity), Is.EqualTo(275.0)); // 100 + 75 + 100
        }

        #endregion
    }
}