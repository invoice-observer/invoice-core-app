using DataLayer.Services;

namespace InvoiceCoreApp.Services;

public static class RmqServiceCollectionExtensions
{
    public static IServiceCollection AddRmqPublisher(this IServiceCollection services)
    {
        services.AddSingleton<RmqPublisherService>();
        services.AddSingleton<IMessagePublisherService>(sp => sp.GetRequiredService<RmqPublisherService>());
        services.AddSingleton<IMessageReadiness>(sp => sp.GetRequiredService<RmqPublisherService>());
        services.AddHostedService<RmqStartupWorker>();
        return services;
    }
}
