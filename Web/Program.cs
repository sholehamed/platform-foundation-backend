using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.ClientGeneration;
using Web.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBaseApiServices();
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

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapScalarWithClientGeneration();
app.UseHttpsRedirection();
app.Run();
