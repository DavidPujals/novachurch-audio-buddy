using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaSetlist.Audio;
using NovaSetlist.Music;
using NovaSetlist.Services;
using NovaSetlist.Timecode;

namespace NovaSetlist.ViewModels;

/// <summary>
/// The LTC timecode viewer panel plus the now-playing countdown. Polls the
/// monitor on a fast render-priority timer so the readout advances every frame.
///
/// Now-playing flow: one ▶ click cues the song — the countdown waits for
/// timecode and starts when lock arrives. A second click starts a manual
/// (wall-clock) countdown immediately; if timecode arrives mid-song the
/// countdown re-anchors to it without jumping.
/// </summary>
public partial class TimecodeViewModel : ObservableObject, IDisposable
{
    public const string OffDevice = "No input — off";

    /// <summary>How long SIGNAL/LOCK keep showing after a momentary dropout, ms.
    /// Hides single-frame stutters without masking a real loss for long.</summary>
    private const long HoldMs = 700;

    /// <summary>"Long ago" sentinel for the hold-off timestamps. Must NOT be
    /// long.MinValue: (now - MinValue) overflows negative and reads as "just now",
    /// which lit SIGNAL/LOCK green from startup with no input at all.</summary>
    private const long NeverTick = -1_000_000_000;

    private readonly AppConfig _config;
    private readonly DispatcherTimer _timer;
    private LtcMonitor? _monitor;

    // Poll caches — rebuild display strings only when the underlying values change.
    private long _shownBits = long.MinValue;
    private double _shownFps = -1;
    private TimecodeRate _shownRate = (TimecodeRate)255;
    private long _lastLockTick = NeverTick;
    private long _lastSignalTick = NeverTick;

    // Now-playing countdown state.
    private double _lengthSeconds;
    private long _anchorWallTick;
    private long _anchorFrames = -1; // -1 = wall-clock anchor
    private int _shownCountdownSec = int.MinValue;

    public ObservableCollection<string> Devices { get; } = new();

    [ObservableProperty]
    private string selectedDevice = OffDevice;

    [ObservableProperty]
    private string timecodeText = "--:--:--:--";

    [ObservableProperty]
    private string rateText = "";

    /// <summary>Readout colour state: "off", "signal" (seen but not locked) or "locked".</summary>
    [ObservableProperty]
    private string tcState = "off";

    [ObservableProperty]
    private bool signalOn;

    [ObservableProperty]
    private bool lockOn;

    [ObservableProperty]
    private string nowPlayingName = "";

    /// <summary>"" (nothing), "cued" (waiting for timecode) or "playing".</summary>
    [ObservableProperty]
    private string playState = "";

    [ObservableProperty]
    private string countdownText = "";

    [ObservableProperty]
    private string countdownSub = "";

    /// <summary>"ok", "warn" (&lt;30 s left), "over" or "up" (counting up, no length).</summary>
    [ObservableProperty]
    private string countdownState = "ok";

    public TimecodeViewModel(AppConfig config)
    {
        _config = config;
        RefreshDevices();
        if (config.TimecodeDevice.Length > 0 && Devices.Contains(config.TimecodeDevice))
            SelectedDevice = config.TimecodeDevice; // triggers the monitor start

        // ~30 fps at render priority: one new LTC frame per tick at 25/30 fps,
        // so the readout advances smoothly instead of jumping 2-3 frames.
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) => Poll();
        if (_monitor is not null)
            _timer.Start();
    }

    /// <summary>Re-enumerates waveIn devices (called when the Settings dialog opens).</summary>
    public void RefreshDevices()
    {
        var names = new List<string> { OffDevice };
        names.AddRange(WdmInput.DeviceNames());

        if (names.SequenceEqual(Devices))
            return;
        var keep = SelectedDevice;
        Devices.Clear();
        foreach (var n in names)
            Devices.Add(n);
        SelectedDevice = Devices.Contains(keep) ? keep : OffDevice;
    }

    partial void OnSelectedDeviceChanged(string value)
    {
        StartMonitor(value);
        var persist = value == OffDevice ? "" : value;
        if (_config.TimecodeDevice != persist)
        {
            _config.TimecodeDevice = persist;
            _config.Save();
        }
    }

    private void StartMonitor(string deviceName)
    {
        _monitor?.Dispose();
        _monitor = null;
        _shownBits = long.MinValue;
        _shownFps = -1;
        _shownRate = (TimecodeRate)255;
        _lastLockTick = _lastSignalTick = NeverTick;
        TimecodeText = "--:--:--:--";
        RateText = "";
        TcState = "off";
        SignalOn = LockOn = false;

        // A countdown anchored to the old monitor's timecode can't survive a
        // device change — the wall anchor (kept continuous below) takes over.
        _anchorFrames = -1;

        if (string.IsNullOrEmpty(deviceName) || deviceName == OffDevice)
        {
            UpdateTimerGate();
            return;
        }

        try
        {
            var index = WdmInput.FindDevice(deviceName);
            if (index < 0)
            {
                RateText = "input not found";
                UpdateTimerGate();
                return;
            }
            _monitor = new LtcMonitor(index);
            _monitor.Start();
            RateText = "no LTC";
        }
        catch (Exception)
        {
            _monitor?.Dispose();
            _monitor = null;
            RateText = "couldn't open input";
        }
        UpdateTimerGate();
    }

    // ---------- now playing ----------

    /// <summary>First ▶ click: cue the song. If timecode is already locked the
    /// countdown starts on it right away; otherwise it waits for lock.</summary>
    public void Cue(string songName, double lengthSeconds)
    {
        NowPlayingName = songName;
        _lengthSeconds = lengthSeconds;
        _shownCountdownSec = int.MinValue;
        _anchorWallTick = Environment.TickCount64;
        _anchorFrames = -1;

        var m = _monitor;
        if (m is not null && m.Locked && m.CurrentBits >= 0 && m.MeasuredFps > 1)
        {
            _anchorFrames = LtcMonitor.FrameFromBits(m.CurrentBits).ToFrameNumber(m.DetectedRate);
            PlayState = "playing";
            UpdateCountdown();
        }
        else
        {
            PlayState = "cued";
            CountdownText = _lengthSeconds > 0 ? SongLength.Format(_lengthSeconds) : "—";
            CountdownState = "ok";
            CountdownSub = "waiting for timecode — ▶ again to start now";
        }
        UpdateTimerGate();
    }

    /// <summary>Second ▶ click while cued: start the countdown on the wall clock now.
    /// It re-anchors to timecode automatically if LTC arrives mid-song.</summary>
    public void StartManual()
    {
        if (PlayState != "cued")
            return;
        _anchorWallTick = Environment.TickCount64;
        _anchorFrames = -1;
        _shownCountdownSec = int.MinValue;
        PlayState = "playing";
        UpdateCountdown();
    }

    public void StopCountdown()
    {
        NowPlayingName = "";
        PlayState = "";
        CountdownText = "";
        CountdownSub = "";
        _anchorFrames = -1;
        UpdateTimerGate();
    }

    private void UpdateTimerGate()
    {
        // _timer is null only while the constructor's initial device restore runs.
        if (_timer is null)
            return;
        if (_monitor is not null || NowPlayingName.Length > 0)
            _timer.Start();
        else
            _timer.Stop();
    }

    // ---------- polling ----------

    private void Poll()
    {
        var m = _monitor;
        if (m?.Error is { } error)
        {
            // A dead input won't come back on its own — stop capturing.
            _monitor = null;
            m.Dispose();
            m = null;
            SignalOn = LockOn = false;
            TcState = "off";
            RateText = error;
            UpdateTimerGate();
        }

        if (m is not null)
            UpdateTimecode(m);

        if (NowPlayingName.Length > 0)
            UpdateCountdown();
    }

    private void UpdateTimecode(LtcMonitor m)
    {
        var now = Environment.TickCount64;
        var rawSignal = m.SignalPresent && m.MeasuredFps > 0;
        var rawLocked = m.Locked && rawSignal;
        if (rawSignal)
            _lastSignalTick = now;
        if (rawLocked)
            _lastLockTick = now;

        // Short hold: a one-frame dropout shouldn't blink the dots or dim the readout.
        var signal = rawSignal || now - _lastSignalTick < HoldMs;
        var locked = rawLocked || now - _lastLockTick < HoldMs;
        SignalOn = signal;
        LockOn = locked;
        TcState = locked ? "locked" : signal ? "signal" : "off";

        var bits = m.CurrentBits;
        if (bits >= 0 && bits != _shownBits)
        {
            _shownBits = bits;
            TimecodeText = LtcMonitor.FrameFromBits(bits).ToString();
        }

        if (!signal)
        {
            _shownFps = -1;
            RateText = "no LTC";
            return;
        }

        var fps = Math.Round(m.MeasuredFps, 2);
        if (fps > 0 && (fps != _shownFps || m.DetectedRate != _shownRate))
        {
            _shownFps = fps;
            _shownRate = m.DetectedRate;
            RateText = $"{fps:0.##} fps · {RateName(m.DetectedRate)}";
        }
    }

    private void UpdateCountdown()
    {
        var m = _monitor;
        var tc = m is not null && m.Locked && m.CurrentBits >= 0 && m.MeasuredFps > 1;
        long nowFrames = 0;
        if (tc)
            nowFrames = LtcMonitor.FrameFromBits(m!.CurrentBits).ToFrameNumber(m.DetectedRate);

        if (PlayState == "cued")
        {
            if (!tc)
                return; // keep showing the static "waiting for timecode" display
            _anchorFrames = nowFrames;
            _anchorWallTick = Environment.TickCount64;
            _shownCountdownSec = int.MinValue;
            PlayState = "playing";
        }

        double elapsed;
        if (tc && _anchorFrames >= 0)
        {
            elapsed = Math.Max(0, (nowFrames - _anchorFrames) / m!.MeasuredFps);
            // Keep the wall anchor in step so a timecode dropout mid-song
            // hands over to the wall clock without a jump.
            _anchorWallTick = Environment.TickCount64 - (long)(elapsed * 1000);
        }
        else
        {
            elapsed = (Environment.TickCount64 - _anchorWallTick) / 1000.0;
            // Manual countdown and timecode just arrived: adopt it, keeping continuity.
            if (tc && _anchorFrames < 0)
                _anchorFrames = nowFrames - (long)(elapsed * m!.MeasuredFps);
        }

        if (_lengthSeconds > 0)
        {
            var remaining = _lengthSeconds - elapsed;
            var second = (int)Math.Ceiling(remaining);
            if (second == _shownCountdownSec)
                return;
            _shownCountdownSec = second;

            if (remaining >= 0)
            {
                CountdownText = SongLength.Format(remaining);
                CountdownState = remaining <= 30 ? "warn" : "ok";
                CountdownSub = $"of {SongLength.Format(_lengthSeconds)}";
            }
            else
            {
                CountdownText = "+" + SongLength.Format(-remaining);
                CountdownState = "over";
                CountdownSub = $"over — song is {SongLength.Format(_lengthSeconds)}";
            }
        }
        else
        {
            var second = (int)elapsed;
            if (second == _shownCountdownSec)
                return;
            _shownCountdownSec = second;
            CountdownText = SongLength.Format(elapsed);
            CountdownState = "up";
            CountdownSub = "elapsed — no length in the sheet";
        }
    }

    private static string RateName(TimecodeRate rate) => rate switch
    {
        TimecodeRate.Film24 => "24 · Film",
        TimecodeRate.Ebu25 => "25 · EBU",
        TimecodeRate.Df2997 => "29.97 · Drop-frame",
        _ => "30 · SMPTE",
    };

    public void Dispose()
    {
        _timer.Stop();
        _monitor?.Dispose();
        _monitor = null;
    }
}
