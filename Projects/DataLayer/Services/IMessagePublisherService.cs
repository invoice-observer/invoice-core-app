using System.Threading;

namespace DataLayer.Services
{
    public interface IMessagePublisherService
    {
        Task PublishInvoiceEventAsync(string eventType, object data, CancellationToken cancellationToken = default);
    }
}