using Scalar.ClientGeneration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScalarClientGeneration();
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.MapScalarWithClientGeneration();
app.UseHttpsRedirection();





app.Run();


