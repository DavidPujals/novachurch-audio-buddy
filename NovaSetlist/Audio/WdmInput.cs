using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NovaSetlist.Audio;

/// <summary>Shared waveIn (WDM) helpers for the LTC monitor and the key detector.</summary>
internal static class WdmInput
{
    /// <summary>All waveIn device product names, in device order. Never throws.</summary>
    public static List<string> DeviceNames()
    {
        var names = new List<string>();
        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
                names.Add(WaveInEvent.GetCapabilities(i).ProductName);
        }
        catch { /* no waveIn support */ }
        return names;
    }

    public static int FindDevice(string productName)
    {
        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
                if (WaveInEvent.GetCapabilities(i).ProductName == productName)
                    return i;
        }
        catch { }
        return -1;
    }

    /// <summary>Finds the WASAPI endpoint behind a waveIn device (waveIn names are the
    /// endpoint name truncated to 31 chars) and returns its native mix rate, so capture
    /// runs without a sample-rate conversion in the path.</summary>
    public static int? GuessEndpointRate(string waveInName)
    {
        try
        {
            var prefix = waveInName.Trim();
            if (prefix.Length == 0)
                return null;
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    if (device.FriendlyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        int rate = device.AudioClient.MixFormat.SampleRate;
                        if (rate is >= 8000 and <= 384000)
                            return rate;
                    }
                }
            }
        }
        catch { /* endpoint lookup is best-effort; 48 kHz fallback still works */ }
        return null;
    }
}
