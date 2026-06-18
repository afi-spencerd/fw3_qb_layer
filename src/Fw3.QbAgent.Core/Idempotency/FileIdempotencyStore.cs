using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Fw3.QbAgent.Core.Idempotency;

/// <summary>
/// File-backed idempotency store: one JSON file per key under a durable directory. Simple and
/// crash-safe enough for a single-instance agent — writes are atomic (temp file + replace) and all
/// access is serialized through the STA worker. Swap for SQL Server later behind the same interface.
/// </summary>
public sealed class FileIdempotencyStore : IIdempotencyStore
{
    private readonly string _directory;
    private readonly ILogger<FileIdempotencyStore> _logger;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileIdempotencyStore(string directory, ILogger<FileIdempotencyStore> logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    public bool TryGet(string key, [NotNullWhen(true)] out IdempotencyRecord? record)
    {
        record = null;
        var path = PathForKey(key);

        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                record = JsonSerializer.Deserialize<IdempotencyRecord>(File.ReadAllText(path), Json);
                return record is not null;
            }
            catch (Exception ex)
            {
                // A corrupt record must never be silently treated as "no prior write" — that could
                // allow a duplicate. Surface it loudly.
                _logger.LogError(ex, "Failed to read idempotency record at {Path}", path);
                throw;
            }
        }
    }

    public void Save(IdempotencyRecord record)
    {
        var path = PathForKey(record.Key);
        var tmp = path + ".tmp";

        lock (_gate)
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(record, Json));
            // Atomic on Windows/NTFS: the reader never sees a half-written file.
            File.Move(tmp, path, overwrite: true);
        }
    }

    /// <summary>Key is hashed to a safe, fixed-length filename (keys may contain path-hostile characters).</summary>
    private string PathForKey(string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory, hash + ".json");
    }
}
