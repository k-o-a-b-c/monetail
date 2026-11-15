using System.Net;
using System.Text;
using System.Text.Json;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferHundredValidRowsOver64KiBAreStreamedWithAndWithoutLength(bool chunked)
    {
        var rows = Enumerable.Range(0, 100).Select(_ => Summary(Guid.NewGuid()) with { Name = new string('名', 100), Memo = new string('記', 100) }).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Page(rows, "opaque-cursor"), Web); Assert.True(bytes.Length > 65536);
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = chunked ? new StreamContent(new ChunkStream(bytes)) : new ByteArrayContent(bytes) });
        var result = await f.Workspace.ListAsync(new("all")); Assert.Equal(rows, result.Transfers); Assert.Equal("opaque-cursor", result.NextCursor); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferDetailSuccessAlsoUsesStreamRatherThanWholeBodyBound(bool chunked)
    {
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 70000) + JsonSerializer.Serialize(Detail(), Web));
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = chunked ? new StreamContent(new ChunkStream(bytes)) : new ByteArrayContent(bytes) });
        Assert.Equal(Detail() with { Memo = null }, await f.Workspace.GetAsync(Transfer)); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("suggestions", false)] [InlineData("suggestions", true)] [InlineData("autofill", false)] [InlineData("autofill", true)]
    [InlineData("error", false)] [InlineData("error", true)]
    public async Task TransferOtherBodiesKeepExisting64KiBLimit(string operation, bool chunked)
    {
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 70000) + (operation == "error" ? "{\"code\":\"forbidden\"}" : Payload(operation).ToJsonString()));
        using var f = new Fixture(_ => new(operation == "error" ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = chunked ? new StreamContent(new ChunkStream(bytes)) : new ByteArrayContent(bytes) });
        await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation == "error" ? "list" : operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("suggestions")] [InlineData("autofill")]
    public async Task TransferBrokenBodyStreamIsConnectionFailureWithoutRetry(string operation)
    {
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(Encoding.UTF8.GetBytes(Payload(operation).ToJsonString()), failAfter: 1)) });
        await Assert.ThrowsAsync<ApiConnectionException>(() => Read(f, operation)); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(201)] [InlineData(204)] [InlineData(302)]
    public async Task TransferReadRejectsUnexpectedSuccessAndRedirectStatuses(int status)
    {
        using var f = new Fixture(_ => Json(Page(), (HttpStatusCode)status)); await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all"))); Assert.Single(f.Requests);
    }

    private sealed class ChunkStream(byte[] bytes, int? failAfter = null) : Stream
    {
        private int position, calls;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); if (failAfter is int fail && calls++ >= fail) throw new IOException("synthetic-stream-failure");
            var count = Math.Min(127, Math.Min(buffer.Length, bytes.Length - position)); bytes.AsMemory(position, count).CopyTo(buffer); position += count; return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
