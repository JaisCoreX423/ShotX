using System.IO;
using System.Media;

namespace ShotX.Services;

/// <summary>Synthesizes a short camera-shutter-like click in memory, no asset file needed.</summary>
public static class ShutterSound
{
    private static byte[]? _wavData;

    public static void Play()
    {
        try
        {
            _wavData ??= Build();
            using var stream = new MemoryStream(_wavData);
            using var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // A missing audio device should never crash a capture.
        }
    }

    private static byte[] Build()
    {
        const int sampleRate = 44100;
        const double durationSeconds = 0.09;
        var sampleCount = (int)(sampleRate * durationSeconds);
        var samples = new short[sampleCount];

        var random = new Random(1);
        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / sampleCount;
            var envelope = Math.Exp(-t * 18); // fast decay = "click"
            var noise = (random.NextDouble() * 2 - 1) * 0.5;
            var tone = Math.Sin(2 * Math.PI * 1800 * i / sampleRate) * 0.5;
            samples[i] = (short)(short.MaxValue * 0.6 * envelope * (noise + tone));
        }

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            var dataSize = sampleCount * 2;
            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + dataSize);
            bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray());
            bw.Write(16);
            bw.Write((short)1);   // PCM
            bw.Write((short)1);   // mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // byte rate
            bw.Write((short)2);   // block align
            bw.Write((short)16);  // bits per sample
            bw.Write("data".ToCharArray());
            bw.Write(dataSize);
            foreach (var s in samples) bw.Write(s);
        }
        return ms.ToArray();
    }
}
