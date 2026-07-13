using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaSetlist.Audio;
using NovaSetlist.Services;

namespace NovaSetlist.ViewModels;

/// <summary>
/// The live key-detection panel: pick a WDM input in Settings, see the key the
/// band is playing in. Confidence-gated with a short display hold so the
/// readout doesn't flicker between neighbouring keys.
/// </summary>
public partial class KeyDetectViewModel : ObservableObject, IDisposable
{
    public const string OffDevice = "No input — off";

    private const double MinConfidence = 0.04; // correlation margin best vs runner-up
    private const double MinLevel = 0.002;     // ~-54 dBFS RMS
    private const long HoldMs = 5000;          // keep the last confident key this long

    private readonly AppConfig _config;
    private readonly DispatcherTimer _timer;
    private KeyDetector? _detector;
    private long _lastGoodTick = long.MinValue;
    private string _lastShownKey = "";

    public ObservableCollection<string> Devices { get; } = new();

    [ObservableProperty]
    private string selectedDevice = OffDevice;

    [ObservableProperty]
    private string keyText = "—";

    [ObservableProperty]
    private string keySub = "off";

    /// <summary>"off", "listening" or "detected" — drives the readout colour.</summary>
    [ObservableProperty]
    private string keyState = "off";

    public KeyDetectViewModel(AppConfig config)
    {
        _config = config;
        RefreshDevices();
        if (config.KeyDetectDevice.Length > 0 && Devices.Contains(config.KeyDetectDevice))
            SelectedDevice = config.KeyDetectDevice; // triggers the detector start

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Poll();
        if (_detector is not null)
            _timer.Start();
    }

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
        StartDetector(value);
        var persist = value == OffDevice ? "" : value;
        if (_config.KeyDetectDevice != persist)
        {
            _config.KeyDetectDevice = persist;
            _config.Save();
        }
    }

    private void StartDetector(string deviceName)
    {
        _detector?.Dispose();
        _detector = null;
        _timer?.Stop();
        _lastGoodTick = long.MinValue;
        _lastShownKey = "";
        KeyText = "—";
        KeySub = "off";
        KeyState = "off";

        if (string.IsNullOrEmpty(deviceName) || deviceName == OffDevice)
            return;

        try
        {
            var index = WdmInput.FindDevice(deviceName);
            if (index < 0)
            {
                KeySub = "input not found";
                return;
            }
            _detector = new KeyDetector(index);
            _detector.Start();
            KeySub = "listening…";
            KeyState = "listening";
            _timer?.Start();
        }
        catch (Exception)
        {
            _detector?.Dispose();
            _detector = null;
            KeySub = "couldn't open the input";
        }
    }

    private void Poll()
    {
        var d = _detector;
        if (d is null)
        {
            _timer.Stop();
            return;
        }

        if (d.Error is { } error)
        {
            _detector = null;
            d.Dispose();
            _timer.Stop();
            KeyText = "—";
            KeySub = error;
            KeyState = "off";
            return;
        }

        var (key, minor, confidence, level) = d.Estimator.Snapshot();
        var now = Environment.TickCount64;

        if (key >= 0 && level >= MinLevel && confidence >= MinConfidence)
        {
            var name = KeyEstimator.KeyNames[key] + (minor ? "m" : "");
            _lastGoodTick = now;
            if (name != _lastShownKey)
            {
                _lastShownKey = name;
                KeyText = name;
                KeySub = minor ? "minor" : "major";
            }
            KeyState = "detected";
            return;
        }

        // Hold the last confident key briefly so quiet passages don't blank it.
        if (now - _lastGoodTick < HoldMs && _lastShownKey.Length > 0)
            return;

        _lastShownKey = "";
        KeyText = "—";
        KeySub = level < MinLevel ? "listening… (no audio)" : "listening…";
        KeyState = "listening";
    }

    public void Dispose()
    {
        _timer.Stop();
        _detector?.Dispose();
        _detector = null;
    }
}
