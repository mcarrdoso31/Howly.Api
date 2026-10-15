using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Identity;

/// <summary>A customer's document: the CPF of a person or the CNPJ of a company.</summary>
public class TaxIdTests
{
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("52998224725", "52998224725")]
    [InlineData(" 529 982 247 25 ", "52998224725")]
    public void A_valid_cpf_is_kept_with_digits_only(string input, string expected)
    {
        TaxId.TryParse(input, out var document).Should().BeTrue();

        document!.Value.Should().Be(expected);
        document.Kind.Should().Be(TaxIdKind.Cpf);
    }

    [Theory]
    [InlineData("529.982.247-24")]   // wrong check digit
    [InlineData("111.111.111-11")]   // repeated digits pass the check, but are not real
    [InlineData("5299822472")]       // too short
    [InlineData("52998224A25")]      // CPFs have no letters
    [InlineData("")]
    [InlineData(null)]
    public void An_invalid_cpf_is_refused(string? input)
    {
        TaxId.TryParse(input, out var document).Should().BeFalse();
        document.Should().BeNull();
    }

    [Theory]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")]
    public void A_valid_cnpj_is_a_company_document(string input, string expected)
    {
        TaxId.TryParse(input, out var document).Should().BeTrue();

        document!.Value.Should().Be(expected);
        document.Kind.Should().Be(TaxIdKind.Cnpj);
    }

    [Fact]
    public void Parse_throws_for_an_invalid_document()
    {
        var act = () => TaxId.Parse("123");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Equal_documents_are_equal()
    {
        TaxId.Parse("529.982.247-25").Should().Be(TaxId.Parse("52998224725"));
    }
}
