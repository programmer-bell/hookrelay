using System.Security.Cryptography;

namespace HookRelay.Features.Endpoints;

public static class EndpointTokens
{
    private const int SlugLength = 10;
    private const string SlugAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string NewSlug()
    {
        var characters = new char[SlugLength];
        for (var i = 0; i < SlugLength; i++)
        {
            characters[i] = SlugAlphabet[RandomNumberGenerator.GetInt32(SlugAlphabet.Length)];
        }

        return new string(characters);
    }

    public static string NewSigningSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
