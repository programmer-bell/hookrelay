using HookRelay.Data;
using HookRelay.Domain;
using HookRelay.Features.Ingest;
using HookRelay.Services;
using Xunit;
using Endpoint = HookRelay.Domain.Endpoint;

namespace HookRelay.Tests;

public class IngestRecorderTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Endpoint SampleEndpoint = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "testslug01",
        "Test",
        "https://httpbin.org/anything",
        "signing-secret",
        new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task UnknownSlug_ReturnsEndpointNotFound()
    {
        var recorder = CreateRecorder();

        var result = await recorder.RecordAsync("nosuchslug", "POST", "{}", "body", null, "");

        Assert.Equal(IngestOutcome.EndpointNotFound, result.Outcome);
        Assert.Null(result.RequestId);
    }

    [Fact]
    public async Task FirstCaptureWithKey_ReturnsCaptured()
    {
        var captures = new FakeCaptureRepository();
        var recorder = CreateRecorder(captures);

        var result = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-1", null, "key-1");

        Assert.Equal(IngestOutcome.Captured, result.Outcome);
        Assert.NotNull(result.RequestId);
        Assert.Single(captures.Store);
        Assert.Equal("key-1", captures.Store[0].IdempotencyKey);
        Assert.Equal(FixedNow, captures.Store[0].ReceivedAt);
    }

    [Fact]
    public async Task DuplicateKey_ReturnsExisting_AndDoesNotCaptureAgain()
    {
        var captures = new FakeCaptureRepository();
        var recorder = CreateRecorder(captures);

        var first = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-1", null, "key-dup");
        var second = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-2", null, "key-dup");

        Assert.Equal(IngestOutcome.Captured, first.Outcome);
        Assert.Equal(IngestOutcome.Existing, second.Outcome);
        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Single(captures.Store);
    }

    [Fact]
    public async Task DifferentKeys_CaptureEachRequest()
    {
        var captures = new FakeCaptureRepository();
        var recorder = CreateRecorder(captures);

        var first = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-1", null, "key-a");
        var second = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-2", null, "key-b");

        Assert.Equal(IngestOutcome.Captured, first.Outcome);
        Assert.Equal(IngestOutcome.Captured, second.Outcome);
        Assert.NotEqual(first.RequestId, second.RequestId);
        Assert.Equal(2, captures.Store.Count);
    }

    [Fact]
    public async Task EmptyKey_AlwaysCaptures()
    {
        var captures = new FakeCaptureRepository();
        var recorder = CreateRecorder(captures);

        var first = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-1", null, "");
        var second = await recorder.RecordAsync(
            SampleEndpoint.Slug, "POST", "{}", "payload-2", null, "   ");

        Assert.Equal(IngestOutcome.Captured, first.Outcome);
        Assert.Equal(IngestOutcome.Captured, second.Outcome);
        Assert.NotEqual(first.RequestId, second.RequestId);
        Assert.All(captures.Store, request => Assert.Null(request.IdempotencyKey));
    }

    private static IngestRecorder CreateRecorder(FakeCaptureRepository? captures = null)
    {
        captures ??= new FakeCaptureRepository();
        return new IngestRecorder(
            new FakeEndpointRepository(SampleEndpoint),
            captures,
            new EventBus(),
            new FixedTimeProvider(FixedNow));
    }

    private sealed class FakeEndpointRepository(Endpoint endpoint) : IEndpointRepository
    {
        public Task<Endpoint?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(slug == endpoint.Slug ? endpoint : null);
    }

    private sealed class FakeCaptureRepository : ICaptureRepository
    {
        public List<CapturedRequest> Store { get; } = [];

        public Task<CapturedRequest?> FindByIdempotencyKeyAsync(
            Guid endpointId,
            string idempotencyKey,
            CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(request =>
                request.EndpointId == endpointId && request.IdempotencyKey == idempotencyKey));

        public Task<CapturedRequest> CaptureAsync(CapturedRequest request, CancellationToken ct = default)
        {
            Store.Add(request);
            return Task.FromResult(request);
        }
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }
}
