using System.Numerics;

namespace NovaSetlist.Audio;

/// <summary>
/// IEC 61672 A-weighting filter for arbitrary sample rates: the analog A-curve
/// factored into six cascaded first-order sections (two high-pass at 20.6 Hz,
/// high-pass at 107.7 Hz and 737.9 Hz, two low-pass at 12194 Hz), each
/// bilinear-transformed, with the overall gain normalized to exactly 0 dB at
/// 1 kHz by evaluating the digital cascade's response there.
///
/// Pure DSP, single-threaded: call Process() from one thread only.
/// </summary>
public sealed class AWeighting
{
    private const int Sections = 6;
    private readonly double[] _b0 = new double[Sections];
    private readonly double[] _b1 = new double[Sections];
    private readonly double[] _a1 = new double[Sections];
    private readonly double[] _x1 = new double[Sections];
    private readonly double[] _y1 = new double[Sections];
    private readonly double _gain;

    public AWeighting(int sampleRate)
    {
        if (sampleRate < 8000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        double c = 2.0 * sampleRate;
        // Prewarp each corner so the poles land at their analog frequencies — without
        // it the 12.2 kHz sections sag ~2.5 dB at 12.5 kHz at 44.1/48 kHz rates.
        double Warp(double f) => c * Math.Tan(Math.PI * f / sampleRate);
        var i = 0;
        void HighPass(double f)
        {
            double w = Warp(f), d = c + w;
            _b0[i] = c / d;
            _b1[i] = -c / d;
            _a1[i] = (w - c) / d;
            i++;
        }
        void LowPass(double f)
        {
            double w = Warp(f), d = c + w;
            _b0[i] = w / d;
            _b1[i] = w / d;
            _a1[i] = (w - c) / d;
            i++;
        }

        HighPass(20.598997);
        HighPass(20.598997);
        HighPass(107.65265);
        HighPass(737.86223);
        LowPass(12194.217);
        LowPass(12194.217);

        _gain = 1.0 / MagnitudeAt(1000.0, sampleRate);
    }

    /// <summary>Magnitude of the (un-normalized) digital cascade at a frequency.</summary>
    private double MagnitudeAt(double freq, int sampleRate)
    {
        var z1 = Complex.Exp(new Complex(0, -2 * Math.PI * freq / sampleRate)); // z^-1
        Complex h = 1;
        for (var k = 0; k < Sections; k++)
            h *= (_b0[k] + _b1[k] * z1) / (1 + _a1[k] * z1);
        return h.Magnitude;
    }

    /// <summary>Runs one sample through the weighting chain.</summary>
    public double Process(double x)
    {
        x *= _gain;
        for (var k = 0; k < Sections; k++)
        {
            var y = _b0[k] * x + _b1[k] * _x1[k] - _a1[k] * _y1[k];
            // Flush denormals — a decaying IIR tail otherwise costs ~100× per op.
            if (y is > -1e-25 and < 1e-25) y = 0;
            _x1[k] = x;
            _y1[k] = y;
            x = y;
        }
        return x;
    }
}
