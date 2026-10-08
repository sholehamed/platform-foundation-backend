using System.Net;
using Application.SharedKernel.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Web.SharedKernel.Observability;
using Xunit;

namespace Platform.Foundation.Observability.Tests;

/// <summary>
/// Contract and permission checks for the three Angular historical endpoints.
/// Uses the existing test authentication handler; no production auth bypass.
/// </summary>
public sealed class TelemetryHistoryApiTests
{
    private const string Trace = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("/api/platform/observability/records", "", HttpStatusCode.Unauthorized)]
    [InlineData("/api/platform/observability/records", "no-permission", HttpStatusCode.Forbidden)]
    [InlineData("/api/platform/observability/records", "allowed", HttpStatusCode.OK)]
    [InlineData("/api/platform/observability/summary", "allowed", HttpStatusCode.OK)]
    [InlineData("/api/platform/observability/traces/" + Trace, "allowed", HttpStatusCode.OK)]
    [InlineData("/api/platform/observability/records?pageSize=101", "allowed", HttpStatusCode.BadRequest)]
    [InlineData("/api/platform/observability/records?page=0", "allowed", HttpStatusCode.BadRequest)]
    [InlineData("/api/platform/observability/records?kind=99", "allowed", HttpStatusCode.BadRequest)]
    [InlineData("/api/platform/observability/traces/not-a-trace-id", "allowed", HttpStatusCode.BadRequest)]
    public async Task History_API_enforces_permission_and_query_bounds(
        string path, string identity, HttpStatusCode expected)
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity.Length > 0) request.Headers.Add("X-Test-Identity", identity);

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Query_types_and_page_shape_are_stable_for_angular()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Identity", "allowed");

        var response = await client.GetAsync(
            "/api/platform/observability/records?kind=Log&source=Modules.Identity&page=2&pageSize=10");
        response.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal(3, root.GetProperty("total").GetInt64());
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, ObservabilityTests.TestAuthHandler>(
                        "Test", _ => { });
                services.AddPlatformObservabilityReadAuthorization();
                services.AddSingleton<ITelemetryHistoryReader, FakeReader>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapPlatformObservabilityHistoryApi());
            });
        return new TestServer(builder);
    }

    private sealed class FakeReader : ITelemetryHistoryReader
    {
        public Task<TelemetryPage<TelemetryItem>> SearchAsync(
            TelemetryQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new TelemetryPage<TelemetryItem>(
                [], 3, query.Page, query.PageSize));

        public Task<TelemetryTraceResult> GetTraceAsync(
            string traceId, DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TelemetryTraceResult(traceId, 0, false, []));

        public Task<TelemetrySummary> GetSummaryAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => Task.FromResult(new TelemetrySummary(
                from, to, 0, 0, 0, 0, 0, 0, 0, false, []));
    }
}
