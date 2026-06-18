using System.Security.Cryptography;
using System.Text;
using Fw3.QbAgent.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Fw3.QbAgent.Host.Auth;

/// <summary>
/// Shared-secret authentication for the ERP-&gt;agent seam. Deliberately the single place auth lives, so
/// moving to mTLS later is a contained change: replace this middleware, leave the rest untouched.
/// <para>
/// Health and the OpenAPI document/UI are exempt so monitoring and contract browsing work without a key.
/// </para>
/// </summary>
public sealed class ApiKeyMiddleware
{
    private static readonly string[] ExemptPrefixes = ["/health", "/openapi.json", "/swagger"];

    private readonly RequestDelegate _next;
    private readonly AuthOptions _options;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<AuthOptions> options, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var presented = context.Request.Headers[_options.HeaderName].ToString();
        if (string.IsNullOrEmpty(presented) || !IsKnownKey(presented))
        {
            _logger.LogWarning("Rejected request to {Path}: missing or invalid API key.", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "Unauthorized",
                detail = $"A valid '{_options.HeaderName}' header is required.",
            });
            return;
        }

        await _next(context);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private bool IsKnownKey(string presented)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);

        // Constant-time comparison against each configured key to avoid leaking length/content via timing.
        var match = false;
        foreach (var key in _options.ApiKeys)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            match |= CryptographicOperations.FixedTimeEquals(presentedBytes, keyBytes);
        }

        return match;
    }
}
