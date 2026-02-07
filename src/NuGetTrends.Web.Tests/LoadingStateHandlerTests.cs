using FluentAssertions;
using NuGetTrends.Web.Client.Services;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class LoadingStateHandlerTests
{
    [Fact]
    public async Task ApiRequest_IncrementsAndDecrementsLoadingState()
    {
        var loadingState = new LoadingState();
        var handler = CreateHandler(loadingState, new OkHandler());

        await handler.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/packages"), CancellationToken.None);

        loadingState.IsLoading.Should().BeFalse("request completed so state should be decremented");
    }

    [Fact]
    public async Task ApiRequest_IsLoadingDuringRequest()
    {
        var loadingState = new LoadingState();
        bool wasLoadingDuringRequest = false;
        var innerHandler = new CallbackHandler(() => wasLoadingDuringRequest = loadingState.IsLoading);
        var handler = CreateHandler(loadingState, innerHandler);

        await handler.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/packages"), CancellationToken.None);

        wasLoadingDuringRequest.Should().BeTrue("loading state should be true while request is in flight");
    }

    [Fact]
    public async Task NonApiRequest_DoesNotAffectLoadingState()
    {
        var loadingState = new LoadingState();
        bool wasLoadingDuringRequest = false;
        var innerHandler = new CallbackHandler(() => wasLoadingDuringRequest = loadingState.IsLoading);
        var handler = CreateHandler(loadingState, innerHandler);

        await handler.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/css/app.css"), CancellationToken.None);

        wasLoadingDuringRequest.Should().BeFalse("non-API requests should not trigger loading state");
        loadingState.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task ApiRequest_WhenSendThrows_StillDecrements()
    {
        var loadingState = new LoadingState();
        var innerHandler = new ThrowingHandler();
        var handler = CreateHandler(loadingState, innerHandler);

        var act = () => handler.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/packages"), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        loadingState.IsLoading.Should().BeFalse("decrement should happen in finally block even on exception");
    }

    [Fact]
    public async Task NullRequestUri_DoesNotAffectLoadingState()
    {
        var loadingState = new LoadingState();
        var handler = CreateHandler(loadingState, new OkHandler());
        var request = new HttpRequestMessage { RequestUri = null };

        await handler.SendAsync(request, CancellationToken.None);

        loadingState.IsLoading.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://localhost/api/packages")]
    [InlineData("https://localhost/api/package/search?q=sentry")]
    [InlineData("https://localhost/api/package/trending")]
    public async Task VariousApiPaths_TriggerLoadingState(string url)
    {
        var loadingState = new LoadingState();
        bool wasLoading = false;
        var innerHandler = new CallbackHandler(() => wasLoading = loadingState.IsLoading);
        var handler = CreateHandler(loadingState, innerHandler);

        await handler.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);

        wasLoading.Should().BeTrue($"URL '{url}' contains /api/ and should trigger loading");
    }

    [Theory]
    [InlineData("https://localhost/css/app.css")]
    [InlineData("https://localhost/js/chart.js")]
    [InlineData("https://localhost/_framework/blazor.web.js")]
    public async Task NonApiPaths_DoNotTriggerLoadingState(string url)
    {
        var loadingState = new LoadingState();
        bool wasLoading = false;
        var innerHandler = new CallbackHandler(() => wasLoading = loadingState.IsLoading);
        var handler = CreateHandler(loadingState, innerHandler);

        await handler.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);

        wasLoading.Should().BeFalse($"URL '{url}' does not contain /api/ and should not trigger loading");
    }

    private static TestableLoadingStateHandler CreateHandler(LoadingState loadingState, HttpMessageHandler innerHandler)
    {
        return new TestableLoadingStateHandler(loadingState) { InnerHandler = innerHandler };
    }

    /// <summary>
    /// Exposes SendAsync publicly for testing (DelegatingHandler.SendAsync is protected).
    /// </summary>
    private class TestableLoadingStateHandler : LoadingStateHandler
    {
        public TestableLoadingStateHandler(LoadingState loadingState) : base(loadingState) { }

        public new Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => base.SendAsync(request, cancellationToken);
    }

    private class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private class CallbackHandler : HttpMessageHandler
    {
        private readonly Action _callback;
        public CallbackHandler(Action callback) => _callback = callback;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callback();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated failure");
    }
}
