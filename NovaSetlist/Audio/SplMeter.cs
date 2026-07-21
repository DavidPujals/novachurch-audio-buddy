using System.Runtime.InteropServices;
using NAudio.Wave;
using NovaSetlist.Timecode;

namespace NovaSetlist.Audio;

/// <summary>
/// Live A-weighted level meter from a WDM (waveIn) input: capture → ring buffer →
/// worker thread running the A-weighting chain and an exponential time weighting
/// (Slow 1 s / Fast 125 ms, switchable live). Same shape and hardening as the LTC
/// monitor and key detector.
///
/// Publishes the A-weighted level in dBFS; the view model adds the user's
/// calibration offset to turn it into dB SPL.
/// </summary>
public sealed class SplMeter : IDisposable
{
    private readonly WaveInEvent _waveIn;
    private readonly FloatRingBuffer _ring = new(1 << 17);
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly AWeighting _weighting;
    private float[] _conv = Array.Empty<float>();
    private volatile bool _stop;
    private volatile string? _error;

    private double _meanSquare = 1e-20;
    private volatile float _alphaPerSample;
    private int _milliDb = -120_000; // A-weighted level, dBFS × 1000 (lock-free)

    public string Description { get; }
    public string? Error => _error;

    /// <summary>Current A-weighted level in dBFS (time-weighted).</summary>
    public double LevelDbfs => Volatile.Read(ref _milliDb) / 1000.0;

    public SplMeter(int deviceNumber, bool fastResponse)
    {
        var caps = WaveInEvent.GetCapabilities(deviceNumber);
        _channels = Math.Clamp(caps.Channels, 1, 2);
        _sampleRate = WdmInput.GuessEndpointRate(caps.ProductName) ?? 48000;
        _weighting = new AWeighting(_sampleRate);
        SetFastResponse(fastResponse);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(_sampleRate, 16, _channels),
            BufferMilliseconds = 10,
            NumberOfBuffers = 12,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += (_, e) =>
        {
            if (!_stop)
                _error = e.Exception is null ? "Input stopped" : "Input error — device lost?";
        };

        Description = $"{caps.ProductName} · {_sampleRate / 1000.0:0.#} kHz";
        _worker = new Thread(MeterLoop) { IsBackground = true, Name = "SPL meter" };
    }

    /// <summary>Switch the time weighting live: Fast = 125 ms, Slow = 1 s.</summary>
    public void SetFastResponse(bool fast) =>
        _alphaPerSample = (float)(1 - Math.Exp(-1.0 / ((fast ? 0.125 : 1.0) * _sampleRate)));

    public void Start()
    {
        _worker.Start();
        _waveIn.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var src = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
        int frames = src.Length / _channels;
        if (frames <= 0)
            return;
        if (_conv.Length < frames)
            _conv = new float[frames];

        for (var i = 0; i < frames; i++)
            _conv[i] = src[i * _channels] * (1f / 32768f); // channel 1

        _ring.Write(_conv.AsSpan(0, frames));
        try { _signal.Set(); }
        catch (ObjectDisposedException) { /* torn down while a late callback was in flight */ }
    }

    private void MeterLoop()
    {
        var buf = new float[4096];
        try
        {
            while (!_stop)
            {
                _signal.WaitOne(100);
                int n;
                while (!_stop && (n = _ring.Read(buf)) > 0)
                {
                    double alpha = _alphaPerSample;
                    var ms = _meanSquare;
                    for (var i = 0; i < n; i++)
                    {
                        double x = buf[i];
                        if (!double.IsFinite(x)) x = 0;
                        else if (x > 16) x = 16;
                        else if (x < -16) x = -16;
                        var w = _weighting.Process(x);
                        ms += (w * w - ms) * alpha;
                    }
                    if (ms < 1e-20 || !double.IsFinite(ms)) ms = 1e-20;
                    _meanSquare = ms;
                    Volatile.Write(ref _milliDb, (int)Math.Round(10.0 * Math.Log10(ms) * 1000));
                }
            }
        }
        catch (Exception)
        {
            _error = "Meter stopped unexpectedly — reselect the input";
        }
    }

    public void Dispose()
    {
        _stop = true;
        try { _waveIn.StopRecording(); } catch { /* device already gone */ }
        _waveIn.Dispose();
        try { _signal.Set(); } catch (ObjectDisposedException) { }
        if (_worker.IsAlive)
            _worker.Join(1000);
        _signal.Dispose();
    }
}
