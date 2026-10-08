using Scalar.ClientGeneration;
using Web.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBaseApiServices();
builder.Services.AddScalarClientGeneration();
builder.Services.AddOpenApi();

var app = builder.Build();

// Must run early so errors raised by following middleware/endpoints are mapped.
app.UseBaseApiExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapScalarWithClientGeneration();
app.UseHttpsRedirection();
app.Run();
