using System.Net;
using System.Text;

namespace DesktopIconGrid.Core.Tests.Sync;

/// <summary>Scripted HTTP responses for the Google clients. Records every request for assertions.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler Enqueue(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        return this;
    }

    public FakeHttpHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    public int Pending => _responses.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsoluteUri,
            request.Headers.Authorization?.Parameter,
            body,
            request.Content?.Headers.ContentType?.MediaType));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}");
        }

        return _responses.Dequeue()(request);
    }

    public sealed record RecordedRequest(HttpMethod Method, string Url, string? BearerToken, string? Body, string? MediaType);
}
