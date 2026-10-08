using Application.SharedKernel;
using Infrastructure.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.ApiResources.Services;
using Modules.System.Identity.Application.Mapping;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Modules.System.Identity.Web.Middlewares;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Reflection;
namespace Modules.System.Identity
{
    public static class DependencyInjections
    {
        public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddScoped<ICurrentUser, CurrentUser>();

            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<ITenantAccessResolver, TenantAccessResolver>();
            services.AddScoped<TenantSaveChangesInterceptor>();
            services.AddBaseInfrastructureServices<IdentityDbContext>(configuration, "Identity", (sp,options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
            options.UseOpenIddict();
        });
            services.AddScoped<ApiResourceSyncService, ApiResourceSyncService>();
            var assembly = Assembly.GetExecutingAssembly();
            services.AddCustomCqrs<AppMappingProfile>(assembly); services.AddDataProtection();
            services.AddScoped<CaptchaService>();
            services.AddScoped<IIdentityDbContext>(provider =>
           provider.GetRequiredService<IdentityDbContext>());
            services.AddScoped<IPermissionResolver, RoutePermissionResolver>();
            services.AddScoped<IPermissionChecker, DbPermissionChecker>();
            services.AddOpenIddict()

                .AddCore(options =>
                {
                    options.UseEntityFrameworkCore()
                           .UseDbContext<IdentityDbContext>();
                })

                .AddServer(options =>
                {
                    options.SetTokenEndpointUris("api/system/Auth/login");
                    options.SetAuthorizationEndpointUris("api/connect/authorize");
                    options.SetEndSessionEndpointUris("api/system/Auth/logout");
                    options.SetUserInfoEndpointUris("api/system/Auth/userinfo");

                    options.AllowPasswordFlow();
                    options.AllowRefreshTokenFlow();
                    options.AllowAuthorizationCodeFlow()
                           .RequireProofKeyForCodeExchange();

                    options.AcceptAnonymousClients();

                    options.RegisterScopes("api", "profile", "email", "roles", "permissions", OpenIddictConstants.Scopes.OfflineAccess);

                    options.SetAccessTokenLifetime(TimeSpan.FromMinutes(30));
                    options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));

                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();

                    options.UseAspNetCore()
                           .EnableTokenEndpointPassthrough()
                           .EnableAuthorizationEndpointPassthrough()
                           .EnableEndSessionEndpointPassthrough()
                           .EnableUserInfoEndpointPassthrough();

                    options.DisableAccessTokenEncryption(); // optional for JWT readability
                })

                .AddValidation(options =>
                {
                    options.UseLocalServer();
                    options.UseAspNetCore();
                });
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });
            services.AddScoped<ISeedContributor, DefaultIdentitySeedContributor>();
            services.AddAuthorization();
            return services;
        }
        public static WebApplication UseIdentityModule(this WebApplication app,IConfiguration configuration)
        {
            var assembly = typeof(IdentityDbContext).Assembly;

            app.UseAuthentication();
            app.UseAuthorization();
            //DbSeeder.SeedAsync(app.Services).Wait();
            //app.UseMiddleware<PermissionMiddleware>();
            app.UseMiddleware<TenantResolutionMiddleware>();
            app.MapEndpoints($"{configuration["BackendPrefix"]}System");

            return app;
        }
    }
}
