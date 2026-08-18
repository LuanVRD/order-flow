using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Application.Validators;

namespace OrderFlow.Orders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateOrderCommandValidator>();

        services.AddScoped<CreateOrderUseCase>();
        services.AddScoped<GetOrderByIdUseCase>();
        services.AddScoped<GetOrdersUseCase>();
        services.AddScoped<ChangeOrderStatusUseCase>();
        services.AddScoped<CancelOrderUseCase>();

        return services;
    }
}
