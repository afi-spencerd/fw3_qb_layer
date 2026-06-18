namespace Fw3.QbAgent.QuickBooks.Connection;

/// <summary>Session metadata discovered when a QuickBooks connection is opened.</summary>
public sealed record QbSessionInfo(string? CompanyFilePath, string? QbVersion, string? SdkVersion);

/// <summary>
/// A single QuickBooks session: OpenConnection + BeginSession on <see cref="Open"/>, and
/// EndSession + CloseConnection on <see cref="IDisposable.Dispose"/>. Always created and used on the
/// STA worker thread. Wrapping it this way guarantees the session is torn down even when a request throws.
/// </summary>
public interface IQbConnection : IDisposable
{
    /// <summary>Open the connection and begin the session. Returns discovered session info.</summary>
    QbSessionInfo Open();

    /// <summary>Submit a qbXML request string and return the qbXML response string.</summary>
    string ProcessRequest(string requestXml);
}

/// <summary>Creates QuickBooks connections. Swapping the live transport later means swapping this.</summary>
public interface IQbConnectionFactory
{
    IQbConnection Create();
}
