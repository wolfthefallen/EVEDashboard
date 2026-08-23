using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void TestPipeline_RunsAndPasses()
    {
        // This test exists only to verify dotnet test discovers and runs the
        // test pipeline. Once Core has real types, this file should be replaced
        // by real tests (or kept as a sanity smoke check, callee's choice).
        true.Should().BeTrue();
    }
}
