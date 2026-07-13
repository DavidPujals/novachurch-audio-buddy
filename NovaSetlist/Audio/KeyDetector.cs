using System.Runtime.InteropServices;
using NAudio.Wave;
using NovaSetlist.Timecode;

namespace NovaSetlist.Audio;

/// <summary>
/// Live key detection from a WDM (waveIn) input: capture → ring buffer →
/// worker thread feeding a <see cref="KeyEstimator"/>. Same shape and
/// hardening as the LTC monitor.
/// </summary>
public sealed class KeyDetector : IDisposable
{
    private readonly WaveInEvent _waveIn;
    private readonly FloatRingBuffer _ring = new(1 << 17);
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private readonly int _channels;
    private float[] _conv = Array.Empty<float>();
    private volatile bool _stop;
    private volatile string? _error;

    public KeyEstimator Estimator { get; }
    public string Description { get; }
    public string? Error => _error;

    public KeyDetector(int deviceNumber)
    {
        var caps = WaveInEvent.GetCapabilities(deviceNumber);
        _channels = Math.Clamp(caps.Channels, 1, 2);
        int sampleRate = WdmInput.GuessEndpointRate(caps.ProductName) ?? 48000;

        Estimator = new KeyEstimator(sampleRate);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(sampleRate, 16, _channels),
            BufferMilliseconds = 10,
            NumberOfBuffers = 12,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += (_, e) =>
        {
            if (!_stop)
                _error = e.Exception is null ? "Input stopped" : "Input error — device lost?";
        };

        Description = $"{caps.ProductName} · {sampleRate / 1000.0:0.#} kHz";
        _worker = new Thread(AnalyzeLoop) { IsBackground = true, Name = "Key detect" };
    }

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

    private void AnalyzeLoop()
    {
        var buf = new float[4096];
        try
        {
            while (!_stop)
            {
                _signal.WaitOne(100);
                int n;
                while (!_stop && (n = _ring.Read(buf)) > 0)
                    Estimator.Process(buf.AsSpan(0, n));
            }
        }
        catch (Exception)
        {
            _error = "Key detector stopped unexpectedly — reselect the input";
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
