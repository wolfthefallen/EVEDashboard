namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Sde.Updater;

/// <summary>No-op <see cref="ISdeUpdater"/> for ServiceRegistration wiring tests.</summary>
internal sealed class FakeSdeUpdater : ISdeUpdater
{
    public event EventHandler<SdeUpdatedEventArgs>? Updated;

    public Task<SdeUpdateResult> CheckAndUpdateAsync(CancellationToken ct = default) =>
        Task.FromResult<SdeUpdateResult>(new SdeUpdateResult.NoUpdateNeeded());

    private void _suppress() => Updated?.Invoke(this, new SdeUpdatedEventArgs(DateTimeOffset.UtcNow));
}
