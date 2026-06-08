namespace EveDashboard.Sde.Tests.Updater;

/// <summary>
/// Loads the committed <c>tests/.../testdata/sde-fixtures.tar.xz</c> bytes
/// for use as a synthesised everef.net response in updater unit tests.
/// </summary>
/// <remarks>
/// The archive is built once via <c>tar -cJf</c> and committed — see
/// <c>tests/EveDashboard.Sde.Tests/testdata/sde-fixtures.tar.xz.regen.md</c>
/// for the regeneration command. Per plan 08 decision 8.26: SharpCompress
/// 0.47.4 cannot write xz, so the original synthesise-at-test-time approach
/// (decision 8.9) was replaced with a shipped binary fixture.
/// </remarks>
internal static class SdeFixtureLoader
{
    /// <summary>
    /// Read the entire shipped tar.xz into a byte array. Each call returns
    /// a fresh array so callers (e.g. fake <see cref="HttpResponseMessage"/>
    /// payloads) cannot accidentally share stream state across tests.
    /// </summary>
    public static byte[] LoadTarXzBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "testdata", "sde-fixtures.tar.xz");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "sde-fixtures.tar.xz not found in test output. " +
                "Did the testdata copy step run? See sde-fixtures.tar.xz.regen.md.",
                path);
        return File.ReadAllBytes(path);
    }
}
