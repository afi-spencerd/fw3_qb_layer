using Fw3.QbAgent.Core.Idempotency;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fw3.QbAgent.Tests;

public class FileIdempotencyStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fw3qbagent-idem-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_then_TryGet_round_trips_the_record()
    {
        var store = new FileIdempotencyStore(_dir, NullLogger<FileIdempotencyStore>.Instance);
        var record = new IdempotencyRecord
        {
            Key = "abc-123",
            Operation = "CustomerAdd",
            RequestHash = "hash",
            StatusCode = 201,
            ResponseJson = "{\"listId\":\"x\"}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        store.Save(record);

        Assert.True(store.TryGet("abc-123", out var found));
        Assert.Equal(record.ResponseJson, found!.ResponseJson);
        Assert.Equal(record.RequestHash, found.RequestHash);
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_key()
    {
        var store = new FileIdempotencyStore(_dir, NullLogger<FileIdempotencyStore>.Instance);
        Assert.False(store.TryGet("never-seen", out var found));
        Assert.Null(found);
    }

    [Fact]
    public void Records_persist_across_store_instances()
    {
        var first = new FileIdempotencyStore(_dir, NullLogger<FileIdempotencyStore>.Instance);
        first.Save(new IdempotencyRecord
        {
            Key = "persist-me",
            Operation = "CustomerAdd",
            RequestHash = "h",
            StatusCode = 201,
            ResponseJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        var second = new FileIdempotencyStore(_dir, NullLogger<FileIdempotencyStore>.Instance);
        Assert.True(second.TryGet("persist-me", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
