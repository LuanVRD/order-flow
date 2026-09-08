using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Orders.Api.Middlewares;
using OrderFlow.Orders.Application;
using OrderFlow.Orders.Infrastructure;
using OrderFlow.Orders.Infrastructure.Persistence;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ServiceName", "Orders.Api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

    var useJsonConsole = context.Configuration.GetValue<bool>("Serilog:UseJsonConsole");
    if (useJsonConsole || !context.HostingEnvironment.IsDevelopment())
    {
        configuration.WriteTo.Console(new CompactJsonFormatter());
    }
    else
    {
        configuration.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}");
    }
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentCors", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders(CorrelationConstants.HeaderName);
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    if (dbContext.Database.IsNpgsql())
    {
        await dbContext.Database.MigrateAsync();
    }
}

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        if (httpContext.Response.Headers.TryGetValue(CorrelationConstants.HeaderName, out var corrId))
        {
            diagnosticContext.Set(CorrelationConstants.PropertyName, corrId.ToString());
        }
    };
});

app.UseExceptionHandler();

app.UseCors("DevelopmentCors");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();

public partial class Program { }


