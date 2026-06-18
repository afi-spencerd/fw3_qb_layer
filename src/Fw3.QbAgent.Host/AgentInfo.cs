using System.Reflection;
using System.Text.Json;

namespace Fw3.QbAgent.Host;

/// <summary>Process-wide constants for the agent.</summary>
public static class AgentInfo
{
    /// <summary>Agent version from assembly metadata (set centrally in Directory.Build.props).</summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

    /// <summary>JSON options shared between endpoint responses and idempotency replay (de)serialization,
    /// so a replayed response is byte-identical to the original.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
}
