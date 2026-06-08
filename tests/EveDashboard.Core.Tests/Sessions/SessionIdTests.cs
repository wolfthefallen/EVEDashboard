namespace EveDashboard.Core.Tests.Sessions;

using EveDashboard.Core.Sessions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for <see cref="SessionId"/> — the content-addressed session id helper
/// (plan 38). The recipe must be stable and identical across live creation,
/// migration, and import, so these lock down determinism, format, the
/// delimiter's collision-avoidance, the sentinel, and the validator.
/// </summary>
public class SessionIdTests
{
    [Fact]
    public void Compute_is_deterministic()
    {
        var a = SessionId.Compute("2026-05-05 KDG-TA", "2026-05-05T22:10:35.203Z");
        var b = SessionId.Compute("2026-05-05 KDG-TA", "2026-05-05T22:10:35.203Z");
        a.Should().Be(b);
    }

    [Fact]
    public void Compute_returns_64_char_lowercase_hex()
    {
        var id = SessionId.Compute("PT-21c Zydrine", "2026-06-06T15:20:43.108Z");
        id.Should().HaveLength(64);
        id.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_matches_known_golden_vector()
    {
        // sha256( UTF8("a" + 0x1F + "b") ), computed independently.
        SessionId.Compute("a", "b")
            .Should().Be("f04cdced9736a69da6103f08a4daaf8c485dd481217d218a1b4993c8c3968e13");
    }

    [Fact]
    public void Delimiter_prevents_name_timestamp_boundary_collision()
    {
        // Without the 0x1F separator, ("ab","c") and ("a","bc") would hash the
        // same concatenation. The delimiter keeps them distinct.
        SessionId.Compute("ab", "c").Should().NotBe(SessionId.Compute("a", "bc"));
    }

    [Fact]
    public void Different_start_times_produce_different_ids()
    {
        var first  = SessionId.Compute("Same Name", "2026-06-06T15:20:43.108Z");
        var second = SessionId.Compute("Same Name", "2026-06-06T15:20:43.109Z");
        first.Should().NotBe(second);
    }

    [Fact]
    public void Unassociated_is_64_zeros()
    {
        SessionId.Unassociated.Should().Be(new string('0', 64));
        SessionId.LooksLikeSha256(SessionId.Unassociated).Should().BeTrue(
            "the sentinel is the same width/charset as a real id");
    }

    [Theory]
    [InlineData("f04cdced9736a69da6103f08a4daaf8c485dd481217d218a1b4993c8c3968e13", true)]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", true)]
    [InlineData("5", false)]                                   // legacy integer id
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("F04CDCED9736A69DA6103F08A4DAAF8C485DD481217D218A1B4993C8C3968E13", false)] // uppercase rejected
    [InlineData("f04cdced9736a69da6103f08a4daaf8c485dd481217d218a1b4993c8c3968e1", false)]  // 63 chars
    [InlineData("g04cdced9736a69da6103f08a4daaf8c485dd481217d218a1b4993c8c3968e13", false)]  // non-hex char
    public void LooksLikeSha256_accepts_only_64_char_lowercase_hex(string? value, bool expected)
    {
        SessionId.LooksLikeSha256(value).Should().Be(expected);
    }
}
