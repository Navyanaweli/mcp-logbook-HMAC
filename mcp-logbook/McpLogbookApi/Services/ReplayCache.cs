using System.Collections.Concurrent;

namespace McpLogbookApi.Services;

// Replay protection: once a key has been registered, it can't be registered again until its
// entry expires. Shared shape used by both HmacAuthenticationMiddleware (keyed on the
// external client's signature -- each client's own unique secret already makes that safe)
// and AdminHmacAuthenticationMiddleware (keyed on the client-generated X-Nonce, since every
// admin request shares one secret and the signature alone isn't guaranteed unique). Each
// middleware owns its own instance, since they're constructed once per app lifetime just
// like any other conventional ASP.NET Core middleware.
public class ReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedKeys = new();

    // Returns false if the key is already registered (and not yet expired).
    public bool TryRegister(string key, TimeSpan expiry)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (usedKey, expiresAt) in _usedKeys)
        {
            if (expiresAt < now)
                _usedKeys.TryRemove(usedKey, out _);
        }

        return _usedKeys.TryAdd(key, now + expiry);
    }
}
