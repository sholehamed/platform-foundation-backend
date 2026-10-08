using Microsoft.Extensions.Caching.Memory;
using Modules.System.Identity.Application.Abstractions;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Middlewares
{
    public sealed class TenantResolutionMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(
            HttpContext http,
            ITenantContext tenantContext,
            ITenantAccessResolver resolver,
            IMemoryCache cache)
        {
            var user = http.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                await next(http);
                return;
            }

            var userId = Guid.Parse(user.FindFirstValue(OpenIddictConstants.Claims.Subject)!);
            var homeTenantId = Guid.Parse(user.FindFirstValue("tenant_id")!);
            var isSuperAdmin = user.IsInRole("SuperAdmin");

            var version = await cache.GetOrCreateAsync($"tenant-access-ver:{userId}",
                _ => Task.FromResult(0L));

            var scope = await cache.GetOrCreateAsync(
                $"tenant-access:{userId}:{version}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    return await resolver.ResolveAsync(
                        userId, homeTenantId, isSuperAdmin, http.RequestAborted);
                });

            Guid? active = null;
            if (http.Request.Headers.TryGetValue("X-Tenant-Id", out var raw)
                && Guid.TryParse(raw, out var parsed)
                && scope.Readable.Contains(parsed))          // اعتبارسنجی اجباری
            {
                active = parsed;
            }

            tenantContext.Initialize(
                userId, homeTenantId, isSuperAdmin,
                scope.Readable, scope.Writable, active ?? homeTenantId);

            await next(http);
        }
    }

}
