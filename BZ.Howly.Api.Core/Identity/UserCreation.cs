namespace BZ.Howly.Api.Core.Identity;

/// <summary>A user created by the company staff. It always starts with a temporary password that must be changed.</summary>
/// <param name="MustConfirmProfile">Customers also confirm their data on first access.</param>
public sealed record NewUserAccount(string Username, string DisplayName, string Role, bool MustConfirmProfile = false);

public sealed record UserCreationResult(UserAccount? Account, bool UsernameTaken, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Account is not null;

    public static UserCreationResult Created(UserAccount account) => new(account, false, []);
    public static readonly UserCreationResult Taken = new(null, true, ["Username is already in use."]);
    public static UserCreationResult Failed(IReadOnlyList<string> errors) => new(null, false, errors);
}
