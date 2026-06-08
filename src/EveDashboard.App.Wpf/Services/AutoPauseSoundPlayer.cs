namespace EveDashboard.App.Wpf.Services;

using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 37: plays the auto-pause and auto-resume notification WAVs. The two
/// WAV files (<c>Miners_have_stopped.wav</c> for auto-pause and
/// <c>Mining_detected.wav</c> for auto-resume) ship as <c>&lt;Resource&gt;</c>
/// entries in the WPF csproj and are loaded once into <see cref="MemoryStream"/>
/// instances at construction time; each is wrapped in a long-lived
/// <see cref="SoundPlayer"/>. The <c>Play*</c> calls are non-blocking
/// (<see cref="SoundPlayer.Play"/> plays on a thread-pool thread).
/// </summary>
/// <remarks>
/// <para>
/// Sound playback is gated by the <c>auto_pause_sound_enabled</c> setting.
/// The value is cached in memory (<see cref="_soundEnabled"/>); hydrated once
/// at construction and refreshed via
/// <see cref="ISettingsRepository.SettingChanged"/> so Settings -&gt; Apply
/// takes effect immediately. <see cref="PlayIfEnabled"/> reads the cached
/// value synchronously without async/await — bug-1 fix: a prior implementation
/// used <c>GetAwaiter().GetResult()</c> on the UI thread and deadlocked when
/// the underlying repo's continuation tried to resume on the captured
/// <see cref="SynchronizationContext"/>.
/// </para>
/// <para>
/// All exceptions inside Play/load paths are caught and logged at
/// <c>Warning</c>: a missing codec, a corrupt WAV, or a settings-read failure
/// degrades the feature to silent — the session lifecycle (auto-pause /
/// auto-resume) continues normally.
/// </para>
/// </remarks>
public sealed class AutoPauseSoundPlayer
{
    private const string PauseWavPackUri  = "pack://application:,,,/Assets/Miners_have_stopped.wav";
    private const string ResumeWavPackUri = "pack://application:,,,/Assets/Mining_detected.wav";

    private readonly SoundPlayer? _pause;
    private readonly SoundPlayer? _resume;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<AutoPauseSoundPlayer> _log;

    /// <summary>
    /// Cached on/off flag. <c>1</c> = enabled, <c>0</c> = disabled.
    /// <c>Volatile.Read</c> / <c>Volatile.Write</c> give the cross-thread
    /// visibility we need (writer-callback thread vs UI thread can both read
    /// and write). Default of <c>1</c> means a hydration failure does not
    /// silently mute the user.
    /// </summary>
    private int _soundEnabled = 1;

    /// <summary>Construct the player and pre-load both WAVs.</summary>
    /// <param name="settings">Settings repository. The cached on/off value is hydrated once and refreshed via <see cref="ISettingsRepository.SettingChanged"/>.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public AutoPauseSoundPlayer(ISettingsRepository settings, ILogger<AutoPauseSoundPlayer> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
        _pause  = TryLoad(PauseWavPackUri,  "auto-pause");
        _resume = TryLoad(ResumeWavPackUri, "auto-resume");

        // Fire-and-forget initial hydration. Any failure leaves the default
        // (enabled). ConfigureAwait(false) so we don't bounce back to whatever
        // SynchronizationContext was current at ctor-time.
        _ = HydrateAsync();

        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>Play the auto-pause notification sound (gated by the cached on/off setting).</summary>
    public void PlayPauseSound()  => PlayIfEnabled(_pause,  "auto-pause");

    /// <summary>Play the auto-resume notification sound (gated by the cached on/off setting).</summary>
    public void PlayResumeSound() => PlayIfEnabled(_resume, "auto-resume");

    private void PlayIfEnabled(SoundPlayer? player, string kind)
    {
        if (player is null) return; // load failed — already logged at construction
        if (Volatile.Read(ref _soundEnabled) == 0) return;

        try
        {
            player.Play();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auto-pause {Kind} sound playback failed.", kind);
        }
    }

    private async Task HydrateAsync()
    {
        try
        {
            var enabled = await _settings.GetAutoPauseSoundEnabledAsync().ConfigureAwait(false);
            Volatile.Write(ref _soundEnabled, enabled ? 1 : 0);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to hydrate auto_pause_sound_enabled at startup; defaulting to enabled.");
        }
    }

    private async void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key != "auto_pause_sound_enabled") return;
        try
        {
            var enabled = await _settings.GetAutoPauseSoundEnabledAsync().ConfigureAwait(false);
            Volatile.Write(ref _soundEnabled, enabled ? 1 : 0);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to refresh auto_pause_sound_enabled on SettingChanged.");
        }
    }

    /// <summary>
    /// Resolve a pack:// resource into a <see cref="SoundPlayer"/> backed by a
    /// <see cref="MemoryStream"/> copy of the WAV bytes. Returns null on any
    /// failure (missing resource, IO error, malformed WAV); the caller treats
    /// null as "silent" and never throws.
    /// </summary>
    private SoundPlayer? TryLoad(string packUri, string kind)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))
                ?? throw new InvalidOperationException($"Resource not found: {packUri}");

            // Copy to MemoryStream so the SoundPlayer can re-load on each Play.
            // The original Stream from GetResourceStream is read-once for some
            // resource types; MemoryStream is seekable and safe for repeated Play().
            var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            ms.Position = 0;

            var player = new SoundPlayer(ms);
            player.LoadAsync(); // pre-decode in background; Play() blocks if not yet loaded
            return player;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AutoPauseSoundPlayer failed to load {Kind} WAV from {PackUri}; that sound will be silent.", kind, packUri);
            return null;
        }
    }
}
