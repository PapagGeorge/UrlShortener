namespace UrlShortener.WriteService.Application.Ports;

/// <summary>The counter went beyond the 62^7 code space. Fails loudly instead of wrapping or producing invalid codes.</summary>
public sealed class CounterExhaustedException()
    : InvalidOperationException("The short code space (62^7 ids) is exhausted.");
