using System.Net;
using System.Text;

namespace FeedTriage.Tests.Helpers;

/// <summary>
/// A configurable <see cref="HttpMessageHandler"/> for unit tests.
/// Matches requests by method + URL substring and returns the configured response.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<FakeRequest> _requests = [];
    private FakeRequest? _default;

    public void On(HttpMethod method, string urlContains, HttpStatusCode status, string body,
        string contentType = "application/json")
    {
        _requests.Add(new FakeRequest(method, urlContains, status, body, contentType));
    }

    public void OnAny(HttpStatusCode status, string body, string contentType = "application/json")
    {
        _default = new FakeRequest(null, null, status, body, contentType);
    }

    /// <summary>All requests that were sent through this handler.</summary>
    public List<(HttpMethod Method, Uri? Uri, string? Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        Calls.Add((request.Method, request.RequestUri, body));

        var match = _requests.FirstOrDefault(r =>
            (r.Method is null || r.Method == request.Method) &&
            (r.UrlContains is null || (request.RequestUri?.ToString().Contains(r.UrlContains) ?? false)));

        var rule = match ?? _default;

        if (rule is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No fake registered for {request.Method} {request.RequestUri}")
            };

        return new HttpResponseMessage(rule.Status)
        {
            Content = new StringContent(rule.Body, Encoding.UTF8, rule.ContentType)
        };
    }

    private sealed record FakeRequest(
        HttpMethod? Method,
        string? UrlContains,
        HttpStatusCode Status,
        string Body,
        string ContentType);
}
