using System.Net;

namespace Protobuf.VisualStudio.Tests.Infrastructure;

/// <summary>Serves canned responses by absolute URL and records every request; unknown URLs get 404.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> responses = new(StringComparer.Ordinal);

    public List<string> Requests { get; } = [];

    public FakeHttpHandler Add(string url, string body) => this.Add(url, System.Text.Encoding.UTF8.GetBytes(body));

    public FakeHttpHandler Add(string url, byte[] body)
    {
        this.responses[url] = body;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (this.Requests)
        {
            this.Requests.Add(url);
        }

        if (!this.responses.TryGetValue(url, out var body))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }

        var content = new ByteArrayContent(body);
        content.Headers.ContentLength = body.Length;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
    }
}
