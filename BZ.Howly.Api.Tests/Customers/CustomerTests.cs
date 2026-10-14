using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Customers;

public class CustomerTests
{
    private static readonly Cnpj ValidCnpj = Cnpj.Parse("10000001000190");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid AnalystId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static ContactDetails Bia(bool receivesNotices = true) => new("Bia Souza", "bia@beagle.example", "(11) 91234-5678", receivesNotices);
    private static ContactDetails Caio(bool receivesNotices = false) => new("Caio Lima", "caio@beagle.example", "(21) 3456-7890", receivesNotices);

    private static Customer NewCustomer(params ContactDetails[] contacts) =>
        Customer.Create(ValidCnpj, "Beagle Ltda", contacts.Length > 0 ? contacts : [Bia()], UserId, AnalystId, Now);

    // ---- create ----

    [Fact]
    public void Create_trims_and_normalizes_the_data_and_keeps_audit_data()
    {
        var customer = Customer.Create(ValidCnpj, "  Beagle Industrial Ltda ",
            [new ContactDetails(" Bia Souza ", " bia@beagle.example ", "(11) 91234-5678", true)], UserId, AnalystId, Now);

        customer.Id.Should().NotBeEmpty();
        customer.Cnpj.Should().Be(ValidCnpj);
        customer.CompanyName.Should().Be("Beagle Industrial Ltda");
        var contact = customer.Contacts.Should().ContainSingle().Subject;
        contact.Name.Should().Be("Bia Souza");
        contact.Email.Should().Be("bia@beagle.example");
        contact.Phone.Should().Be("11912345678");
        contact.ReceivesNotices.Should().BeTrue();
        customer.UserId.Should().Be(UserId);
        customer.CreatedBy.Should().Be(AnalystId);
        customer.CreatedAt.Should().Be(Now);
        customer.ConfirmedAt.Should().BeNull();
    }

    [Fact]
    public void Create_keeps_several_contacts_in_order()
    {
        var customer = NewCustomer(Bia(), Caio());

        customer.Contacts.Select(c => c.Name).Should().Equal("Bia Souza", "Caio Lima");
        customer.Contacts.Select(c => c.ReceivesNotices).Should().Equal(true, false);
    }

    [Fact]
    public void Create_throws_when_data_is_invalid()
    {
        var act = () => Customer.Create(ValidCnpj, "", [Bia()], UserId, AnalystId, Now);

        act.Should().Throw<ArgumentException>();
    }

    // ---- validation ----

    [Fact]
    public void Validate_returns_no_errors_for_valid_data()
    {
        Customer.Validate("Beagle Ltda", [Bia(), Caio()]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_requires_the_company_name(string companyName)
    {
        Customer.Validate(companyName, [Bia()]).Should().ContainKey("companyName").And.HaveCount(1);
    }

    [Fact]
    public void Validate_rejects_company_name_longer_than_200_characters()
    {
        Customer.Validate(new string('a', 201), [Bia()]).Should().ContainKey("companyName");
    }

    [Fact]
    public void Validate_requires_at_least_one_contact()
    {
        Customer.Validate("Beagle Ltda", []).Should().ContainKey("contacts");
    }

    [Fact]
    public void Validate_limits_the_number_of_contacts()
    {
        var contacts = Enumerable.Range(0, Customer.MaxContacts + 1)
            .Select(i => new ContactDetails($"Person {i}", $"p{i}@beagle.example", "(11) 91234-5678", true))
            .ToList();

        Customer.Validate("Beagle Ltda", contacts).Should().ContainKey("contacts");
    }

    [Fact]
    public void Validate_requires_someone_to_receive_the_notices()
    {
        Customer.Validate("Beagle Ltda", [Bia(receivesNotices: false), Caio(receivesNotices: false)]).Should().ContainKey("contacts");
    }

    [Theory]
    [InlineData("", "bia@beagle.example", "(11) 91234-5678", "contacts[1].name")]
    [InlineData("Bia", "", "(11) 91234-5678", "contacts[1].email")]
    [InlineData("Bia", "not-an-email", "(11) 91234-5678", "contacts[1].email")]
    [InlineData("Bia", "two@@beagle.example", "(11) 91234-5678", "contacts[1].email")]
    [InlineData("Bia", "bia2@beagle.example", "", "contacts[1].phone")]
    [InlineData("Bia", "bia2@beagle.example", "1234-5678", "contacts[1].phone")]
    public void Validate_points_to_the_invalid_field_of_the_contact(string name, string email, string phone, string field)
    {
        var errors = Customer.Validate("Beagle Ltda", [Caio(receivesNotices: true), new ContactDetails(name, email, phone, false)]);

        errors.Should().ContainKey(field).And.HaveCount(1);
    }

    [Fact]
    public void Validate_rejects_the_same_email_twice()
    {
        var errors = Customer.Validate("Beagle Ltda", [Bia(), Bia() with { Name = "Bia Again", Email = "BIA@beagle.example" }]);

        errors.Should().ContainKey("contacts[1].email");
    }

    // ---- update ----

    [Fact]
    public void Update_replaces_the_company_name_and_contacts_but_keeps_the_cnpj()
    {
        var customer = NewCustomer(Bia());

        customer.Update(" Beagle Industrial S.A. ", [Caio(receivesNotices: true)]);

        customer.CompanyName.Should().Be("Beagle Industrial S.A.");
        customer.Contacts.Should().ContainSingle().Which.Name.Should().Be("Caio Lima");
        customer.Cnpj.Should().Be(ValidCnpj);
    }

    [Fact]
    public void Update_throws_when_data_is_invalid_and_changes_nothing()
    {
        var customer = NewCustomer(Bia());

        var act = () => customer.Update("Beagle Ltda", [Bia() with { Email = "not-an-email" }]);

        act.Should().Throw<ArgumentException>();
        customer.Contacts.Should().ContainSingle().Which.Email.Should().Be("bia@beagle.example");
    }

    // ---- confirmation ----

    [Fact]
    public void Confirm_records_the_time_and_the_reviewed_contacts()
    {
        var customer = NewCustomer(Bia());

        customer.Confirm([Bia(), Caio()], Now.AddDays(1));

        customer.ConfirmedAt.Should().Be(Now.AddDays(1));
        customer.Contacts.Should().HaveCount(2);
        customer.CompanyName.Should().Be("Beagle Ltda");
    }

    [Fact]
    public void Confirm_again_keeps_the_first_confirmation_time()
    {
        var customer = NewCustomer(Bia());
        customer.Confirm([Bia()], Now.AddDays(1));

        customer.Confirm([Caio(receivesNotices: true)], Now.AddDays(2));

        customer.ConfirmedAt.Should().Be(Now.AddDays(1));
        customer.Contacts.Should().ContainSingle().Which.Name.Should().Be("Caio Lima");
    }

    [Fact]
    public void Confirm_throws_when_the_contacts_are_invalid_and_changes_nothing()
    {
        var customer = NewCustomer(Bia());

        var act = () => customer.Confirm([], Now);

        act.Should().Throw<ArgumentException>();
        customer.ConfirmedAt.Should().BeNull();
    }

    // ---- trade name ----

    [Fact]
    public void Shows_the_company_name_until_a_trade_name_is_set()
    {
        var customer = NewCustomer();
        customer.DisplayName.Should().Be("Beagle Ltda");

        customer.SetTradeName("  Beagle Tech ");

        customer.TradeName.Should().Be("Beagle Tech");
        customer.DisplayName.Should().Be("Beagle Tech");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Clearing_the_trade_name_goes_back_to_the_company_name(string? tradeName)
    {
        var customer = NewCustomer();
        customer.SetTradeName("Beagle Tech");

        customer.SetTradeName(tradeName);

        customer.TradeName.Should().BeNull();
        customer.DisplayName.Should().Be("Beagle Ltda");
    }

    [Fact]
    public void Rejects_a_trade_name_longer_than_200_characters()
    {
        var act = () => NewCustomer().SetTradeName(new string('a', 201));

        act.Should().Throw<ArgumentException>();
    }
}
