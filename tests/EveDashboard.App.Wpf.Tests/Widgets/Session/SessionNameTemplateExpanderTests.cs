namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using System.IO;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using FluentAssertions;

/// <summary>
/// Per plan 16 decision 16.R: exact-string-match replacement; no nesting;
/// unknown tokens left as literal.
/// </summary>
public class SessionNameTemplateExpanderTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 5, 2, 14, 30, 45, TimeSpan.Zero);

    private static IReadOnlyList<Character> Pilots(params string[] names) =>
        names.Select((n, i) => new Character(2_000_000_000 + i, n, Path.Combine(Path.GetTempPath(), n + ".txt"))).ToList();

    [Fact]
    public void Expand_throws_on_null_pilots()
    {
        Action act = () => SessionNameTemplateExpander.Expand("anything", null!, AsOf);
        act.Should().Throw<ArgumentNullException>().WithParameterName("selectedPilots");
    }

    [Fact]
    public void Expand_empty_template_returns_empty()
    {
        var result = SessionNameTemplateExpander.Expand("", Pilots(), AsOf);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_no_variables_returns_input_unchanged()
    {
        var result = SessionNameTemplateExpander.Expand("Mining Run", Pilots("Alice"), AsOf);
        result.Should().Be("Mining Run");
    }

    [Fact]
    public void Expand_DATE_replaces_with_yyyy_MM_dd()
    {
        var result = SessionNameTemplateExpander.Expand("Run on {{$DATE}}", Pilots(), AsOf);
        result.Should().Be("Run on 2026-05-02");
    }

    [Fact]
    public void Expand_TIME_replaces_with_HH_mm()
    {
        var result = SessionNameTemplateExpander.Expand("Started at {{$TIME}}", Pilots(), AsOf);
        result.Should().Be("Started at 14:30");
    }

    [Fact]
    public void Expand_DATETIME_replaces_with_full()
    {
        var result = SessionNameTemplateExpander.Expand("{{$DATETIME}} session", Pilots(), AsOf);
        result.Should().Be("2026-05-02 14:30 session");
    }

    [Fact]
    public void Expand_CHARS_joins_with_comma_space()
    {
        var result = SessionNameTemplateExpander.Expand("With {{$CHARS}}", Pilots("Alice", "Bob", "Carol"), AsOf);
        result.Should().Be("With Alice, Bob, Carol");
    }

    [Fact]
    public void Expand_unknown_token_left_as_literal()
    {
        var result = SessionNameTemplateExpander.Expand("Run {{$UNKNOWN}} now", Pilots(), AsOf);
        result.Should().Be("Run {{$UNKNOWN}} now");
    }

    [Fact]
    public void Expand_multiple_variables_in_one_template()
    {
        var template = "{{$DATE}} {{$TIME}} mining with {{$CHARS}}";
        var result = SessionNameTemplateExpander.Expand(template, Pilots("Alice", "Bob"), AsOf);
        result.Should().Be("2026-05-02 14:30 mining with Alice, Bob");
    }

    [Fact]
    public void Expand_DATETIME_replaced_before_DATE_or_TIME_substring_match()
    {
        // Order matters: if DATE were replaced first, it would substring-eat
        // the DATE portion of DATETIME. Verify the longest token wins.
        var result = SessionNameTemplateExpander.Expand("{{$DATETIME}}", Pilots(), AsOf);
        result.Should().Be("2026-05-02 14:30");
    }
}
