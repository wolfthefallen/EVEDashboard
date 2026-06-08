using EveDashboard.Stats.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Storage;

public class SessionStoreTests
{
    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        // The internal test-seam ctor (path + logger) is the one we exercise here.
        Action act = () => new SessionStore(null!, "Z:\\nowhere\\session.sqlite");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultConstructor_uses_AppData_root()
    {
        var store = new SessionStore(NullLogger<SessionStore>.Instance);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        store.DatabasePath.Should().StartWith(appData);
        store.DatabasePath.Should().EndWith(Path.Combine("EveDashboard", "session.sqlite"));
    }

    [Fact]
    public void DatabasePath_returns_explicit_path_when_provided()
    {
        var explicitPath = Path.Combine(Path.GetTempPath(), $"explicit-{Guid.NewGuid():N}.sqlite");
        var store = new SessionStore(NullLogger<SessionStore>.Instance, explicitPath);

        store.DatabasePath.Should().Be(explicitPath);
    }
}
