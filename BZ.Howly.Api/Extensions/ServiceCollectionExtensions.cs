using System.Text;
using System.Threading.RateLimiting;
using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using BZ.Howly.Api.DAO.Repositories;
using BZ.Howly.Api.Services.Customers;
using BZ.Howly.Api.Services.Identity;
using BZ.Howly.Api.Services.Notifications;
using BZ.Howly.Api.Services.Processes;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Utils.Security;
using BZ.Howly.Api.Utils.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
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
            options.UseSqlServer(provider.GetRequiredService<IConfiguration>().GetConnectionString("Howly")));

        return services;
    }

    public static IServiceCollection AddHowlyFileStorage(this IServiceCollection services)
    {
        services.AddOptions<FileStorageOptions>()
            .BindConfiguration(FileStorageOptions.SectionName)
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.RootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.RootPath)));
        services.AddSingleton<IFileStorage, DiskFileStorage>();

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
    /// Rejects the token (401, so the front refreshes or signs out) when its account was deactivated or its roles changed
    /// since it was issued. The refresh then issues a token with the current roles, or fails for an inactive account.
    /// </summary>
    private static async Task AccountStillMatchesAsync(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirst(JwtClaimTypes.Subject)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("Token without a subject.");
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
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
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
