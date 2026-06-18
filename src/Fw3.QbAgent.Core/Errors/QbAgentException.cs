using Fw3.QbAgent.Core.Abstractions;

namespace Fw3.QbAgent.Core.Errors;

/// <summary>
/// Stable machine-readable error categories. The HTTP layer maps these to status codes and the ERP
/// branches on the string code, not the message. Add new codes deliberately — the ERP depends on them.
/// </summary>
public enum QbErrorCode
{
    /// <summary>Request was structurally invalid (missing required field, bad value).</summary>
    Validation,

    /// <summary>The requested resource does not exist in QuickBooks.</summary>
    NotFound,

    /// <summary>An idempotency key was reused with a different request body.</summary>
    IdempotencyConflict,

    /// <summary>QuickBooks accepted the request but rejected the operation (statusSeverity=Error).</summary>
    QbRequestFailed,

    /// <summary>The QuickBooks SDK could not be reached (not installed, not running, not authorized).</summary>
    QbUnreachable,

    /// <summary>No company file is open/available for the session.</summary>
    CompanyFileNotOpen,

    /// <summary>An unexpected internal failure.</summary>
    Internal,
}

/// <summary>
/// The agent's single structured failure type. Carries everything the HTTP layer needs to produce a
/// meaningful, non-200 response and everything the audit log needs to explain what QuickBooks said.
/// </summary>
public sealed class QbAgentException : Exception
{
    public QbErrorCode Code { get; }

    /// <summary>The HTTP status the Host should return for this failure.</summary>
    public int HttpStatus { get; }

    /// <summary>The raw QuickBooks status, when the failure originated from a qbXML response.</summary>
    public QbStatus? QbStatus { get; }

    public QbAgentException(QbErrorCode code, int httpStatus, string message, QbStatus? qbStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        HttpStatus = httpStatus;
        QbStatus = qbStatus;
    }

    // Plain HTTP status ints keep Core free of any ASP.NET Core dependency.
    public static QbAgentException Validation(string message) =>
        new(QbErrorCode.Validation, 400, message);

    public static QbAgentException NotFound(string message) =>
        new(QbErrorCode.NotFound, 404, message);

    public static QbAgentException IdempotencyConflict(string message) =>
        new(QbErrorCode.IdempotencyConflict, 409, message);

    /// <summary>QuickBooks processed the request but returned an Error severity. 422: the request was
    /// understood but the operation could not be completed.</summary>
    public static QbAgentException QbRequestFailed(QbStatus status) =>
        new(QbErrorCode.QbRequestFailed, 422,
            $"QuickBooks rejected the request (statusCode {status.Code}): {status.Message}", status);

    public static QbAgentException Unreachable(string message, Exception? inner = null) =>
        new(QbErrorCode.QbUnreachable, 503, message, inner: inner);

    public static QbAgentException CompanyFileNotOpen(string message) =>
        new(QbErrorCode.CompanyFileNotOpen, 503, message);
}
