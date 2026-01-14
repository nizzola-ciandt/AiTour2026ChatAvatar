using System.Collections.Concurrent;

namespace AiTourBackend.Services;

public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, IVoiceLiveSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;
    private readonly IServiceProvider _serviceProvider;

    public SessionManager(ILogger<SessionManager> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<IVoiceLiveSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        
        // Create session with dependency injection
        var session = ActivatorUtilities.CreateInstance<VoiceLiveSession>(
            _serviceProvider, 
            sessionId);

        await session.ConnectAsync(cancellationToken);
        
        if (!_sessions.TryAdd(sessionId, session))
        {
            await session.DisconnectAsync();
            throw new InvalidOperationException($"Failed to add session {sessionId}");
        }

        _logger.LogInformation("Created Voice Live session {SessionId}", sessionId);
        return session;
    }

    public Task<IVoiceLiveSession> GetSessionAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Session {sessionId} not found");
        }

        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<string>> ListSessionIdsAsync()
    {
        IReadOnlyList<string> ids = _sessions.Keys.ToList();
        return Task.FromResult(ids);
    }

    public async Task RemoveSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisconnectAsync();
            _logger.LogInformation("Removed session {SessionId}", sessionId);
        }
    }
}