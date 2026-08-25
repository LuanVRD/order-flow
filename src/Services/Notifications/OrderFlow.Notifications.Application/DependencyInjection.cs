using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Notifications.Application.UseCases;

namespace OrderFlow.Notifications.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationsApplication(this IServiceCollection services)
    {
        services.AddScoped<ProcessOrderCreatedEventUseCase>();
        services.AddScoped<ProcessOrderStatusChangedEventUseCase>();
        services.AddScoped<ProcessOrderCompletedEventUseCase>();
        services.AddScoped<ProcessOrderCancelledEventUseCase>();

        return services;
    }
}
