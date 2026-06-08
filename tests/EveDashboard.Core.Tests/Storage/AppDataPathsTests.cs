using EveDashboard.Core.Storage;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Storage;

public class AppDataPathsTests
{
    [Fact]
    public void AppFolderName_is_EveDashboard()
    {
        AppDataPaths.AppFolderName.Should().Be("EveDashboard");
    }

    [Fact]
    public void GetAppDataRoot_returns_appdata_joined_with_app_folder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EveDashboard");

        AppDataPaths.GetAppDataRoot().Should().Be(expected);
    }
}
