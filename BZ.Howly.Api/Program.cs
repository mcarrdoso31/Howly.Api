using System.Text.Json.Serialization;
using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.ErrorHandling;
using BZ.Howly.Api.Extensions;
using BZ.Howly.Api.Tenancy;
using BZ.Howly.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<PasswordChangeRequiredFilter>();
    options.Filters.Add<ProfileConfirmationRequiredFilter>();
})
// Enums travel as their names ("Agency", "InProgress"): readable for clients, and unknown names are rejected.
.AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();

// One answer format for every failure (problem details), in this order: the client gave up (499, quiet),
// exceptions with a meaning (401/403/404/409/400…), anything else (500 with the trace id).
builder.Services.AddExceptionHandler<CanceledRequestHandler>();
builder.Services.AddExceptionHandler<HowlyExceptionHandler>();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services
    .AddHowlyForwardedHeaders(builder.Configuration)
    .AddHowlyTenancy()
    .AddHowlyDatabase()
    .AddHowlyIdentity()
    .AddHowlyFileStorage()
    .AddHowlyAuthentication(builder.Configuration)
    .AddHowlyRateLimiting()
    .AddHowlyCors();

// For the load balancer: an instance whose database is unreachable is taken out of rotation.
builder.Services.AddHealthChecks().AddDbContextCheck<HowlyDbContext>();
builder.Services.AddSingleton<OverdueStepsRunner>();
builder.Services.AddHostedService<OverdueStepsWorker>();

var app = builder.Build();

// First: behind a load balancer, the real host (which names the portal), scheme and client address.
app.UseForwardedHeaders();
// In every environment, so the client never gets an HTML error page.
app.UseExceptionHandler();
// Error responses without a body (404 of an unknown route, 405, the 401 of the bearer challenge) get problem details too.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    // Production applies migrations as a deploy step (migration bundle): N instances starting together must not race.
    await app.SeedDevelopmentDataAsync();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
// Before anything reads data: which consultancy this request belongs to.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
