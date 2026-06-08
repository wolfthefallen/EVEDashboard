namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Sde.Storage;

/// <summary>Minimal <see cref="ISdeStorage"/> for ServiceRegistration wiring tests.</summary>
internal sealed class FakeSdeStorage : ISdeStorage
{
    public string DatabasePath { get; }

    public FakeSdeStorage(string databasePath) { DatabasePath = databasePath; }

    public Task<bool> EnsureDatabaseExistsAsync(CancellationToken ct = default) => Task.FromResult(false);
}
