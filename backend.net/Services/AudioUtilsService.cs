using System.Buffers;
using System.Runtime.InteropServices;

namespace AiTourBackend.Services;

public class AudioUtilsService : IAudioUtilsService
{
    private const int TargetSampleRate = 24000;
    private const short Int16Max = short.MaxValue;
    private const short Int16Min = short.MinValue;

    public string FloatFrameBase64ToPcm16Base64(string dataBase64)
    {
        var floatBytes = Convert.FromBase64String(dataBase64);
        var floatArray = MemoryMarshal.Cast<byte, float>(floatBytes);
        var pcmBytes = FloatFrameToPcm16Bytes(floatArray);
        return Convert.ToBase64String(pcmBytes);
    }

    public byte[] FloatFrameToPcm16Bytes(ReadOnlySpan<float> frame)
    {
        var int16Samples = ArrayPool<short>.Shared.Rent(frame.Length);
        try
        {
            for (int i = 0; i < frame.Length; i++)
            {
                var clipped = Math.Clamp(frame[i], -1.0f, 1.0f);
                int16Samples[i] = (short)(clipped * Int16Max);
            }

            var result = new byte[frame.Length * sizeof(short)];
            Buffer.BlockCopy(int16Samples, 0, result, 0, result.Length);
            return result;
        }
        finally
        {
            ArrayPool<short>.Shared.Return(int16Samples);
        }
    }

    public string Pcm16BytesToBase64(byte[] raw)
    {
        return Convert.ToBase64String(raw);
    }
}