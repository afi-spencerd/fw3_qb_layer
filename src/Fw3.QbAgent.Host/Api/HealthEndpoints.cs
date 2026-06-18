using Fw3.QbAgent.Core.Configuration;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Queue;
using Microsoft.Extensions.Options;

namespace Fw3.QbAgent.Host.Api;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (IQbRequestQueue queue, IOptions<QbAgentOptions> options, CancellationToken ct) =>
        {
            // Routed through the same serialized queue as real work, so health reflects the actual
            // path requests take to QuickBooks.
            var health = await queue.EnqueueAsync("Health", gw => gw.CheckHealth(ct), ct);

            return Results.Ok(new HealthResponse
            {
                QbReachable = health.QbReachable,
                CompanyFileOpen = health.CompanyFileOpen,
                CompanyFilePath = health.CompanyFilePath,
                QbVersion = health.QbVersion,
                SdkVersion = health.SdkVersion,
                AgentVersion = AgentInfo.Version,
                Mode = options.Value.Mode,
                Detail = health.Detail,
            });
        })
        .WithName("GetHealth")
        .WithSummary("Agent health and QuickBooks reachability.")
        .Produces<HealthResponse>();

        return app;
    }
}
