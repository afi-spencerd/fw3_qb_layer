using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Configuration;
using Fw3.QbAgent.QuickBooks.Connection;
using Fw3.QbAgent.QuickBooks.Gateways;
using Microsoft.Extensions.DependencyInjection;

namespace Fw3.QbAgent.QuickBooks;

public static class QuickBooksServiceCollectionExtensions
{
    /// <summary>
    /// Register the active QuickBooks gateway. "Live" wires the real QBXMLRP2 connection; anything else
    /// ("Fixture") wires the simulated gateway, which needs no SDK and no company file.
    /// </summary>
    public static IServiceCollection AddQuickBooksGateway(this IServiceCollection services, QbAgentOptions options)
    {
        if (options.IsLive)
        {
            services.AddSingleton<IQbConnectionFactory, QbxmlRp2ConnectionFactory>();
            services.AddSingleton<IQuickBooksGateway, LiveQuickBooksGateway>();
        }
        else
        {
            services.AddSingleton<IQuickBooksGateway, FixtureQuickBooksGateway>();
        }

        return services;
    }
}
