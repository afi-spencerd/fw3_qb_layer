namespace Fw3.QbAgent.Core.Configuration;

/// <summary>
/// Authentication settings for the ERP-&gt;agent seam. The slice ships with a shared API key over the
/// internal interface; this is deliberately a single pluggable concern so swapping to mTLS later is a
/// contained change (replace the middleware, keep everything else).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Set false only for local development/tests where the network is already trusted.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Header the ERP sends the shared secret in.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>Accepted shared secrets. Support more than one so keys can be rotated without downtime.</summary>
    public List<string> ApiKeys { get; set; } = new();
}
