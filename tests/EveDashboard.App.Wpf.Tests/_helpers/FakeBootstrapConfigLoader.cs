namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Configuration;

/// <summary>
/// Test seam — returns a configurable <see cref="BootstrapConfigResult"/> from
/// <see cref="LoadAsync"/> and records the last <see cref="SaveAsync"/> path.
/// Used by Settings dialog VM tests and ServiceRegistration DI tests.
/// </summary>
internal sealed class FakeBootstrapConfigLoader : IBootstrapConfigLoader
{
    private BootstrapConfigResult _loadResult;

    public FakeBootstrapConfigLoader(BootstrapConfigResult? loadResult = null)
    {
        _loadResult = loadResult ?? new BootstrapConfigResult.Loaded(@"C:\fake\Gamelogs");
    }

    public string? LastSavedPath { get; private set; }
    public int SaveCount { get; private set; }

    /// <summary>When set, SaveAsync throws this exception on every call.</summary>
    public Exception? SaveExceptionToThrow { get; set; }

    /// <summary>When set, the next SaveAsync call throws this then clears it (so the second attempt succeeds).</summary>
    public Exception? SaveExceptionToThrowOnce { get; set; }

    public Task<BootstrapConfigResult> LoadAsync(CancellationToken ct = default)
        => Task.FromResult(_loadResult);

    public Task SaveAsync(string gamelogPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gamelogPath);

        if (SaveExceptionToThrowOnce is { } once)
        {
            SaveExceptionToThrowOnce = null;
            throw once;
        }
        if (SaveExceptionToThrow is { } persistent)
        {
            throw persistent;
        }

        LastSavedPath = gamelogPath;
        SaveCount++;
        return Task.CompletedTask;
    }

    public void SetLoadResult(BootstrapConfigResult result) => _loadResult = result;
}
