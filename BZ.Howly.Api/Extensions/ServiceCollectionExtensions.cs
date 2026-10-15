using System.Text;
using System.Threading.RateLimiting;
using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.Services.Agencies;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using BZ.Howly.Api.DAO.Repositories;
using BZ.Howly.Api.Services.Customers;
using BZ.Howly.Api.Services.Identity;
using BZ.Howly.Api.Services.Notifications;
using BZ.Howly.Api.Services.Processes;
using BZ.Howly.Api.Services.Tenancy;
using BZ.Howly.Api.Tenancy;
using BZ.Howly.Api.Utils.Security;
using BZ.Howly.Api.Utils.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BZ.Howly.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddHowlyDatabase(this IServiceCollection services)
    {
        services.AddDbContext<HowlyDbContext>((provider, options) =>
            // Steps and timeline load in separate queries instead of one steps × events product.
            options.UseSqlServer(
                provider.GetRequiredService<IConfiguration>().GetConnectionString("Howly"),
                sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        return services;
    }

    /// <summary>
    /// Behind a load balancer the request arrives from it: the original host (which names the portal), scheme and
    /// client address come in X-Forwarded-* headers. Only trusted from loopback unless
    /// <c>ForwardedHeaders:TrustAllProxies</c> is set (a cloud load balancer whose addresses are not fixed).
    /// </summary>
    public static IServiceCollection AddHowlyForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            if (configuration.GetValue("ForwardedHeaders:TrustAllProxies", false))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        return services;
    }

    /// <summary>The consultancy of each request, found from its subdomain (see <see cref="TenantResolutionMiddleware"/>).</summary>
    public static IServiceCollection AddHowlyTenancy(this IServiceCollection services)
    {
        services.AddOptions<TenancyOptions>().BindConfiguration(TenancyOptions.SectionName);
        services.AddSingleton<TenantCache>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantRepository, TenantRepository>();

        return services;
    }

    public static IServiceCollection AddHowlyFileStorage(this IServiceCollection services)
    {
        services.AddOptions<FileStorageOptions>()
            .BindConfiguration(FileStorageOptions.SectionName)
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.RootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.RootPath)));
        // The provider is a setting (Disk in development, AzureBlob with several instances); the use cases see it
        // through TenantFileStorage, which keeps each consultancy in its own folder.
        services.AddSingleton<DiskFileStorage>();
        services.AddSingleton<AzureBlobFileStorage>();
        services.AddScoped<IFileStorage>(provider =>
        {
            IFileStorage inner = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value.Provider == FileStorageProvider.AzureBlob
                ? provider.GetRequiredService<AzureBlobFileStorage>()
                : provider.GetRequiredService<DiskFileStorage>();
            return new TenantFileStorage(inner, provider.GetRequiredService<ITenantContext>());
        });

        return services;
    }

    public static IServiceCollection AddHowlyIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(options =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HowlyDbContext>();

        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProcessTypeRepository, ProcessTypeRepository>();
        services.AddScoped<IProcessRepository, ProcessRepository>();
        services.AddScoped<IProcessDocumentRepository, ProcessDocumentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IJobLock, DatabaseJobLock>();
        services.AddScoped<IAgencyRepository, AgencyRepository>();
        services.AddScoped<IDocumentExampleRepository, DocumentExampleRepository>();
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInternalUserService, InternalUserService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerProfileService, CustomerProfileService>();
        services.AddScoped<IPreferencesService, PreferencesService>();
        services.AddScoped<IAccountProfileService, AccountProfileService>();
        services.AddScoped<IProcessTypeService, ProcessTypeService>();
        services.AddScoped<IProcessService, ProcessService>();
        services.AddScoped<IProcessDocumentService, ProcessDocumentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IOverdueStepService, OverdueStepService>();
        services.AddScoped<IPauseReturnService, PauseReturnService>();
        services.AddScoped<IRenewalReminderService, RenewalReminderService>();
        services.AddScoped<ITenantProfileService, TenantProfileService>();
        services.AddScoped<IAgencyService, AgencyService>();
        services.AddScoped<IDocumentExampleService, DocumentExampleService>();

        return services;
    }

    public static IServiceCollection AddHowlyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Issuer and Jwt:Audience are required.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Jwt:SigningKey must be at least 32 bytes. Set it with user-secrets or an environment variable.")
            .ValidateOnStart();

        services.Configure<RefreshCookieOptions>(configuration.GetSection(RefreshCookieOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    NameClaimType = JwtClaimTypes.Name,
                    RoleClaimType = JwtClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                // A token lives for minutes: without this, a deactivated or demoted person would keep their access until it expires.
                bearer.Events = new JwtBearerEvents { OnTokenValidated = AccountStillMatchesAsync };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Internal, p => p.RequireRole(Roles.Internal))
            .AddPolicy(Policies.Customer, p => p.RequireRole(Roles.Customer))
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));

        return services;
    }

    /// <summary>
    /// Rejects the token (401, so the front refreshes or signs out) when it was issued in another consultancy than the
    /// portal being used, or when its account was deactivated or its roles changed since it was issued. The refresh
    /// then issues a token with the current roles, or fails for an inactive account.
    /// </summary>
    private static async Task AccountStillMatchesAsync(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirst(JwtClaimTypes.Subject)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("Token without a subject.");
            return;
        }

        var portal = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId;
        if (!Guid.TryParse(context.Principal!.FindFirst(JwtClaimTypes.Tenant)?.Value, out var tokenTenant) || tokenTenant != portal)
        {
            context.Fail("The token belongs to another portal.");
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IUserAccountStore>();
        var user = await store.FindByIdAsync(userId, context.HttpContext.RequestAborted);
        var tokenRoles = context.Principal!.FindAll(JwtClaimTypes.Role).Select(c => c.Value).Order().ToList();
        if (user is not { IsActive: true } || !user.Roles.Order().SequenceEqual(tokenRoles))
            context.Fail("The account changed since the token was issued.");
    }

    // Settings below are read through options/DI instead of eagerly, so every configuration
    // source (environment, user-secrets, test overrides) is applied before they are used.

    public static IServiceCollection AddHowlyRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicy, context =>
            {
                var permitLimit = context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("RateLimiting:Login:PermitLimit", 10);

                return RateLimitPartition.GetFixedWindowLimiter(
                    // Per consultancy and address: one portal under attack does not lock the others out.
                    $"{context.RequestServices.GetRequiredService<ITenantContext>().TenantId}:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) });
            });
        });

        return services;
    }

    public static IServiceCollection AddHowlyCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            options.AddDefaultPolicy(policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        return services;
    }
}
