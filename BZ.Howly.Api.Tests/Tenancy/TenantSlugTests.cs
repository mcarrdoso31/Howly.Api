using BZ.Howly.Api.Tenancy;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Tenancy;

public class TenantSlugTests
{
    private static readonly TenancyOptions Options = new() { BaseDomains = ["howly.app", "localhost"], FallbackSlug = "bia" };

    [Theory]
    [InlineData("bia.howly.app", "bia")]
    [InlineData("BIA.Howly.App", "bia")]
    [InlineData("claude-ai.localhost", "claude-ai")]
    public void The_subdomain_names_the_portal(string host, string expected) =>
        TenantResolutionMiddleware.SlugOf(host, Options, allowFallback: false).Should().Be(expected);

    [Theory]
    [InlineData("a.b.howly.app")]
    [InlineData("evil.com")]
    [InlineData("127.0.0.1")]
    [InlineData("-bad-.howly.app")]
    public void Anything_else_is_no_portal(string host) =>
        TenantResolutionMiddleware.SlugOf(host, Options, allowFallback: true).Should().BeNull();

    [Fact]
    public void The_bare_domain_falls_back_only_when_allowed()
    {
        TenantResolutionMiddleware.SlugOf("localhost", Options, allowFallback: true).Should().Be("bia");
        TenantResolutionMiddleware.SlugOf("howly.app", Options, allowFallback: false).Should().BeNull("production never opens a default portal");
    }
}
