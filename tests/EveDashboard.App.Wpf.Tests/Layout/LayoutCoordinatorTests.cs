namespace EveDashboard.App.Wpf.Tests.Layout;

using EveDashboard.App.Wpf.Layout;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Sessions;
using FluentAssertions;

/// <summary>
/// LayoutCoordinator test workhorse — covers ctor null-args, LoadAsync default
/// + saved + ghost-preserving + malformed paths, per-pilot collection
/// grow/shrink merge, MoveItem persistence, Hide/Show flips, the
/// HideRequested event bridge, dispose unsubscription, and the
/// SessionStats-on-first-activation lifecycle (SessionConfig is a modal
/// dialog, not an inline widget, so the dashboard layout owns only
/// SessionStats + Pilot kinds).
/// </summary>
public class LayoutCoordinatorTests
{
    /* ---------- Constructor null-arg checks ---------- */

    [Fact]
    public void Ctor_throws_on_null_activeSession()
    {
        using var f = new LayoutCoordinatorTestFixture();
        Action act = () => new LayoutCoordinator(
            null!, f.PerPilotCoord, f.SessionVm, f.SessionStatsVm, f.SettingsRepo, f.CoordLog);
        act.Should().Throw<ArgumentNullException>().WithParameterName("activeSession");
    }

    [Fact]
    public void Ctor_throws_on_null_perPilots()
    {
        using var f = new LayoutCoordinatorTestFixture();
        Action act = () => new LayoutCoordinator(
            f.PerPilotFixture.ActiveSession, null!, f.SessionVm, f.SessionStatsVm, f.SettingsRepo, f.CoordLog);
        act.Should().Throw<ArgumentNullException>().WithParameterName("perPilots");
    }

    [Fact]
    public void Ctor_throws_on_null_sessionController()
    {
        using var f = new LayoutCoordinatorTestFixture();
        Action act = () => new LayoutCoordinator(
            f.PerPilotFixture.ActiveSession, f.PerPilotCoord, null!, f.SessionStatsVm, f.SettingsRepo, f.CoordLog);
        act.Should().Throw<ArgumentNullException>().WithParameterName("sessionController");
    }

    [Fact]
    public void Ctor_throws_on_null_sessionStatsVm()
    {
        using var f = new LayoutCoordinatorTestFixture();
        Action act = () => new LayoutCoordinator(
            f.PerPilotFixture.ActiveSession, f.PerPilotCoord, f.SessionVm, null!, f.SettingsRepo, f.CoordLog);
        act.Should().Throw<ArgumentNullException>().WithParameterName("sessionStatsVm");
    }

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        using var f = new LayoutCoordinatorTestFixture();
        Action act = () => new LayoutCoordinator(
            f.PerPilotFixture.ActiveSession, f.PerPilotCoord, f.SessionVm, f.SessionStatsVm, null!, f.CoordLog);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Ctor_throws_on_null_log()
    {
        using var f = new LayoutCoordinatorTestFixture();
        Action act = () => new LayoutCoordinator(
            f.PerPilotFixture.ActiveSession, f.PerPilotCoord, f.SessionVm, f.SessionStatsVm, f.SettingsRepo, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("log");
    }

    /* ---------- LoadAsync default layout ---------- */

    [Fact]
    public async Task LoadAsync_with_no_saved_layout_yields_alphabetical_pilots_only()
    {
        // The default layout does not seed SessionConfig (it is a modal
        // dialog, not a widget). SessionStats is added on first activation,
        // not at load time. So Items is the per-pilot list.
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync(
            (101, "Charlie"), (102, "alice"), (103, "Bob"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        coord.Items.Should().HaveCount(3);
        coord.Items[0].DisplayName.Should().Be("alice");
        coord.Items[1].DisplayName.Should().Be("Bob");
        coord.Items[2].DisplayName.Should().Be("Charlie");
        coord.Items.Should().AllSatisfy(i => i.IsHidden.Should().BeFalse());
    }

    /* ---------- LoadAsync saved layout ---------- */

    [Fact]
    public async Task LoadAsync_with_saved_layout_restores_ordering_and_hidden_state()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync(
            (101, "alice"), (102, "Bob"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        var seeded = new WidgetLayout(new[]
        {
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(102), Hidden: true),
            new WidgetLayoutItem(new WidgetDescriptor.SessionStats(), Hidden: false),
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(101), Hidden: false),
        });
        await f.SettingsRepo.SetWidgetLayoutAsync(seeded);

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        // Activate stats so the SessionStats descriptor renders. Mode change
        // must fire AFTER coord construction (before that, the controller's
        // PropertyChanged event has no subscriber on the coord).
        f.SessionVm.Mode = SessionWidgetMode.Running;

        coord.Items.Should().HaveCount(3);
        coord.Items[0].DisplayName.Should().Be("Bob");
        coord.Items[0].IsHidden.Should().BeTrue();
        coord.Items[1].Descriptor.Should().BeOfType<WidgetDescriptor.SessionStats>();
        coord.Items[2].DisplayName.Should().Be("alice");
    }

    /* ---------- LoadAsync ghost preservation ---------- */

    [Fact]
    public async Task LoadAsync_with_ghost_pilot_skips_render_but_preserves_persisted_entry()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        // Seed a layout containing a ghost pilot id (999) not in the current session.
        var seeded = new WidgetLayout(new[]
        {
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(999), Hidden: false),
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(101), Hidden: false),
        });
        await f.SettingsRepo.SetWidgetLayoutAsync(seeded);

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        // Renderable items: alice only (ghost 999 NOT materialised).
        coord.Items.Should().HaveCount(1);
        coord.Items[0].DisplayName.Should().Be("alice");

        // But the persisted order list still contains the ghost.
        var persisted = coord.SnapshotPersistedItems();
        persisted.Should().HaveCount(2);
        persisted[0].Descriptor.Should().BeEquivalentTo(new WidgetDescriptor.Pilot(999));
    }

    /* ---------- LoadAsync malformed JSON ---------- */

    [Fact]
    public async Task LoadAsync_with_malformed_saved_json_falls_back_to_default_layout()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        // Stuff malformed JSON directly into the setting table.
        await f.PerPilotFixture.Db.ExecuteNonQueryAsync(
            "INSERT INTO setting (key, value) VALUES ('widget_layout', '{not-json')");

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        // Default layout: alice only (no SessionConfig now; SessionStats latent).
        coord.Items.Should().HaveCount(1);
        coord.Items[0].DisplayName.Should().Be("alice");
    }

    /* ---------- Pilot-collection grows ---------- */

    [Fact]
    public async Task PerPilots_collection_grows_appends_new_pilot_to_items()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid1 = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid1, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        coord.Items.Should().HaveCount(1);

        // Begin a second session with one new pilot — PerPilotWidgetCoordinator clears + repopulates Widgets.
        var sid2 = await f.InsertSessionWithPilotsAsync((202, "Zach"));
        f.PerPilotFixture.ActiveSession.Set(sid2, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        // Items rebuilt: Zach (alice ghost preserved in persisted order but not materialised).
        coord.Items.Should().HaveCount(1);
        coord.Items[0].DisplayName.Should().Be("Zach");

        var persisted = coord.SnapshotPersistedItems();
        persisted.Select(i => i.Descriptor).Should().ContainEquivalentOf(new WidgetDescriptor.Pilot(101));
        persisted.Select(i => i.Descriptor).Should().ContainEquivalentOf(new WidgetDescriptor.Pilot(202));
    }

    /* ---------- MoveItem ---------- */

    [Fact]
    public async Task MoveItem_reorders_items_and_persists_new_order()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync(
            (101, "alice"), (102, "Bob"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        // Initial order: alice, Bob.
        coord.MoveItem(1, 0); // Move Bob to slot 0.
        await coord.FlushPendingSaveAsync();

        coord.Items[0].DisplayName.Should().Be("Bob");
        coord.Items[1].DisplayName.Should().Be("alice");

        // Reload and verify the saved order survives.
        var saved = await f.SettingsRepo.GetWidgetLayoutAsync();
        saved.Should().NotBeNull();
        saved!.Items.Should().HaveCount(2);
        saved.Items[0].Descriptor.Should().BeEquivalentTo(new WidgetDescriptor.Pilot(102));
        saved.Items[1].Descriptor.Should().BeEquivalentTo(new WidgetDescriptor.Pilot(101));
    }

    [Fact]
    public async Task MoveItem_with_same_from_to_is_noop()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        coord.MoveItem(0, 0);

        coord.Items[0].DisplayName.Should().Be("alice");
    }

    /* ---------- Hide / Show ---------- */

    [Fact]
    public async Task Hide_flips_IsHidden_and_HasHiddenWidgets_and_persists()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        coord.HasHiddenWidgets.Should().BeFalse();

        coord.Hide(new WidgetDescriptor.Pilot(101));
        await coord.FlushPendingSaveAsync();

        coord.Items[0].IsHidden.Should().BeTrue();
        coord.HasHiddenWidgets.Should().BeTrue();
        coord.HiddenItems.Should().ContainSingle().Which.DisplayName.Should().Be("alice");

        var saved = await f.SettingsRepo.GetWidgetLayoutAsync();
        saved.Should().NotBeNull();
        var pilotEntry = saved!.Items.Single(i => i.Descriptor is WidgetDescriptor.Pilot pp && pp.PilotId == 101);
        pilotEntry.Hidden.Should().BeTrue();
    }

    [Fact]
    public async Task Show_flips_IsHidden_back_and_clears_HasHiddenWidgets()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        coord.Hide(new WidgetDescriptor.Pilot(101));
        coord.Show(new WidgetDescriptor.Pilot(101));
        await coord.FlushPendingSaveAsync();

        coord.Items[0].IsHidden.Should().BeFalse();
        coord.HasHiddenWidgets.Should().BeFalse();
    }

    [Fact]
    public async Task Hide_with_unknown_descriptor_is_noop()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        coord.Hide(new WidgetDescriptor.Pilot(99999));
        coord.HasHiddenWidgets.Should().BeFalse();
    }

    [Fact]
    public void Hide_throws_on_null_descriptor()
    {
        using var f = new LayoutCoordinatorTestFixture();
        using var coord = f.CreateCoordinator();
        Action act = () => coord.Hide(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Show_throws_on_null_descriptor()
    {
        using var f = new LayoutCoordinatorTestFixture();
        using var coord = f.CreateCoordinator();
        Action act = () => coord.Show(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /* ---------- HideRequested event bridge ---------- */

    [Fact]
    public async Task WidgetVm_HideCommand_triggers_LayoutCoordinator_Hide()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        // Invoke HideCommand on the per-pilot VM (as the chrome x button would).
        var pilotVm = f.PerPilotCoord.Widgets.Single();
        pilotVm.HideCommand.Execute(null);

        coord.Items[0].IsHidden.Should().BeTrue();
        coord.HasHiddenWidgets.Should().BeTrue();
    }

    /* ---------- Dispose unsubscription ---------- */

    [Fact]
    public async Task Dispose_unsubscribes_so_post_dispose_pilot_changes_dont_mutate_items()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid1 = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid1, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        var preDispose = coord.Items.Count;

        coord.Dispose();

        // Trigger a new session; coordinator should NOT react.
        var sid2 = await f.InsertSessionWithPilotsAsync((202, "Zach"));
        f.PerPilotFixture.ActiveSession.Set(sid2, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        coord.Items.Count.Should().Be(preDispose);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var coord = f.CreateCoordinator();
        coord.Dispose();
        Action act = () => coord.Dispose();
        act.Should().NotThrow();
    }

    /* ---------- SessionStats lifecycle (SessionConfig is a dialog, not a widget) ---------- */

    [Fact]
    public async Task Mode_change_to_Running_activates_stats()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        // Default: SessionStats not yet renderable.
        coord.Items.Should().NotContain(i => i.Descriptor is WidgetDescriptor.SessionStats);

        // Drive Mode -> Running on the controller; coordinator's PropertyChanged handler latches _statsActivated.
        f.SessionVm.Mode = SessionWidgetMode.Running;

        coord.Items.Should().Contain(i => i.Descriptor is WidgetDescriptor.SessionStats);
    }

    [Fact]
    public async Task Mode_change_to_Paused_activates_stats()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        f.SessionVm.Mode = SessionWidgetMode.Paused;

        coord.Items.Should().Contain(i => i.Descriptor is WidgetDescriptor.SessionStats);
    }

    [Fact]
    public async Task Mode_change_to_Stopped_keeps_stats_in_items()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        f.SessionVm.Mode = SessionWidgetMode.Running;
        f.SessionVm.Mode = SessionWidgetMode.Stopped;

        coord.Items.Should().Contain(i => i.Descriptor is WidgetDescriptor.SessionStats);
    }

    [Fact]
    public async Task Stats_descriptor_inserts_at_slot_0_on_first_activation_when_no_saved_slot()
    {
        using var f = new LayoutCoordinatorTestFixture();
        // No per-pilot widgets active; default empty layout.
        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();
        coord.Items.Should().BeEmpty();

        // Activate stats by transitioning Mode.
        f.SessionVm.Mode = SessionWidgetMode.Running;

        var persisted = coord.SnapshotPersistedItems();
        persisted[0].Descriptor.Should().BeOfType<WidgetDescriptor.SessionStats>(
            "first activation inserts SessionStats at slot 0 of _persistedItems");
    }

    [Fact]
    public async Task Stats_descriptor_uses_saved_slot_on_subsequent_activations()
    {
        using var f = new LayoutCoordinatorTestFixture();
        var sid = await f.InsertSessionWithPilotsAsync((101, "alice"), (102, "Bob"));
        f.PerPilotFixture.ActiveSession.Set(sid, SessionRunState.Running);
        await f.PerPilotCoord.HandleActiveSessionChangedAsync();

        // Pre-seed widget_layout with SessionStats at slot 2 (after both pilots).
        var seeded = new WidgetLayout(new[]
        {
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(101), Hidden: false),
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(102), Hidden: false),
            new WidgetLayoutItem(new WidgetDescriptor.SessionStats(), Hidden: false),
        });
        await f.SettingsRepo.SetWidgetLayoutAsync(seeded);

        using var coord = f.CreateCoordinator();
        await coord.LoadAsync();

        // Stats not yet activated; only pilots render.
        coord.Items.Should().NotContain(i => i.Descriptor is WidgetDescriptor.SessionStats);

        // Activate by Mode -> Running.
        f.SessionVm.Mode = SessionWidgetMode.Running;

        // Saved order: pilot101, pilot102, SessionStats.
        coord.Items.Should().HaveCount(3);
        coord.Items[0].DisplayName.Should().Be("alice");
        coord.Items[1].DisplayName.Should().Be("Bob");
        coord.Items[2].Descriptor.Should().BeOfType<WidgetDescriptor.SessionStats>();
    }
}
