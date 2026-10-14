using BZ.Howly.Api.Core.Identity;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Identity;

public class StaffProfileTests
{
    private static StaffProfileChanges Valid => new("Bia Analista", "Bia", "bia@howly.example", "(11) 91234-5678", null, null);

    [Fact]
    public void Accepts_a_complete_profile() => StaffProfileChanges.Validate(Valid).Should().BeEmpty();

    [Fact]
    public void Accepts_only_the_name() =>
        StaffProfileChanges.Validate(new StaffProfileChanges("Bia Analista", null, null, null, null, null)).Should().BeEmpty();

    [Theory]
    [InlineData("displayName")]
    [InlineData("nickname")]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("secondaryEmail")]
    [InlineData("secondaryPhone")]
    public void Points_to_the_invalid_field(string field)
    {
        var changes = field switch
        {
            "displayName" => Valid with { DisplayName = " " },
            "nickname" => Valid with { Nickname = new string('b', StaffProfileChanges.MaxNicknameLength + 1) },
            "email" => Valid with { Email = "not-an-email" },
            "phone" => Valid with { Phone = "1234" },
            "secondaryEmail" => Valid with { SecondaryEmail = "nope" },
            _ => Valid with { SecondaryPhone = "(11) 81234-5678" },
        };

        StaffProfileChanges.Validate(changes).Should().ContainKey(field).And.HaveCount(1);
    }

    [Fact]
    public void Normalizes_the_values()
    {
        var normalized = (Valid with { Nickname = "  ", Phone = "(11) 91234-5678", SecondaryEmail = " b@howly.example " }).Normalize();

        normalized.Nickname.Should().BeNull();
        normalized.Phone.Should().Be("11912345678");
        normalized.SecondaryEmail.Should().Be("b@howly.example");
    }
}
