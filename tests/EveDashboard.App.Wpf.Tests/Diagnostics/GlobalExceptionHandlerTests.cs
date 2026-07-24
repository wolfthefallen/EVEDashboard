namespace EveDashboard.App.Wpf.Tests.Diagnostics;

using System.Collections.Generic;
using EveDashboard.App.Wpf.Diagnostics;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class GlobalExceptionHandlerTests
{
    private sealed class FakeDialog : IUnhandledErrorDialog
    {
        public List<string> Messages { get; } = new();
        public void Show(string message) => Messages.Add(message);
    }

    private const string LogHint = @"%APPDATA%\EveDashboard\logs";

    private static (GlobalExceptionHandler Sut, CapturingLogger<GlobalExceptionHandler> Log, FakeDialog Dialog) Build()
    {
        var log = new CapturingLogger<GlobalExceptionHandler>();
        var dialog = new FakeDialog();
        return (new GlobalExceptionHandler(log, dialog, LogHint), log, dialog);
    }

    [Fact]
    public void Dispatcher_exception_logs_error_shows_dialog_and_returns_handled()
    {
        var (sut, log, dialog) = Build();

        var handled = sut.HandleDispatcherException(new InvalidOperationException("boom"));

        handled.Should().BeTrue(); // keep the app running
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
        dialog.Messages.Should().ContainSingle();
    }

    [Fact]
    public void Dialog_message_names_the_exception_and_points_at_the_log()
    {
        var (sut, _, dialog) = Build();

        sut.HandleDispatcherException(new InvalidOperationException("disk gremlins"));

        var msg = dialog.Messages.Single();
        msg.Should().Contain("InvalidOperationException");
        msg.Should().Contain("disk gremlins");
        msg.Should().Contain(LogHint);
    }

    [Fact]
    public void Terminating_domain_exception_logs_critical_and_shows_dialog()
    {
        var (sut, log, dialog) = Build();

        sut.HandleDomainException(new Exception("fatal"), isTerminating: true);

        log.Entries.Should().Contain(e => e.Level == LogLevel.Critical);
        dialog.Messages.Should().ContainSingle();
    }

    [Fact]
    public void Non_terminating_domain_exception_logs_error()
    {
        var (sut, log, _) = Build();

        sut.HandleDomainException(new Exception("bg"), isTerminating: false);

        log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Unobserved_task_exception_logs_warning()
    {
        var (sut, log, _) = Build();

        sut.HandleUnobservedTaskException(new Exception("unobserved"));

        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Same_signature_logs_every_time_but_shows_the_dialog_once()
    {
        var (sut, log, dialog) = Build();

        sut.HandleDispatcherException(new InvalidOperationException("recurring"));
        sut.HandleDispatcherException(new InvalidOperationException("recurring")); // same type + message

        log.Entries.Count(e => e.Level == LogLevel.Error).Should().Be(2); // logged both times
        dialog.Messages.Should().ContainSingle();                          // dialog only once
    }

    [Fact]
    public void Different_signatures_each_show_a_dialog()
    {
        var (sut, _, dialog) = Build();

        sut.HandleDispatcherException(new InvalidOperationException("a"));
        sut.HandleDispatcherException(new InvalidOperationException("b"));

        dialog.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void Ctor_and_methods_guard_nulls()
    {
        var dialog = new FakeDialog();
        ((Action)(() => _ = new GlobalExceptionHandler(null!, dialog, LogHint))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, null!, LogHint))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, dialog, null!))).Should().Throw<ArgumentNullException>();

        var (sut, _, _) = Build();
        ((Action)(() => sut.HandleDispatcherException(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => sut.HandleUnobservedTaskException(null!))).Should().Throw<ArgumentNullException>();
    }
}
