namespace EveDashboard.App.Wpf.Tests.Services;

using EveDashboard.App.Wpf.Services;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Updates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

public class UpdateNotifierTests
{
    private sealed class FakeReleaseClient : IReleaseClient
    {
        private readonly ReleaseInfo? _result;
        private readonly Exception? _throw;

        public FakeReleaseClient(ReleaseInfo? result, Exception? throwEx = null)
        {
            _result = result;
            _throw = throwEx;
        }

        public Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
        {
            if (_throw is not null) throw _throw;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeBrowserLauncher : IBrowserLauncher
    {
        public int OpenCount { get; private set; }
        public string? LastUrl { get; private set; }

        public void Open(string url)
        {
            OpenCount++;
            LastUrl = url;
        }
    }

    private static readonly Version Running = new(0, 1, 0, 0);

    private static UpdateNotifier New(
        ReleaseInfo? release,
        FakeConfirmDialog confirm,
        FakeBrowserLauncher browser,
        Exception? clientThrow = null) =>
        new(new FakeReleaseClient(release, clientThrow), confirm, browser, NullLogger<UpdateNotifier>.Instance);

    [Fact]
    public async Task Prompts_with_Yes_No_and_opens_browser_when_newer_and_accepted()
    {
        var confirm = new FakeConfirmDialog(result: true);
        var browser = new FakeBrowserLauncher();
        var notifier = New(new ReleaseInfo("v0.2.0", "https://example/rel"), confirm, browser);

        await notifier.NotifyIfUpdateAvailableAsync(Running);

        confirm.ShowCount.Should().Be(1);
        confirm.LastConfirmLabel.Should().Be("Yes");
        confirm.LastCancelLabel.Should().Be("No");
        browser.OpenCount.Should().Be(1);
        browser.LastUrl.Should().Be("https://example/rel");
    }

    [Fact]
    public async Task Prompts_but_does_not_open_browser_when_declined()
    {
        var confirm = new FakeConfirmDialog(result: false);
        var browser = new FakeBrowserLauncher();
        var notifier = New(new ReleaseInfo("v0.2.0", "https://example/rel"), confirm, browser);

        await notifier.NotifyIfUpdateAvailableAsync(Running);

        confirm.ShowCount.Should().Be(1);
        browser.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Does_not_prompt_when_release_not_newer()
    {
        var confirm = new FakeConfirmDialog(result: true);
        var browser = new FakeBrowserLauncher();
        var notifier = New(new ReleaseInfo("v0.1.0", "https://example/rel"), confirm, browser);

        await notifier.NotifyIfUpdateAvailableAsync(Running);

        confirm.ShowCount.Should().Be(0);
        browser.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Does_not_prompt_when_no_release_found()
    {
        var confirm = new FakeConfirmDialog(result: true);
        var browser = new FakeBrowserLauncher();
        var notifier = New(release: null, confirm, browser);

        await notifier.NotifyIfUpdateAvailableAsync(Running);

        confirm.ShowCount.Should().Be(0);
        browser.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Swallows_client_exception_and_does_not_throw()
    {
        var confirm = new FakeConfirmDialog(result: true);
        var browser = new FakeBrowserLauncher();
        var notifier = New(release: null, confirm, browser, clientThrow: new InvalidOperationException("boom"));

        Func<Task> act = async () => await notifier.NotifyIfUpdateAvailableAsync(Running);

        await act.Should().NotThrowAsync();
        browser.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Throws_on_null_running_version()
    {
        var notifier = New(new ReleaseInfo("v0.2.0", "https://example/rel"), new FakeConfirmDialog(true), new FakeBrowserLauncher());
        Func<Task> act = async () => await notifier.NotifyIfUpdateAvailableAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
