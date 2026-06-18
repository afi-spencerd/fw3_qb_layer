namespace Fw3.QbAgent.Core.Configuration;

/// <summary>
/// Strongly-typed configuration for the agent, bound from the "QbAgent" section of appsettings.
/// </summary>
public sealed class QbAgentOptions
{
    public const string SectionName = "QbAgent";

    /// <summary>"Fixture" (simulated QuickBooks, no SDK required) or "Live" (real QuickBooks via the SDK).</summary>
    public string Mode { get; set; } = "Fixture";

    /// <summary>Absolute path to the company file, used when <see cref="ConnectToOpenFile"/> is false.</summary>
    public string CompanyFilePath { get; set; } = "";

    /// <summary>
    /// When true, the session binds to whatever company file is currently open in QuickBooks
    /// (qbXML BeginSession with an empty path). When false, it opens <see cref="CompanyFilePath"/>.
    /// </summary>
    public bool ConnectToOpenFile { get; set; } = true;

    /// <summary>Application name shown in QuickBooks' integrated-application authorization list.</summary>
    public string AppName { get; set; } = "FormulaWeb QuickBooks Agent";

    /// <summary>Stable application id used by the SDK authorization grant. Empty is acceptable for a local app.</summary>
    public string AppId { get; set; } = "";

    /// <summary>qbXML schema version to negotiate with the request processor (QuickBooks Enterprise 2024 → 16.0).</summary>
    public string QbXmlVersion { get; set; } = "16.0";

    /// <summary>Directory of qbXML response fixtures used by the Fixture gateway.</summary>
    public string FixturesPath { get; set; } = "fixtures";

    /// <summary>Durable directory where idempotency records are persisted.</summary>
    public string IdempotencyPath { get; set; } = "data/idempotency";

    /// <summary>Durable directory for the rotating qbXML request/response audit log.</summary>
    public string QbXmlAuditPath { get; set; } = "logs/qbxml";

    public bool IsLive => string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);
}
