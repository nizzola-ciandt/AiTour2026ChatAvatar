namespace AiTourBackend.Services;

public interface ISessionManager
{
    Task<IVoiceLiveSession> CreateSessionAsync(CancellationToken cancellationToken = default);
    Task<IVoiceLiveSession> GetSessionAsync(string sessionId);
    Task<IReadOnlyList<string>> ListSessionIdsAsync();
    Task RemoveSessionAsync(string sessionId);
}