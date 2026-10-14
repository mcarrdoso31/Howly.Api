using System.Text.Json;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared.Exceptions;
using BZ.Howly.Api.ErrorHandling;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BZ.Howly.Api.Tests.ErrorHandling;

public class ExceptionHandlerTests
{
    /// <summary>What the handlers need from the app: logging (it brings options) and the problem details writer.</summary>
    private static readonly ServiceProvider Services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();

    private static DefaultHttpContext Context(bool clientGone = false)
    {
        var context = new DefaultHttpContext { RequestServices = Services };
        context.Response.Body = new MemoryStream();
        if (clientGone)
        {
            var aborted = new CancellationTokenSource();
            aborted.Cancel();
            context.RequestAborted = aborted.Token;
        }
        return context;
    }

    private static async Task<JsonElement> BodyOf(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
    }

    private static IHostEnvironment Environment(string name) => new FakeEnvironment { EnvironmentName = name };

    // ---- canceled requests ----

    [Fact]
    public async Task A_request_the_client_gave_up_on_ends_quietly_with_499()
    {
        var context = Context(clientGone: true);

        var handled = await new CanceledRequestHandler(NullLogger<CanceledRequestHandler>.Instance)
            .TryHandleAsync(context, new TaskCanceledException(), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task A_cancellation_the_client_did_not_ask_for_is_not_treated_as_one()
    {
        var context = Context(clientGone: false);

        var handled = await new CanceledRequestHandler(NullLogger<CanceledRequestHandler>.Instance)
            .TryHandleAsync(context, new TaskCanceledException(), CancellationToken.None);

        handled.Should().BeFalse();
    }

    // ---- our exceptions ----

    [Theory]
    [InlineData(typeof(UnauthorizedException), 401)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ConflictException), 409)]
    public async Task Each_exception_of_ours_becomes_its_status(Type exceptionType, int status)
    {
        var context = Context();
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Something specific happened.", "some_code")!;

        var handled = await Handler().TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(status);
        var body = await BodyOf(context);
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("title").GetString().Should().Be("Something specific happened.");
        body.GetProperty("code").GetString().Should().Be("some_code");
    }

    [Fact]
    public async Task Process_rules_are_conflicts()
    {
        var context = Context();

        await Handler().TryHandleAsync(context, new ProcessRuleException("Only paused processes can be resumed."), CancellationToken.None);

        context.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Validation_errors_point_to_the_fields()
    {
        var context = Context();
        var exception = new ValidationException(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });

        await Handler().TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(400);
        (await BodyOf(context)).GetProperty("errors").GetProperty("title")[0].GetString().Should().Be("Title is required.");
    }

    [Fact]
    public async Task Requests_the_server_refused_keep_their_status()
    {
        var context = Context();

        await Handler().TryHandleAsync(context, new BadHttpRequestException("Request body too large.", 413), CancellationToken.None);

        context.Response.StatusCode.Should().Be(413);
    }

    [Fact]
    public async Task Other_exceptions_are_left_to_the_next_handler()
    {
        (await Handler().TryHandleAsync(Context(), new InvalidOperationException("boom"), CancellationToken.None)).Should().BeFalse();
    }

    // ---- everything else ----

    [Fact]
    public async Task Unexpected_errors_become_500_without_leaking_details_in_production()
    {
        var context = Context();

        var handled = await Unexpected("Production").TryHandleAsync(context, new InvalidOperationException("Connection string has password=123"), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(500);
        var body = await BodyOf(context);
        body.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
        body.GetRawText().Should().NotContain("password=123");
        body.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unexpected_errors_show_the_message_while_developing()
    {
        var context = Context();

        await Unexpected("Development").TryHandleAsync(context, new InvalidOperationException("Something broke here"), CancellationToken.None);

        (await BodyOf(context)).GetProperty("detail").GetString().Should().Be("Something broke here");
    }

    private static HowlyExceptionHandler Handler() =>
        new(Services.GetRequiredService<IProblemDetailsService>(), NullLogger<HowlyExceptionHandler>.Instance);

    private static UnexpectedExceptionHandler Unexpected(string environment) =>
        new(Services.GetRequiredService<IProblemDetailsService>(), NullLogger<UnexpectedExceptionHandler>.Instance, Environment(environment));

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Howly";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
