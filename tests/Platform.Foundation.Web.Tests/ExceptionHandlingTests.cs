using System.Net;
using System.Text.Json;
using Application.SharedKernel.Exceptions;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Web.SharedKernel;
using AppValidationException = Application.SharedKernel.Exceptions.ValidationException;
using Xunit;

namespace Platform.Foundation.Web.Tests;

public sealed class ExceptionHandlingTests
{
    [Theory]
    [InlineData("/badrequest", 400, "request.invalid")]
    [InlineData("/notfound", 404, "customer.not_found")]
    [InlineData("/conflict", 409, "resource.conflict")]
    [InlineData("/business", 422, "business.limit")]
    [InlineData("/unauthorized", 401, "authentication.required")]
    [InlineData("/forbidden", 403, "authorization.forbidden")]
    [InlineData("/unavailable", 503, "service.unavailable")]
    [InlineData("/legacyforbidden", 403, "authorization.forbidden")]
    [InlineData("/legacyservice", 400, "legacy.service_error.7")]
    public async Task Known_failures_produce_stable_problem_details(
        string path, int expectedStatus, string expectedCode)
    {
        using var server = StartServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync(path);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Validation_failure_exposes_field_errors_without_handler_details()
    {
        using var server = StartServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/validation");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.failed", root.GetProperty("code").GetString());
        Assert.Equal("Email is required", root.GetProperty("errors")
            .GetProperty("Email")[0].GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Unexpected_exception_returns_safe_500_without_leaking_secrets()
    {
        using var server = StartServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/unexpected");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("common.unexpected_error", doc.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("SUPER_SECRET_DATABASE_PASSWORD", body);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("/status-401", 401, "authentication.required")]
    [InlineData("/status-403", 403, "authorization.forbidden")]
    public async Task Empty_status_responses_get_the_same_error_contract(
        string path, int expectedStatus, string code)
    {
        using var server = StartServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync(path);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unsupported_accept_header_still_gets_problem_json()
    {
        using var server = StartServer();
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/notfound");
        request.Headers.Accept.ParseAdd("text/plain");

        using var response = await client.SendAsync(request);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("customer.not_found", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static TestServer StartServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services => services.AddBaseApiServices())
            .Configure(app =>
            {
                app.UseBaseApiExceptionHandling();
                app.Run(context =>
                {
                    switch (context.Request.Path.Value)
                    {
                        case "/badrequest":
                            throw AppException.BadRequest("request.invalid", "Request is invalid.");
                        case "/notfound":
                            throw AppException.NotFound("customer.not_found", "Customer not found.");
                        case "/conflict":
                            throw AppException.Conflict("resource.conflict", "Record changed.");
                        case "/business":
                            throw AppException.BusinessRule("business.limit", "Limit reached.");
                        case "/unauthorized":
                            throw AppException.Unauthorized();
                        case "/forbidden":
                            throw AppException.Forbidden();
                        case "/unavailable":
                            throw AppException.Unavailable("service.unavailable", "Service unavailable.");
                        case "/legacyforbidden":
                            throw new ForbiddenAccessException();
                        case "/legacyservice":
                            throw new ServiceExeption("Potentially sensitive text", 7);
                        case "/validation":
                            throw new AppValidationException(
                                [new ValidationFailure("Email", "Email is required")]);
                        case "/unexpected":
                            throw new InvalidOperationException("SUPER_SECRET_DATABASE_PASSWORD");
                        case "/status-401":
                            context.Response.StatusCode = 401;
                            break;
                        case "/status-403":
                            context.Response.StatusCode = 403;
                            break;
                    }
                    return Task.CompletedTask;
                });
            });

        return new TestServer(builder);
    }
}
