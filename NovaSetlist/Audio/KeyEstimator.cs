namespace NovaSetlist.Audio;

/// <summary>
/// Estimates the musical key of an audio stream: Hann-windowed FFT → 12-bin
/// chromagram (smoothed over a few seconds) → correlation against the
/// Krumhansl-Schmuckler major/minor key profiles.
///
/// Pure DSP, no audio I/O — feed samples via Process() from any thread (one at
/// a time); read Snapshot() from any other thread.
/// </summary>
public sealed class KeyEstimator
{
    // Temperley (Kostka-Payne) key profiles, C-based, index 0 = tonic. Chosen over
    // the classic Krumhansl-Schmuckler set because they are far less prone to
    // reporting the relative minor for major-key chord loops (G-D-Em-C etc.).
    private static readonly double[] MajorProfile =
        { 5.0, 2.0, 3.5, 2.0, 4.5, 4.0, 2.0, 4.5, 2.0, 3.5, 1.5, 4.0 };
    private static readonly double[] MinorProfile =
        { 5.0, 2.0, 3.5, 4.5, 2.0, 4.0, 2.0, 4.5, 3.5, 2.0, 1.5, 4.0 };

    /// <summary>Pitch-class names as shown in the app (index 0 = C).</summary>
    public static readonly string[] KeyNames =
        { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly int _hop;
    private readonly double[] _window;
    private readonly float[] _fill;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly int[] _binPitchClass;   // FFT bin → pitch class, -1 = outside band
    private readonly double[] _binWeight;    // bass bins weigh more — the bass carries the root
    private readonly double[] _chroma = new double[12]; // smoothed chromagram
    private int _filled;

    // Published results (packed for lock-free cross-thread reads).
    private int _key = -1;          // 0..11, -1 = none yet
    private int _minor;             // 0/1
    private int _confMilli;         // correlation margin ×1000
    private int _levelMilli;        // frame RMS ×1000

    public KeyEstimator(int sampleRate)
    {
        _sampleRate = sampleRate;
        // ~1/3 s analysis window: 16384 @ 48 kHz → 2.9 Hz bins, enough to separate
        // semitones down to ~65 Hz (low C on a bass).
        _fftSize = 1;
        while (_fftSize < sampleRate / 3)
            _fftSize <<= 1;
        _hop = _fftSize / 2;

        _window = new double[_fftSize];
        for (var i = 0; i < _fftSize; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (_fftSize - 1));

        _fill = new float[_fftSize];
        _re = new double[_fftSize];
        _im = new double[_fftSize];

        // Precompute bin → pitch class over the musically useful band.
        _binPitchClass = new int[_fftSize / 2];
        _binWeight = new double[_fftSize / 2];
        for (var bin = 0; bin < _fftSize / 2; bin++)
        {
            double f = (double)bin * sampleRate / _fftSize;
            if (f is < 55 or > 1500)
            {
                _binPitchClass[bin] = -1;
                continue;
            }
            // A4 = 440 Hz is pitch class 9 (A); +57 keeps the value positive before mod.
            var pc = ((int)Math.Round(12 * Math.Log2(f / 440.0)) + 57) % 12;
            _binPitchClass[bin] = pc;
            // The bass register carries the chord roots — the strongest tonality cue,
            // and what separates a key from its relative major/minor.
            _binWeight[bin] = f < 220 ? 2.5 : 1.0;
        }
    }

    /// <summary>(key 0..11 or -1, minor, confidence = correlation margin, input level RMS).</summary>
    public (int Key, bool Minor, double Confidence, double Level) Snapshot()
    {
        return (Volatile.Read(ref _key),
                Volatile.Read(ref _minor) != 0,
                Volatile.Read(ref _confMilli) / 1000.0,
                Volatile.Read(ref _levelMilli) / 1000.0);
    }

    public void Process(ReadOnlySpan<float> samples)
    {
        while (samples.Length > 0)
        {
            var take = Math.Min(samples.Length, _fftSize - _filled);
            samples[..take].CopyTo(_fill.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];

            if (_filled == _fftSize)
            {
                Analyze();
                Array.Copy(_fill, _hop, _fill, 0, _fftSize - _hop);
                _filled = _fftSize - _hop;
            }
        }
    }

    private void Analyze()
    {
        double sumSq = 0;
        for (var i = 0; i < _fftSize; i++)
        {
            double x = _fill[i];
            if (!double.IsFinite(x)) x = 0;
            sumSq += x * x;
            _re[i] = x * _window[i];
            _im[i] = 0;
        }
        var rms = Math.Sqrt(sumSq / _fftSize);
        Volatile.Write(ref _levelMilli, (int)Math.Clamp(rms * 1000, 0, 1_000_000));

        // Too quiet to mean anything — decay the chroma so stale keys fade out.
        if (rms < 0.001)
        {
            for (var i = 0; i < 12; i++)
                _chroma[i] *= 0.9;
            Publish();
            return;
        }

        Fft(_re, _im);

        Span<double> frame = stackalloc double[12];
        for (var bin = 1; bin < _fftSize / 2; bin++)
        {
            var pc = _binPitchClass[bin];
            if (pc < 0)
                continue;
            var mag = Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]);
            frame[pc] += mag * _binWeight[bin];
        }

        double total = 0;
        for (var i = 0; i < 12; i++)
            total += frame[i];
        if (total <= 0)
            return;

        // Normalized frame smoothed into the running chromagram. The time constant
        // (~6 s at the 0.17 s hop) matters: too short and the current chord wins —
        // a progression ending on Em reads as Em instead of G. The key should
        // reflect the tonal centre of the last phrase, not the last strum.
        for (var i = 0; i < 12; i++)
            _chroma[i] = 0.97 * _chroma[i] + 0.03 * (frame[i] / total);

        Publish();
    }

    private void Publish()
    {
        double best = double.MinValue, second = double.MinValue;
        int bestKey = -1, bestMinor = 0;
        for (var tonic = 0; tonic < 12; tonic++)
        {
            for (var minor = 0; minor < 2; minor++)
            {
                var r = Correlate(_chroma, minor == 1 ? MinorProfile : MajorProfile, tonic);
                if (r > best)
                {
                    second = best;
                    best = r;
                    bestKey = tonic;
                    bestMinor = minor;
                }
                else if (r > second)
                {
                    second = r;
                }
            }
        }

        Volatile.Write(ref _key, bestKey);
        Volatile.Write(ref _minor, bestMinor);
        Volatile.Write(ref _confMilli, (int)Math.Clamp((best - second) * 1000, -1000, 1000));
    }

    /// <summary>Pearson correlation of the chromagram against a profile rotated to a tonic.</summary>
    private static double Correlate(double[] chroma, double[] profile, int tonic)
    {
        double mc = 0, mp = 0;
        for (var i = 0; i < 12; i++)
        {
            mc += chroma[i];
            mp += profile[i];
        }
        mc /= 12;
        mp /= 12;

        double num = 0, dc = 0, dp = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[i] - mc;
            var p = profile[(i - tonic + 12) % 12] - mp;
            num += c * p;
            dc += c * c;
            dp += p * p;
        }
        var den = Math.Sqrt(dc * dp);
        return den < 1e-12 ? 0 : num / den;
    }

    /// <summary>In-place iterative radix-2 complex FFT.</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wRe = Math.Cos(ang);
            var wIm = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    var tRe = re[b] * curRe - im[b] * curIm;
                    var tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    var nRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                }
            }
        }
    }
}
