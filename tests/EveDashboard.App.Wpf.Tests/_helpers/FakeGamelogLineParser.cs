namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Domain;
using EveDashboard.Mining.Parsing;

/// <summary>Returns null for every line; sufficient for SessionController tests where no log lines flow.</summary>
internal sealed class FakeGamelogLineParser : IGamelogLineParser
{
    public MiningEvent? Parse(string line, long characterId) => null;
}
