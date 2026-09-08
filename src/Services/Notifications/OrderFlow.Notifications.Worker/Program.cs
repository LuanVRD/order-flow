using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Application;
using OrderFlow.Notifications.Infrastructure;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Worker;
using Serilog;
using Serilog.Formatting.Compact;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ServiceName", "Notifications.Worker")
        .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

    var useJsonConsole = builder.Configuration.GetValue<bool>("Serilog:UseJsonConsole");
    if (useJsonConsole || !builder.Environment.IsDevelopment())
    {
        configuration.WriteTo.Console(new CompactJsonFormatter());
    }
    else
    {
        configuration.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}");
    }
});

builder.Services.AddNotificationsApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    if (dbContext.Database.IsNpgsql())
    {
        await dbContext.Database.MigrateAsync();
    }
}

host.Run();


