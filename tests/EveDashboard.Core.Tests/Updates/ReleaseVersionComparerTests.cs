namespace EveDashboard.Core.Tests.Updates;

using EveDashboard.Core.Updates;
using FluentAssertions;

public class ReleaseVersionComparerTests
{
    [Theory]
    // Equal -> not newer (covers the System.Version 3-vs-4-part normalization).
    [InlineData("0.1.0.0", "v0.1.0", false)]
    [InlineData("0.1.0.0", "0.1.0", false)]
    [InlineData("0.1.0", "v0.1.0", false)]
    // Strictly newer.
    [InlineData("0.1.0.0", "v0.2.0", true)]
    [InlineData("0.9.9.0", "v1.0.0", true)]
    [InlineData("0.1.0.0", "v0.1.1", true)]
    [InlineData("1.2.3.0", "v1.2.4", true)]
    // Older -> not newer.
    [InlineData("0.2.0.0", "v0.1.0", false)]
    [InlineData("1.0.0.0", "v0.9.9", false)]
    // SemVer pre-release / build-metadata suffix stripped before compare.
    [InlineData("0.1.0.0", "v0.2.0-beta", true)]
    [InlineData("0.1.0.0", "v0.1.0+build5", false)]
    // Blank / unparseable -> not newer (fail safe, no prompt).
    [InlineData("0.1.0.0", "", false)]
    [InlineData("0.1.0.0", "   ", false)]
    [InlineData("0.1.0.0", "v", false)]
    [InlineData("0.1.0.0", "latest", false)]
    [InlineData("0.1.0.0", "abc", false)]
    public void IsNewer_matches_expected(string running, string tag, bool expected)
    {
        ReleaseVersionComparer.IsNewer(Version.Parse(running), tag).Should().Be(expected);
    }

    [Fact]
    public void IsNewer_throws_on_null_running_version()
    {
        Action act = () => ReleaseVersionComparer.IsNewer(null!, "v1.0.0");
        act.Should().Throw<ArgumentNullException>();
    }
}
