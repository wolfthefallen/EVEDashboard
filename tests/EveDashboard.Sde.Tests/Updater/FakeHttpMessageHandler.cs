namespace EveDashboard.Sde.Tests.Updater;

/// <summary>
/// Hand-rolled <see cref="HttpMessageHandler"/> for unit tests of
/// <see cref="EveDashboard.Sde.Updater.EverefSdeUpdater"/>. Tests configure
/// <see cref="Responder"/> per scenario; the handler captures every request
/// it serves into <see cref="Requests"/> so tests can assert on counts,
/// methods, and headers (including conditional-GET headers).
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    /// <summary>
    /// Test-supplied responder. Receives the inbound <see cref="HttpRequestMessage"/>
    /// and returns the response to send back. May throw to simulate a network
    /// failure (the throw propagates to the caller as if the underlying socket
    /// had failed). Required — leaving it null causes <see cref="SendAsync"/>
    /// to throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    /// <summary>
    /// Captured requests, in the order they were sent. Each entry is the
    /// live <see cref="HttpRequestMessage"/> instance — properties remain
    /// readable for assertions.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Responder is null)
            throw new InvalidOperationException(
                "FakeHttpMessageHandler.Responder was not configured before SendAsync ran.");
        return Task.FromResult(Responder(request));
    }
}
