using UrlShortener.Domain;

namespace UrlShortener.WriteService.Application.Ports;

/// <summary>
/// Turns a counter id into a generated short code. The mapping is a keyed bijection, so distinct ids
/// always give distinct codes, yet the codes are neither sequential nor guessable.
/// </summary>
public interface ICodeObfuscator
{
    /// <exception cref="ArgumentOutOfRangeException">The id is outside <c>[0, 62^7)</c>.</exception>
    ShortCode ToCode(long id);
}
