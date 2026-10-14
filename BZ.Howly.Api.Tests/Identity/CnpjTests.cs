using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Identity;

public class CnpjTests
{
    [Theory]
    [InlineData("11222333000181", "11222333000181")]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData(" 11.222.333/0001-81 ", "11222333000181")]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")]
    [InlineData("12abc34501de35", "12ABC34501DE35")]
    public void TryParse_accepts_valid_cnpj_and_keeps_only_normalized_characters(string input, string expected)
    {
        var parsed = Cnpj.TryParse(input, out var cnpj);

        parsed.Should().BeTrue();
        cnpj!.Value.Should().Be(expected);
    }

    [Fact]
    public void Parse_returns_normalized_cnpj()
    {
        Cnpj.Parse("11.222.333/0001-81").Value.Should().Be("11222333000181");
    }

    [Fact]
    public void Parse_throws_for_invalid_cnpj()
    {
        var act = () => Cnpj.Parse("11222333000182");

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("11222333000182")]     // wrong second check digit
    [InlineData("11222333000171")]     // wrong first check digit
    [InlineData("12ABC34501DE36")]     // wrong check digit (alphanumeric)
    [InlineData("1122233300018")]      // 13 characters
    [InlineData("112223330001811")]    // 15 characters
    [InlineData("00000000000000")]     // repeated digits pass the checksum but are invalid
    [InlineData("12ABC34501DEAB")]     // check digits must be numeric
    [InlineData("11222333#00181")]     // invalid character
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_rejects_invalid_cnpj(string? input)
    {
        var parsed = Cnpj.TryParse(input, out var cnpj);

        parsed.Should().BeFalse();
        cnpj.Should().BeNull();
    }
}
