namespace Fw3.QbAgent.Core.Abstractions;

/// <summary>
/// Gateway-level health, as reported by whichever <see cref="IQuickBooksGateway"/> is active.
/// The Host enriches this with agent version and mode before returning it as a HealthResponse.
/// </summary>
public sealed record QbHealth
{
    public required bool QbReachable { get; init; }
    public required bool CompanyFileOpen { get; init; }
    public string? CompanyFilePath { get; init; }
    public string? QbVersion { get; init; }
    public string? SdkVersion { get; init; }

    /// <summary>Detail when not reachable (e.g. "QBXMLRP2 not registered").</summary>
    public string? Detail { get; init; }
}
