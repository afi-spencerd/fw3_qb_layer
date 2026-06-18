using Fw3.QbAgent.Core.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Fw3.QbAgent.Host.Errors;

/// <summary>
/// Translates every failure into a structured ProblemDetails response. The ERP branches on the stable
/// string <c>code</c>; QuickBooks' own status (code/severity/message) is surfaced verbatim when present,
/// so a QB error is never swallowed or masquerading as a 200.
/// </summary>
public sealed class QbAgentExceptionHandler : IExceptionHandler
{
    private readonly ILogger<QbAgentExceptionHandler> _logger;

    public QbAgentExceptionHandler(ILogger<QbAgentExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var problem = new ProblemDetails();

        switch (exception)
        {
            case QbAgentException qb:
                problem.Status = qb.HttpStatus;
                problem.Title = qb.Code.ToString();
                problem.Detail = qb.Message;
                problem.Extensions["code"] = qb.Code.ToString();
                if (qb.QbStatus is { } s)
                {
                    problem.Extensions["qbStatusCode"] = s.Code;
                    problem.Extensions["qbStatusSeverity"] = s.Severity;
                    problem.Extensions["qbStatusMessage"] = s.Message;
                }

                // Client/4xx faults are expected; only 5xx warrant an error-level log.
                if (qb.HttpStatus >= 500)
                {
                    _logger.LogError(exception, "QB agent failure ({Code})", qb.Code);
                }
                else
                {
                    _logger.LogWarning("QB agent rejected request ({Code}): {Message}", qb.Code, qb.Message);
                }

                break;

            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                // Client disconnected; nothing useful to send.
                return true;

            default:
                problem.Status = StatusCodes.Status500InternalServerError;
                problem.Title = "Internal";
                problem.Detail = "An unexpected error occurred.";
                problem.Extensions["code"] = "Internal";
                _logger.LogError(exception, "Unhandled exception");
                break;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
