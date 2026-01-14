namespace AiTourBackend.Services;

public interface IAudioUtilsService
{
    string FloatFrameBase64ToPcm16Base64(string dataBase64);
    byte[] FloatFrameToPcm16Bytes(ReadOnlySpan<float> frame);
    string Pcm16BytesToBase64(byte[] raw);
}