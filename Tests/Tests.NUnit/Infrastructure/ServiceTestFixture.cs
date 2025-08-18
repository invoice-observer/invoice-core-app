using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using DataLayer;
using DataLayer.Services;
using Moq;

namespace Tests.NUnit.Infrastructure
{
    /// <summary>
    /// Test fixture for configuring dependency injection with different implementations
    /// </summary>
    public static class ServiceTestFixture
    {
        /// <summary>
        /// Creates a service provider configured with MockInvoiceService
        /// </summary>
        public static ServiceProvider CreateMockServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IInvoiceService, MockInvoiceService>();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Creates a service provider configured with SqLiteInvoiceService using in-memory database
        /// </summary>
        public static ServiceProvider CreateSqLiteServiceProvider()
        {
            var services = new ServiceCollection();

            // services.AddSingleton<IMessagePublisherService, RmqPublisherService>();

            // Use publisher mock:
            var mockMessagePublisher = new Mock<IMessagePublisherService>();
            services.AddSingleton(mockMessagePublisher.Object);

            // services.AddDbContext<InvoiceDbContext>(options =>
            //     options.UseSqlite("Data Source=Invoices.db"));
            // services.AddScoped<IInvoiceService, SqLiteInvoiceService>();

            // Use in-memory database:
            services.AddDbContext<InvoiceDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: "DB-" + Guid.NewGuid().ToString()));



            services.AddScoped<IInvoiceService, SqLiteInvoiceService>();
            
            return services.BuildServiceProvider();
        }
    }
}