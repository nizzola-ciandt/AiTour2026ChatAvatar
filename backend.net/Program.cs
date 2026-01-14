using AiTourBackend;
using AiTourBackend.Configuration;
using AiTourBackend.Services;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON options
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.WriteIndented = false;
});

// Configure settings from appsettings.json and environment variables
builder.Services.Configure<AzureVoiceSettings>(
    builder.Configuration.GetSection(AzureVoiceSettings.SectionName));

// Register services
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddScoped<IAudioUtilsService, AudioUtilsService>();
//builder.Services.AddScoped<IVoiceLiveSession, VoiceLiveSession>();
builder.Services.AddScoped<IToolsService, ToolsService>();

// Register HTTP clients
builder.Services.AddHttpClient();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
//builder.Services.AddOpenApi();

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.UseCors();

// Warm up ecom API on startup
var ecomApiUrl = builder.Configuration["AzureVoice:EcomApiUrl"];
if (!string.IsNullOrEmpty(ecomApiUrl))
{
    _ = Task.Run(async () =>
    {
        try
        {
            var httpClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
            var warmupUrl = $"{ecomApiUrl.TrimEnd('/')}/openapi";
            app.Logger.LogInformation("Warming up ecom API at {Url}", warmupUrl);
            
            var response = await httpClient.GetAsync(warmupUrl);
            if (response.IsSuccessStatusCode)
            {
                app.Logger.LogInformation("Successfully warmed up ecom API");
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to warm up ecom API");
        }
    });
}

app.MapEndpoints();

// Serve static files if they exist
var staticPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(staticPath))
{
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

// Cleanup sessions on shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(async () =>
{
    var sessionManager = app.Services.GetRequiredService<ISessionManager>();
    var sessionIds = await sessionManager.ListSessionIdsAsync();
    await Task.WhenAll(sessionIds.Select(id => sessionManager.RemoveSessionAsync(id)));
});

app.Run();