using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Azure.Identity;
using AiTourBackend.Configuration;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace AiTourBackend.Services;

public class VoiceLiveSession : IVoiceLiveSession, IDisposable
{
    private const string SystemInstructions = @"
Você é um Agente de IA que irá responder a questões de possíveis clientes da CI&T a respeito dos serviços por ela executados e das
suas qualidades como prestadora de serviços de tecnologia, onde a CI&T possui um time técnico composto de mais de 8000 funcionários ao 
redor do mundo alocados em grandes clientes.
Você irá utilizar somente informações contidas na base de conhecimento para responder as perguntas.
When the customer starts the conversation with a greeting, reciprocate as you respond to their queries. 
Refer to the context provided to you from the CI&T knowledge base to respond to their queries.
**DO NOT RESPOND BASED ON YOUR PERSONAL OPINIONS OR EXPERIENCES**

**FORMATTING INSTRUCTIONS:**
- Always format your responses using proper Markdown syntax for better readability
- Use bullet points (- or *) for lists
- Use numbered lists (1. 2. 3.) when presenting ordered information like product details, order summaries, or shipment details
- Use **bold text** for important information like prices, order IDs, or product names
- Use proper line breaks and spacing for clarity
- Use headers (## or ###) when organizing longer responses into sections
- Format tables using Markdown table syntax when presenting structured data

- APIs to analyze call logs.
    - When the user indicates there are no more questions, and wants to end the conversation, you must implicitly call this API to log the entire conversation for analysis. The user does not need to know you are doing this.
    - **You do not need to send the results of the analysis back to the user. You could just say the conversation has been logged for analysis.**

Important confirmation requirements:
**Empathize with the customer when you respond**
**Remember that your persona is that of a woman. When you speak to the customer in Portuguese, mind your gender when you respond**";

    private readonly ILogger<VoiceLiveSession> _logger;
    private readonly IAudioUtilsService _audioUtils;
    private readonly IToolsService _toolsService;
    private readonly AzureVoiceSettings _settings;
    
    private ClientWebSocket? _webSocket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Channel<Dictionary<string, object>> _eventChannel;
    private TaskCompletionSource<string>? _avatarSdpTcs;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public string SessionId { get; }

    public VoiceLiveSession(string sessionId,
        ILogger<VoiceLiveSession> logger,
        IAudioUtilsService audioUtils,
        IToolsService toolsService,
        IOptions<AzureVoiceSettings> settings)
    {
        SessionId = sessionId;
        _logger = logger;
        _audioUtils = audioUtils;
        _toolsService = toolsService;
        _settings = settings.Value;
        _eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State == WebSocketState.Open)
            return;

        _webSocket = new ClientWebSocket();
        
        var wsUrl = BuildWebSocketUrl();
        var requestId = Guid.NewGuid().ToString();
        
        _webSocket.Options.SetRequestHeader("x-ms-client-request-id", requestId);

        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _webSocket.Options.SetRequestHeader("api-key", _settings.ApiKey);
        }
        else
        {
            var token = await GetAzureTokenAsync();
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
        _logger.LogInformation("[{SessionId}] Connected to Azure Voice Live", SessionId);

        // Start receive loop
        _receiveCts = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

        // Send session configuration
        var sessionConfig = BuildSessionConfig();
        await SendEventAsync("session.update", new { session = sessionConfig }, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
        }

        _receiveCts?.Cancel();
        
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _eventChannel.Writer.Complete();
        
        _logger.LogInformation("[{SessionId}] Disconnected session", SessionId);
    }

    public async Task<string> ConnectAvatarAsync(string clientSdp, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _avatarSdpTcs = new TaskCompletionSource<string>();
        
        var encodedSdp = EncodeClientSdp(clientSdp);
        var payload = new
        {
            client_sdp = encodedSdp,
            rtc_configuration = new { bundle_policy = "max-bundle" }
        };

        await SendEventAsync("session.avatar.connect", payload, cancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await _avatarSdpTcs.Task.WaitAsync(linkedCts.Token);
        }
        finally
        {
            _avatarSdpTcs = null;
        }
    }

    public async Task SendUserMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await SendEventAsync("conversation.item.create", new
        {
            item = new
            {
                type = "message",
                role = "user",
                content = new[]
                {
                    new { type = "input_text", text }
                }
            }
        }, cancellationToken);

        await SendEventAsync("response.create", new
        {
            response = new { modalities = new[] { "text", "audio" } }
        }, cancellationToken);
    }

    public async Task SendAudioChunkAsync(string audioBase64, string encoding = "float32", CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var pcmBase64 = encoding == "float32" 
            ? _audioUtils.FloatFrameBase64ToPcm16Base64(audioBase64)
            : audioBase64;

        await SendEventAsync("input_audio_buffer.append", new { audio = pcmBase64 }, cancellationToken);
    }

    public async Task CommitAudioAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await SendEventAsync("input_audio_buffer.commit", null, cancellationToken);
    }

    public async Task ClearAudioAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await SendEventAsync("input_audio_buffer.clear", null, cancellationToken);
    }

    public async Task RequestResponseAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await SendEventAsync("response.create", new
        {
            response = new { modalities = new[] { "text", "audio" } }
        }, cancellationToken);
    }

    public async IAsyncEnumerable<Dictionary<string, object>> GetEventStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null)
            return;

        var buffer = new byte[1024 * 16];
        var messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("[{SessionId}] WebSocket closed by server", SessionId);
                    break;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuilder.ToString();
                    messageBuilder.Clear();

                    try
                    {
                        var eventData = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
                        if (eventData != null)
                        {
                            await ProcessEventAsync(eventData);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "[{SessionId}] Failed to parse WebSocket message", SessionId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{SessionId}] Receive loop cancelled", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{SessionId}] Error in receive loop", SessionId);
            await BroadcastEventAsync(new Dictionary<string, object>
            {
                ["type"] = "error",
                ["payload"] = new { message = ex.Message }
            });
        }
    }

    private async Task ProcessEventAsync(Dictionary<string, object> eventData)
    {
        if (!eventData.TryGetValue("type", out var typeObj) || typeObj is not JsonElement typeElement)
            return;

        var eventType = typeElement.GetString();

        switch (eventType)
        {
            case "error":
                await BroadcastEventAsync(new Dictionary<string, object>
                {
                    ["type"] = "error",
                    ["payload"] = eventData
                });
                break;

            case "response.audio.delta":
                if (eventData.TryGetValue("delta", out var delta))
                {
                    await BroadcastEventAsync(new Dictionary<string, object>
                    {
                        ["type"] = "assistant_audio_delta",
                        ["delta"] = delta
                    });
                }
                break;

            case "response.audio.done":
                await BroadcastEventAsync(new Dictionary<string, object>
                {
                    ["type"] = "assistant_audio_done",
                    ["payload"] = eventData
                });
                break;

            case "response.audio_transcript.delta":
                await BroadcastEventAsync(new Dictionary<string, object>
                {
                    ["type"] = "assistant_transcript_delta",
                    ["delta"] = eventData.GetValueOrDefault("delta"),
                    ["item_id"] = eventData.GetValueOrDefault("item_id")
                });
                break;

            case "response.audio_transcript.done":
                await BroadcastEventAsync(new Dictionary<string, object>
                {
                    ["type"] = "assistant_transcript_done",
                    ["transcript"] = eventData.GetValueOrDefault("transcript"),
                    ["item_id"] = eventData.GetValueOrDefault("item_id")
                });
                break;

            case "conversation.item.input_audio_transcription.completed":
                await BroadcastEventAsync(new Dictionary<string, object>
                {
                    ["type"] = "user_transcript_completed",
                    ["transcript"] = eventData.GetValueOrDefault("transcript"),
                    ["item_id"] = eventData.GetValueOrDefault("item_id")
                });
                break;

            case "input_audio_buffer.speech_started":
                await BroadcastEventAsync(new Dictionary<string, object> { ["type"] = "speech_started" });
                break;

            case "input_audio_buffer.speech_stopped":
                await BroadcastEventAsync(new Dictionary<string, object> { ["type"] = "speech_stopped" });
                break;

            case "input_audio_buffer.committed":
                await BroadcastEventAsync(new Dictionary<string, object> { ["type"] = "input_audio_committed" });
                break;

            case "session.avatar.connecting":
                if (eventData.TryGetValue("server_sdp", out var sdpObj) && sdpObj is JsonElement sdpElement)
                {
                    var serverSdp = DecodeServerSdp(sdpElement.GetString());
                    _avatarSdpTcs?.TrySetResult(serverSdp ?? string.Empty);
                }
                await BroadcastEventAsync(new Dictionary<string, object> { ["type"] = "avatar_connecting" });
                break;

            case "response.done":
                await HandleResponseDoneAsync(eventData);
                break;

            default:
                await BroadcastEventAsync(new Dictionary<string, object>
                {
                    ["type"] = "event",
                    ["payload"] = eventData
                });
                break;
        }
    }

    private async Task HandleResponseDoneAsync(Dictionary<string, object> eventData)
    {
        if (!eventData.TryGetValue("response", out var responseObj) || 
            responseObj is not JsonElement responseElement)
            return;

        var response = JsonSerializer.Deserialize<Dictionary<string, object>>(responseElement.GetRawText());
        if (response == null)
            return;

        if (!response.TryGetValue("status", out var statusObj) || 
            statusObj is not JsonElement statusElement)
            return;

        var status = statusElement.GetString();
        if (status != "completed")
        {
            await BroadcastEventAsync(new Dictionary<string, object>
            {
                ["type"] = "response_status",
                ["status"] = status ?? "unknown"
            });
            return;
        }

        if (!response.TryGetValue("output", out var outputObj) || 
            outputObj is not JsonElement outputElement)
            return;

        var outputItems = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(outputElement.GetRawText());
        if (outputItems == null || outputItems.Count == 0)
            return;

        var firstItem = outputItems[0];
        if (!firstItem.TryGetValue("type", out var typeObj) || 
            typeObj is not JsonElement typeElement || 
            typeElement.GetString() != "function_call")
            return;

        var functionName = firstItem.TryGetValue("name", out var nameObj) && nameObj is JsonElement nameElement
            ? nameElement.GetString()
            : null;

        var argumentsJson = firstItem.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement argsElement
            ? argsElement.GetString()
            : "{}";

        var callId = firstItem.TryGetValue("call_id", out var callIdObj) && callIdObj is JsonElement callIdElement
            ? callIdElement.GetString()
            : null;

        if (string.IsNullOrEmpty(functionName) || string.IsNullOrEmpty(callId))
            return;

        _logger.LogInformation("[{SessionId}] Function call requested: {FunctionName}", SessionId, functionName);

        try
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson!) ?? new();
            var result = await _toolsService.ExecuteFunctionAsync(functionName, arguments);

            await SendEventAsync("conversation.item.create", new
            {
                item = new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = result
                }
            });

            await SendEventAsync("response.create", new
            {
                response = new { modalities = new[] { "text", "audio" } }
            });

            await BroadcastEventAsync(new Dictionary<string, object>
            {
                ["type"] = "function_call_completed",
                ["name"] = functionName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{SessionId}] Function {FunctionName} failed", SessionId, functionName);
        }
    }

    private async Task SendEventAsync(string eventType, object? data, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected");

        var payload = new Dictionary<string, object>
        {
            ["event_id"] = $"evt_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["type"] = eventType
        };

        if (data != null)
        {
            var dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                JsonSerializer.Serialize(data));
            
            if (dataDict != null)
            {
                foreach (var kvp in dataDict)
                {
                    payload[kvp.Key] = kvp.Value;
                }
            }
        }

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task BroadcastEventAsync(Dictionary<string, object> eventData)
    {
        await _eventChannel.Writer.WriteAsync(eventData);
    }

    private string BuildWebSocketUrl()
    {
        var endpoint = _settings.Endpoint.Replace("https://", "wss://").TrimEnd('/');
        var url = $"{endpoint}/voice-live/realtime?api-version={_settings.ApiVersion}&model={_settings.Model}";
        return url;
    }

    private async Task<string> GetAzureTokenAsync()
    {
        var credential = new DefaultAzureCredential();
        var tokenContext = new Azure.Core.TokenRequestContext(new[] { "https://ai.azure.com/.default" });
        var token = await credential.GetTokenAsync(tokenContext);
        return token.Token;
    }

    private object BuildSessionConfig()
    {
        return new
        {
            modalities = new[] { "text", "audio", "avatar", "animation" },
            input_audio_sampling_rate = 24000,
            instructions = SystemInstructions,
            turn_detection = new
            {
                type = "server_vad",
                threshold = 0.5,
                prefix_padding_ms = 300,
                silence_duration_ms = 500
            },
            tools = _toolsService.GetToolsList(),
            tool_choice = "auto",
            input_audio_noise_reduction = new { type = "azure_deep_noise_suppression" },
            input_audio_echo_cancellation = new { type = "server_echo_cancellation" },
            voice = new
            {
                name = _settings.TtsVoice,
                type = "azure-standard",
                temperature = 0.8
            },
            input_audio_transcription = new { model = "whisper-1" },
            avatar = BuildAvatarConfig(),
            animation = new
            {
                model_name = "default",
                outputs = new[] { "blendshapes", "viseme_id" }
            }
        };
    }

    private object BuildAvatarConfig()
    {
        var config = new Dictionary<string, object>
        {
            ["character"] = _settings.Avatar.Character,
            ["customized"] = false,
            ["video"] = new
            {
                resolution = new
                {
                    width = _settings.Avatar.Width,
                    height = _settings.Avatar.Height
                },
                bitrate = _settings.Avatar.Bitrate
            }
        };

        if (!string.IsNullOrEmpty(_settings.Avatar.Style))
        {
            config["style"] = _settings.Avatar.Style;
        }

        if (!string.IsNullOrEmpty(_settings.Avatar.IceUrls))
        {
            var urls = _settings.Avatar.IceUrls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            config["ice_servers"] = new[] { new { urls } };
        }

        return config;
    }

    private static string EncodeClientSdp(string clientSdp)
    {
        var payload = JsonSerializer.Serialize(new { type = "offer", sdp = clientSdp });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static string? DecodeServerSdp(string? serverSdpRaw)
    {
        if (string.IsNullOrEmpty(serverSdpRaw))
            return null;

        if (serverSdpRaw.StartsWith("v=0"))
            return serverSdpRaw;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(serverSdpRaw));
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(decoded);
            return payload?.GetValueOrDefault("sdp") ?? decoded;
        }
        catch
        {
            return serverSdpRaw;
        }
    }

    private void EnsureConnected()
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Session is not connected");
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        _receiveCts?.Dispose();
        _webSocket?.Dispose();
    }
}