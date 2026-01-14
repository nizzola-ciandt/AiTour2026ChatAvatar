using AiTourBackend.Models;
using AiTourBackend.Services;
using Microsoft.AspNetCore.Builder;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiTourBackend;

public static class Endpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        // Health check endpoint
        app.MapGet("/health", () => new HealthResponse("healthy", "voice-live-avatar-backend"))
           .WithName("HealthCheck");

        // Create session
        app.MapPost("/sessions", async (ISessionManager sessionManager, CancellationToken ct) =>
        {
            var session = await sessionManager.CreateSessionAsync(ct);
            return Results.Ok(new SessionResponse(session.SessionId));
        })
        .WithName("CreateSession");

        // Handle avatar offer
        app.MapPost("/sessions/{sessionId}/avatar-offer",
            async (string sessionId, AvatarOfferRequest request, ISessionManager sessionManager, CancellationToken ct) =>
            {
                try
                {
                    var session = await sessionManager.GetSessionAsync(sessionId);
                    var serverSdp = await session.ConnectAvatarAsync(request.Sdp, ct);
                    return Results.Ok(new AvatarAnswerResponse(serverSdp));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = "Session not found" });
                }
            })
        .WithName("HandleAvatarOffer");

        // Send text message
        app.MapPost("/sessions/{sessionId}/text",
            async (string sessionId, TextMessageRequest request, ISessionManager sessionManager, CancellationToken ct) =>
            {
                try
                {
                    var session = await sessionManager.GetSessionAsync(sessionId);
                    await session.SendUserMessageAsync(request.Text, ct);
                    return Results.Ok(new { status = "queued" });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = "Session not found" });
                }
            })
        .WithName("SendTextMessage");

        // Commit audio
        app.MapPost("/sessions/{sessionId}/commit-audio",
            async (string sessionId, ISessionManager sessionManager, CancellationToken ct) =>
            {
                try
                {
                    var session = await sessionManager.GetSessionAsync(sessionId);
                    await session.CommitAudioAsync(ct);
                    return Results.Ok(new AudioCommitResponse("committed"));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = "Session not found" });
                }
            })
        .WithName("CommitAudio");

        // WebSocket endpoint
        app.Map("/ws/sessions/{sessionId}", async (HttpContext context, string sessionId, ISessionManager sessionManager) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            IVoiceLiveSession? session;
            try
            {
                session = await sessionManager.GetSessionAsync(sessionId);
            }
            catch (KeyNotFoundException)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Session not found");
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

            // Send session ready event
            var readyEvent = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["type"] = "session_ready",
                ["session_id"] = sessionId
            });
            await webSocket.SendAsync(
                Encoding.UTF8.GetBytes(readyEvent),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);

            // Start broadcasting events from session to client
            var broadcastTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in session.GetEventStreamAsync(context.RequestAborted))
                    {
                        var json = JsonSerializer.Serialize(evt);
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error broadcasting events for session {SessionId}", sessionId);
                }
            });

            // Receive messages from client
            var buffer = new byte[1024 * 4];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message);

                    if (data == null)
                        continue;

                    var msgType = data.GetValueOrDefault("type").GetString();

                    switch (msgType)
                    {
                        case "audio_chunk":
                            var audioData = data.GetValueOrDefault("data").GetString();
                            var encoding = data.TryGetValue("encoding", out var enc) ? enc.GetString() : "float32";
                            if (audioData != null)
                            {
                                await session.SendAudioChunkAsync(audioData, encoding ?? "float32", context.RequestAborted);
                            }
                            break;

                        case "commit_audio":
                            await session.CommitAudioAsync(context.RequestAborted);
                            break;

                        case "clear_audio":
                            await session.ClearAudioAsync(context.RequestAborted);
                            break;

                        case "user_text":
                            var text = data.GetValueOrDefault("text").GetString();
                            if (text != null)
                            {
                                await session.SendUserMessageAsync(text, context.RequestAborted);
                            }
                            break;

                        case "request_response":
                            await session.RequestResponseAsync(context.RequestAborted);
                            break;

                        default:
                            logger.LogWarning("Unknown WebSocket message type: {Type}", msgType);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("WebSocket connection cancelled for session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in WebSocket handler for session {SessionId}", sessionId);
            }
            finally
            {
                await broadcastTask;
            }
        });

    }
}
