namespace AiTourBackend.Configuration;

public class AzureVoiceSettings
{
    public const string SectionName = "AzureVoice";

    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2025-05-01-preview";
    public string? ApiKey { get; set; }
    public string TtsVoice { get; set; } = "pt-BR-GiovannaNeural";
    
    public AvatarSettings Avatar { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public LogicAppSettings LogicApps { get; set; } = new();
    public string? EcomApiUrl { get; set; }
}

public class AvatarSettings
{
    public string Character { get; set; } = "lisa";
    public string? Style { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Bitrate { get; set; } = 2000000;
    public string? IceUrls { get; set; }
}

public class SearchSettings
{
    public string Url { get; set; } = string.Empty;
    public string? Key { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public string SemanticConfig { get; set; } = string.Empty;
}

public class LogicAppSettings
{
    public string? ShipmentOrdersUrl { get; set; }
    public string? CallLogAnalysisUrl { get; set; }
}