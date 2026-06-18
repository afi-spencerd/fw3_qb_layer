namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// Response for <c>GET /health</c>. Lets the ERP confirm the agent can reach QuickBooks and see the
/// company file before it attempts real work.
/// </summary>
public sealed record HealthResponse
{
    /// <summary>True if the agent opened a QuickBooks connection/session successfully.</summary>
    public required bool QbReachable { get; init; }

    /// <summary>True if a company file is open/available to the session.</summary>
    public required bool CompanyFileOpen { get; init; }

    /// <summary>Path of the company file the session is bound to, if known.</summary>
    public string? CompanyFilePath { get; init; }

    /// <summary>QuickBooks Desktop product/version string reported by the SDK, if reachable.</summary>
    public string? QbVersion { get; init; }

    /// <summary>qbXML/SDK version negotiated with the request processor, if reachable.</summary>
    public string? SdkVersion { get; init; }

    /// <summary>The agent's own version (from assembly metadata).</summary>
    public required string AgentVersion { get; init; }

    /// <summary>Active gateway mode: "Fixture" (simulated) or "Live" (real QuickBooks).</summary>
    public required string Mode { get; init; }

    /// <summary>Free-text detail when <see cref="QbReachable"/> is false (e.g. SDK not installed).</summary>
    public string? Detail { get; init; }
}
