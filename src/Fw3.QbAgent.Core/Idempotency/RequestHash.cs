using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fw3.QbAgent.Core.Idempotency;

/// <summary>
/// Computes a stable hash of a request body, used to distinguish a genuine replay (same key, same
/// body) from a key-reuse conflict (same key, different body).
/// </summary>
public static class RequestHash
{
    // Property order is stable for a given type, so serializing the typed DTO yields a stable string.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Compute<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(bytes);
    }
}
