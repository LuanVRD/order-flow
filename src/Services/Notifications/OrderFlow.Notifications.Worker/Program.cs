using OrderFlow.Notifications.Application;
using OrderFlow.Notifications.Infrastructure;
using OrderFlow.Notifications.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNotificationsApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
