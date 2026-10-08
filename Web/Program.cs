using Application.SharedKernel;
using Infrastructure.Observability.Configuration;
using Web.SharedKernel.Observability;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.ClientGeneration;
using Web.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBaseApiServices();
// Core CQRS behaviors are registered in the host; feature modules must register
// their own handler/validator assemblies when enabled.
builder.Services.AddCustomCqrs(typeof(Application.SharedKernel.ServiceCollectionExtensions).Assembly);
builder.Services.AddPlatformObservability(builder.Configuration);

// Monitoring JSON endpoints are OFF until authentication and permissions are
// configured by a consuming host/module. No backend dashboard is mapped.
if (builder.Configuration.GetValue<bool>("Observability:EnableFrontendReadApi"))
    builder.Services.AddPlatformObservabilityReadAuthorization();
builder.Services.AddScalarClientGeneration();
builder.Services.AddOpenApi();

// Opt-in standalone messaging host. For transactional business+event outbox,
// configure AddPlatformMessaging<YourModuleDbContext>() against that DbContext.
if (builder.Configuration.GetConnectionString("PlatformMessaging") is { Length: > 0 } messagingConnection)
{
    builder.Services.AddDbContext<PlatformMessagingDbContext>(options =>
        options.UseSqlServer(messagingConnection));
    builder.Services.AddPlatformMessaging<PlatformMessagingDbContext>();
    builder.Services.AddPlatformMessagingHangfire<PlatformMessagingDbContext>(messagingConnection);
}

var app = builder.Build();
app.UseBaseApiExceptionHandling();
app.UsePlatformCorrelation();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapScalarWithClientGeneration();
if (builder.Configuration.GetValue<bool>("Observability:EnableFrontendReadApi"))
    app.MapPlatformObservabilityReadApi();
app.UseHttpsRedirection();
app.Run();
