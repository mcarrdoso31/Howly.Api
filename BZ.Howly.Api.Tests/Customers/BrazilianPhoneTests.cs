using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Customers;

public class BrazilianPhoneTests
{
    [Theory]
    [InlineData("(11) 91234-5678", "11912345678")]
    [InlineData("11912345678", "11912345678")]
    [InlineData("(21) 3456-7890", "2134567890")]
    [InlineData(" 21 3456 7890 ", "2134567890")]
    public void TryNormalize_keeps_only_the_digits_of_valid_numbers(string input, string expected)
    {
        BrazilianPhone.TryNormalize(input, out var digits).Should().BeTrue();
        digits.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234-5678")]          // no area code
    [InlineData("(01) 91234-5678")]    // area codes never start with 0
    [InlineData("(10) 91234-5678")]    // nor end with 0
    [InlineData("(11) 81234-5678")]    // 11 digits: mobile numbers start with 9
    [InlineData("(11) 912345-6789")]   // too long
    [InlineData("+55 11 91234-5678")]  // country code is not part of the format
    [InlineData("(11) 9123a-5678")]
    public void TryNormalize_rejects_invalid_numbers(string? input)
    {
        BrazilianPhone.TryNormalize(input, out _).Should().BeFalse();
    }
}
