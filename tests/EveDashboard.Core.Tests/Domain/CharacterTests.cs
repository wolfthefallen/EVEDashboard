using EveDashboard.Core.Domain;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Domain;

public class CharacterTests
{
    [Fact]
    public void TwoCharactersWithSameFields_AreEqual()
    {
        var a = new Character(95465499L, "CCP Falcon", @"D:\logs\20260429_180000_95465499.txt");
        var b = new Character(95465499L, "CCP Falcon", @"D:\logs\20260429_180000_95465499.txt");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void CharactersDifferingById_AreNotEqual()
    {
        var a = new Character(95465499L, "CCP Falcon", @"D:\logs\a.txt");
        var b = new Character(95465500L, "CCP Falcon", @"D:\logs\a.txt");
        a.Should().NotBe(b);
    }

    [Fact]
    public void CharactersDifferingByLogFilePath_AreNotEqual()
    {
        var a = new Character(95465499L, "CCP Falcon", @"D:\logs\a.txt");
        var b = new Character(95465499L, "CCP Falcon", @"D:\logs\b.txt");
        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_ContainsName()
    {
        var c = new Character(95465499L, "CCP Falcon", @"D:\logs\a.txt");
        c.ToString().Should().Contain("CCP Falcon");
    }
}
