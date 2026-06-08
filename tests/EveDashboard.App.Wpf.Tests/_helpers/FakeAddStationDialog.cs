namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.Core.Settings;

/// <summary>
/// Hand-rolled fake of <see cref="IAddStationDialog"/>. Returns a queue of
/// preset results so a single test can drive multiple Add clicks. Counts
/// invocations so tests can assert the dialog was shown the expected number
/// of times.
/// </summary>
internal sealed class FakeAddStationDialog : IAddStationDialog
{
    private readonly Queue<MarketStation?> _queue;

    public int ShowCount { get; private set; }

    public FakeAddStationDialog(params MarketStation?[] results)
    {
        _queue = new Queue<MarketStation?>(results);
    }

    public MarketStation? Show()
    {
        ShowCount++;
        return _queue.Count > 0 ? _queue.Dequeue() : null;
    }
}
