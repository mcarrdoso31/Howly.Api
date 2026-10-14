using BZ.Howly.Api.Utils.Security;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Identity;

public class PasswordGeneratorTests
{
    private readonly PasswordGenerator _generator = new();

    [Fact]
    public void Generate_returns_twelve_characters()
    {
        _generator.Generate().Should().HaveLength(12);
    }

    [Fact]
    public void Generate_always_includes_upper_lower_digit_and_symbol()
    {
        for (var i = 0; i < 500; i++)
        {
            var password = _generator.Generate();

            password.Any(char.IsAsciiLetterUpper).Should().BeTrue("'{0}' needs an uppercase letter", password);
            password.Any(char.IsAsciiLetterLower).Should().BeTrue("'{0}' needs a lowercase letter", password);
            password.Any(char.IsAsciiDigit).Should().BeTrue("'{0}' needs a digit", password);
            password.Any(c => !char.IsAsciiLetterOrDigit(c)).Should().BeTrue("'{0}' needs a symbol", password);
        }
    }

    [Fact]
    public void Generate_never_uses_characters_that_are_easy_to_confuse_when_read_aloud_or_typed()
    {
        var all = string.Concat(Enumerable.Range(0, 500).Select(_ => _generator.Generate()));

        all.Should().NotContainAny("0", "O", "o", "1", "l", "I");
    }

    [Fact]
    public void Generate_returns_different_passwords()
    {
        Enumerable.Range(0, 100).Select(_ => _generator.Generate()).Should().OnlyHaveUniqueItems();
    }
}
