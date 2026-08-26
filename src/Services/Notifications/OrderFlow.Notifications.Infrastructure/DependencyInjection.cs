using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Infrastructure.Persistence.Repositories;

namespace OrderFlow.Notifications.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();

        if (services.Any(sd => sd.ServiceType == typeof(DbContextOptions<NotificationsDbContext>)))
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString("NotificationsConnection")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:NotificationsConnection"]
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'NotificationsConnection' (or 'DefaultConnection') was not found.");
        }

        if (connectionString.Contains("DataSource=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<NotificationsDbContext>(options =>
                options.UseSqlite(connectionString));
        }
        else
        {
            services.AddDbContext<NotificationsDbContext>(options =>
                options.UseNpgsql(connectionString));
        }

        return services;
    }
}
