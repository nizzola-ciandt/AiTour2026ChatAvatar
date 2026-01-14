namespace AiTourBackend.Services;

public interface IVoiceLiveSession
{
    string SessionId { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<string> ConnectAvatarAsync(string clientSdp, CancellationToken cancellationToken = default);
    Task SendUserMessageAsync(string text, CancellationToken cancellationToken = default);
    Task SendAudioChunkAsync(string audioBase64, string encoding = "float32", CancellationToken cancellationToken = default);
    Task CommitAudioAsync(CancellationToken cancellationToken = default);
    Task ClearAudioAsync(CancellationToken cancellationToken = default);
    Task RequestResponseAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<Dictionary<string, object>> GetEventStreamAsync(CancellationToken cancellationToken = default);
}