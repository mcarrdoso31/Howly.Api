using BZ.Howly.Api.Core.Identity;

namespace BZ.Howly.Api.Core.Interfaces.Service;

public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user);
    IssuedRefreshToken CreateRefreshToken();
    string HashRefreshToken(string value);
}
