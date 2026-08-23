namespace EveDashboard.App.Wpf.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

/// <summary>
/// Plays the auto-pause and auto-resume notification WAVs. The
/// two WAV files (<c>Miners_have_stopped.wav</c> for auto-pause and
/// <c>Mining_detected.wav</c> for auto-resume) ship as <c>&lt;Resource&gt;</c>
/// entries in the WPF csproj and are loaded once into <c>byte[]</c> buffers at
/// construction time. Each <c>Play*</c> call opens a fresh <see cref="NAudio.Wave.WaveOutEvent"/>
/// over the embedded bytes and applies the cached volume; <see cref="NAudio.Wave.WaveOutEvent"/>
/// plays on its own background thread (non-blocking, not dispatcher-bound).
/// </summary>
/// <remarks>
/// <para>
/// NAudio replaced the original <c>System.Media.SoundPlayer</c> engine (which has
/// no volume API) so the <c>auto_pause_sound_volume</c> setting can
/// apply a real per-sound gain. The WAVs stay embedded in the single-file EXE; the
/// bytes are read once via <see cref="Application.GetResourceStream(Uri)"/>.
/// </para>
/// <para>
/// Two cached settings gate / shape playback: <c>auto_pause_sound_enabled</c>
/// (the master on/off, <see cref="_soundEnabled"/>) and <c>auto_pause_sound_volume</c>
/// (percent 0-100, <see cref="_soundVolume"/>). Both are hydrated once at
/// construction and refreshed via <see cref="ISettingsRepository.SettingChanged"/>
/// so Settings -&gt; Apply takes effect immediately. <see cref="PlayIfEnabled"/>
/// reads the cached values synchronously without async/await — a prior
/// implementation used <c>GetAwaiter().GetResult()</c> on the UI thread and
/// deadlocked when the underlying repo's continuation tried to resume on the
/// captured <see cref="SynchronizationContext"/>.
/// </para>
/// <para>
/// All exceptions inside the load/play paths are caught and logged at
/// <c>Warning</c>: a missing audio device, a corrupt WAV, or a settings-read
/// failure degrades the feature to silent — the session lifecycle (auto-pause /
/// auto-resume) continues normally. Each play owns its <see cref="NAudio.Wave.WaveOutEvent"/>
/// and disposes it (plus the reader + stream) on <c>PlaybackStopped</c>, so
/// overlapping plays are independent and nothing leaks.
/// </para>
/// </remarks>
public sealed class AutoPauseSoundPlayer
{
    private const string PauseWavPackUri  = "pack://application:,,,/Assets/Miners_have_stopped.wav";
    private const string ResumeWavPackUri = "pack://application:,,,/Assets/Mining_detected.wav";

    private readonly byte[]? _pauseWav;
    private readonly byte[]? _resumeWav;
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

    /// <summary>
    /// Cached volume percent in <c>[0, 100]</c>. <c>Volatile</c> for the same
    /// cross-thread reason as <see cref="_soundEnabled"/>. Default <c>65</c>
    /// matches the seeded setting default so a hydration failure plays at a
    /// sensible level rather than silent or blaring.
    /// </summary>
    private int _soundVolume = 65;

    /// <summary>Construct the player and pre-load both WAVs.</summary>
    /// <param name="settings">Settings repository. The cached on/off + volume values are hydrated once and refreshed via <see cref="ISettingsRepository.SettingChanged"/>.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public AutoPauseSoundPlayer(ISettingsRepository settings, ILogger<AutoPauseSoundPlayer> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
        _pauseWav  = TryLoadBytes(PauseWavPackUri,  "auto-pause");
        _resumeWav = TryLoadBytes(ResumeWavPackUri, "auto-resume");

        // Fire-and-forget initial hydration. Any failure leaves the defaults
        // (enabled, 65%). ConfigureAwait(false) so we don't bounce back to
        // whatever SynchronizationContext was current at ctor-time.
        _ = HydrateAsync();

        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>Play the auto-pause notification sound (gated by the cached on/off setting, scaled by the cached volume).</summary>
    public void PlayPauseSound()  => PlayIfEnabled(_pauseWav,  "auto-pause");

    /// <summary>Play the auto-resume notification sound (gated by the cached on/off setting, scaled by the cached volume).</summary>
    public void PlayResumeSound() => PlayIfEnabled(_resumeWav, "auto-resume");

    private void PlayIfEnabled(byte[]? wav, string kind)
    {
        if (wav is null) return; // load failed — already logged at construction
        if (Volatile.Read(ref _soundEnabled) == 0) return;
        var volume = Volatile.Read(ref _soundVolume); // percent 0-100

        try
        {
            // Fresh stream + reader + output per play so overlapping plays don't
            // share mutable engine state. All three are disposed on PlaybackStopped.
            var ms = new MemoryStream(wav, writable: false);
            var reader = new WaveFileReader(ms);
            var sampleProvider = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = volume / 100f,
            };
            var output = new WaveOutEvent();
            output.Init(sampleProvider);
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                reader.Dispose();
                ms.Dispose();
            };
            output.Play();
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
            var volume = await _settings.GetAutoPauseSoundVolumeAsync().ConfigureAwait(false);
            Volatile.Write(ref _soundVolume, volume);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to hydrate auto-pause sound settings at startup; defaulting to enabled at 65%.");
        }
    }

    private async void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        try
        {
            if (e.Key == "auto_pause_sound_enabled")
            {
                var enabled = await _settings.GetAutoPauseSoundEnabledAsync().ConfigureAwait(false);
                Volatile.Write(ref _soundEnabled, enabled ? 1 : 0);
            }
            else if (e.Key == "auto_pause_sound_volume")
            {
                var volume = await _settings.GetAutoPauseSoundVolumeAsync().ConfigureAwait(false);
                Volatile.Write(ref _soundVolume, volume);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to refresh auto-pause sound settings on SettingChanged for key {Key}.", e.Key);
        }
    }

    /// <summary>
    /// Resolve a pack:// resource into the raw WAV bytes. Returns null on any
    /// failure (missing resource, IO error); the caller treats null as "silent"
    /// and never throws. The bytes are re-wrapped in a fresh <see cref="MemoryStream"/>
    /// on each play so concurrent plays do not share a seek position.
    /// </summary>
    private byte[]? TryLoadBytes(string packUri, string kind)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))
                ?? throw new InvalidOperationException($"Resource not found: {packUri}");

            using var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AutoPauseSoundPlayer failed to load {Kind} WAV from {PackUri}; that sound will be silent.", kind, packUri);
            return null;
        }
    }
}
