using System.Runtime.InteropServices;
using Fw3.QbAgent.Core.Configuration;
using Fw3.QbAgent.Core.Errors;
using Microsoft.Extensions.Options;

namespace Fw3.QbAgent.QuickBooks.Connection;

/// <summary>
/// Live QuickBooks connection over the QBXMLRP2 Request Processor COM component, using late binding so
/// this assembly compiles and runs without the SDK installed at build time. The processor is the same
/// SDK layer QBFC sits on top of; we drive it with the qbXML strings produced by <c>CustomerMapper</c>,
/// which keeps a single mapping codepath and lets us log the exact wire content.
/// <para>
/// NOTE: This is enabled by config (QbAgent:Mode = Live) and validated against the live company file as
/// part of the SDK-install step. Until QBSDK16 is installed, <see cref="Open"/> reports a clear,
/// structured "SDK not installed" failure rather than crashing.
/// </para>
/// </summary>
public sealed class QbxmlRp2Connection : IQbConnection
{
    // QBXMLRP2 enum values (passed as ints under late binding).
    private const int ConnectionTypeLocalQbd = 1; // localQBD
    private const int FileModeDoNotCare = 2;       // qbFileOpenDoNotCare

    private readonly QbAgentOptions _options;
    private object? _rp;       // QBXMLRP2.RequestProcessor COM instance
    private string? _ticket;   // session ticket from BeginSession
    private bool _connectionOpen;

    public QbxmlRp2Connection(QbAgentOptions options) => _options = options;

    public QbSessionInfo Open()
    {
        var progId = "QBXMLRP2.RequestProcessor";
        var type = Type.GetTypeFromProgID(progId);
        if (type is null)
        {
            throw QbAgentException.Unreachable(
                $"The QuickBooks SDK request processor ('{progId}') is not registered. Install QuickBooks Desktop SDK 16.0 (QBFC16).");
        }

        try
        {
            dynamic rp = Activator.CreateInstance(type)
                ?? throw QbAgentException.Unreachable($"Could not create COM instance of '{progId}'.");
            _rp = rp;

            // OpenConnection2(appID, appName, connectionType)
            rp.OpenConnection2(_options.AppId ?? string.Empty, _options.AppName, ConnectionTypeLocalQbd);
            _connectionOpen = true;

            // BeginSession(companyFile, fileMode). Empty path => use the file currently open in QuickBooks.
            var companyFile = _options.ConnectToOpenFile ? string.Empty : _options.CompanyFilePath;
            _ticket = rp.BeginSession(companyFile, FileModeDoNotCare);

            string? sdkVersion = TryGetNegotiatedVersion(rp, _ticket);
            var resolvedFile = string.IsNullOrEmpty(companyFile) ? "(file open in QuickBooks)" : companyFile;
            return new QbSessionInfo(resolvedFile, QbVersion: null, SdkVersion: sdkVersion);
        }
        catch (QbAgentException)
        {
            Dispose();
            throw;
        }
        catch (COMException ex)
        {
            Dispose();
            throw QbAgentException.Unreachable(
                $"QuickBooks rejected the session (HRESULT 0x{ex.HResult:X8}). Check that QuickBooks is running, the company file is open, and this app is authorized in QuickBooks.",
                ex);
        }
    }

    public string ProcessRequest(string requestXml)
    {
        if (_rp is null || _ticket is null)
        {
            throw QbAgentException.Unreachable("ProcessRequest called before the QuickBooks session was opened.");
        }

        try
        {
            dynamic rp = _rp;
            return rp.ProcessRequest(_ticket, requestXml);
        }
        catch (COMException ex)
        {
            throw QbAgentException.Unreachable(
                $"QuickBooks failed to process the request (HRESULT 0x{ex.HResult:X8}).", ex);
        }
    }

    public void Dispose()
    {
        if (_rp is null)
        {
            return;
        }

        dynamic rp = _rp;
        try
        {
            if (_ticket is not null)
            {
                rp.EndSession(_ticket);
            }

            if (_connectionOpen)
            {
                rp.CloseConnection();
            }
        }
        catch
        {
            // Teardown is best-effort; never let cleanup mask the original outcome.
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                Marshal.FinalReleaseComObject(_rp);
            }

            _rp = null;
            _ticket = null;
            _connectionOpen = false;
        }
    }

    private static string? TryGetNegotiatedVersion(dynamic rp, string? ticket)
    {
        try
        {
            // QBXMLVersionsForSession returns the qbXML versions this company file supports.
            object[]? versions = rp.QBXMLVersionsForSession(ticket);
            return versions is { Length: > 0 } ? versions[^1]?.ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Factory for the live QBXMLRP2 connection.</summary>
public sealed class QbxmlRp2ConnectionFactory : IQbConnectionFactory
{
    private readonly QbAgentOptions _options;

    public QbxmlRp2ConnectionFactory(IOptions<QbAgentOptions> options) => _options = options.Value;

    public IQbConnection Create() => new QbxmlRp2Connection(_options);
}
