namespace EveDashboard.App.Wpf.Tests.Helpers;

/// <summary>
/// An <see cref="IProgress{T}"/> that records synchronously on the calling
/// thread.
/// </summary>
/// <typeparam name="T">The progress payload.</typeparam>
/// <remarks>
/// Deliberately NOT <see cref="Progress{T}"/>: that captures the ambient
/// <see cref="SynchronizationContext"/> and posts its callbacks, so a test
/// asserting on them right after the awaited call is racing the context pump -
/// it passes or fails on timing. Production code still uses
/// <see cref="Progress{T}"/>, which is what marshals progress onto the UI
/// thread; only the observation in tests needs to be synchronous.
/// </remarks>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    /// <summary>Everything reported, in the order it was reported.</summary>
    public List<T> Reports { get; } = new();

    /// <inheritdoc />
    public void Report(T value) => Reports.Add(value);
}
