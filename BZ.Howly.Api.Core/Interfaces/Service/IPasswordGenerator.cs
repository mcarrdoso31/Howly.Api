namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Generates temporary passwords handed to new users by the company staff.</summary>
public interface IPasswordGenerator
{
    string Generate();
}
