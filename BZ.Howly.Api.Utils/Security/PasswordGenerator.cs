using System.Security.Cryptography;
using BZ.Howly.Api.Core.Interfaces.Service;

namespace BZ.Howly.Api.Utils.Security;

/// <summary>
/// 12-character temporary passwords with at least one character of each class.
/// Characters that look alike (0/O/o, 1/l/I) are left out because the password is read and typed by people.
/// </summary>
public sealed class PasswordGenerator : IPasswordGenerator
{
    private const int Length = 12;
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*-_?";
    private const string All = Upper + Lower + Digits + Symbols;

    public string Generate()
    {
        var characters = new List<char>(Length) { Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols) };
        while (characters.Count < Length)
            characters.Add(Pick(All));

        var shuffled = characters.ToArray();
        RandomNumberGenerator.Shuffle(shuffled.AsSpan());
        return new string(shuffled);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
}
