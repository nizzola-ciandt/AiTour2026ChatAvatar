using Azure.Search.Documents;
using Azure;
using AiTourBackend.Configuration;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Azure.Search.Documents.Models;

namespace AiTourBackend.Services;

public class ToolsService : IToolsService
{
    private readonly ILogger<ToolsService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureVoiceSettings _settings;
    private readonly SearchClient? _searchClient;

    public ToolsService(
        ILogger<ToolsService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<AzureVoiceSettings> settings)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;

        if (!string.IsNullOrEmpty(_settings.Search.Url) && 
            !string.IsNullOrEmpty(_settings.Search.Key))
        {
            var credential = new AzureKeyCredential(_settings.Search.Key);
            _searchClient = new SearchClient(
                new Uri(_settings.Search.Url),
                _settings.Search.IndexName,
                credential);
        }
    }

    public async Task<string> PerformSearchBasedQnaAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("perform_search_based_qna - query: {Query}", query);

        if (_searchClient == null)
        {
            throw new InvalidOperationException("Azure Search is not configured");
        }

        var searchOptions = new SearchOptions
        {
            QueryType = Azure.Search.Documents.Models.SearchQueryType.Semantic,
            SemanticSearch = new()
            {
                SemanticConfigurationName = _settings.Search.SemanticConfig
            },
            Size = 2
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(
            query, 
            searchOptions, 
            cancellationToken);

        var responseDocs = new List<string>();
        var counter = 0;

        await foreach (var result in response.Value.GetResultsAsync())
        {
            _logger.LogDebug("Search hit {Counter}: {Name}", 
                counter, 
                result.Document.TryGetValue("metadata_storage_name", out var name) ? name : "unknown");

            if (result.Document.TryGetValue("content", out var content))
            {
                responseDocs.Add($" --- Document context start ---{content}\n ---End of Document ---\n");
            }

            if (++counter >= 2) break;
        }

        _logger.LogInformation("Search aggregation complete with {Count} documents", responseDocs.Count);
        return string.Join("", responseDocs);
    }

    public async Task<string> CreateDeliveryOrderAsync(string orderId, string destination, CancellationToken cancellationToken = default)
    {
        var url = _settings.LogicApps.ShipmentOrdersUrl 
            ?? throw new InvalidOperationException("ShipmentOrdersUrl not configured");

        var payload = new { order_id = orderId, destination };
        var result = await PostJsonAsync(url, payload, cancellationToken);
        return JsonSerializer.Serialize(result);
    }

    public async Task<string> PerformCallLogAnalysisAsync(string callLog, CancellationToken cancellationToken = default)
    {
        var url = _settings.LogicApps.CallLogAnalysisUrl 
            ?? throw new InvalidOperationException("CallLogAnalysisUrl not configured");

        try
        {
            var callLogJson = JsonSerializer.Deserialize<JsonElement>(callLog);
            var payload = new { call_logs = callLogJson };
            var result = await PostJsonAsync(url, payload, cancellationToken);
            return JsonSerializer.Serialize(result);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON for call_log");
            return JsonSerializer.Serialize(new { error = $"Invalid JSON: {ex.Message}" });
        }
    }

    public async Task<object> GetProductsByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        var api = _settings.EcomApiUrl 
            ?? throw new InvalidOperationException("EcomApiUrl not configured");

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{api}/api/products/category/{category}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<object>(cancellationToken) 
            ?? throw new InvalidOperationException("Empty response");
    }

    public async Task<object> SearchProductsByCategoryAndPriceAsync(string category, float price, CancellationToken cancellationToken = default)
    {
        var api = _settings.EcomApiUrl 
            ?? throw new InvalidOperationException("EcomApiUrl not configured");

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(
            $"{api}/api/products/search?category={category}&price={price}", 
            cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<object>(cancellationToken) 
            ?? throw new InvalidOperationException("Empty response");
    }

    public async Task<object> OrderProductsAsync(string productId, int quantity, CancellationToken cancellationToken = default)
    {
        var api = _settings.EcomApiUrl 
            ?? throw new InvalidOperationException("EcomApiUrl not configured");

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(
            $"{api}/api/orders/?id={productId}&quantity={quantity}", 
            cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<object>(cancellationToken) 
            ?? throw new InvalidOperationException("Empty response");
    }

    public async Task<string> ExecuteFunctionAsync(
        string functionName, 
        Dictionary<string, object> arguments, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing function: {FunctionName}", functionName);

        object result = functionName switch
        {
            "perform_search_based_qna" => await PerformSearchBasedQnaAsync(
                arguments["query"].ToString()!, cancellationToken),
            
            "create_delivery_order" => await CreateDeliveryOrderAsync(
                arguments["order_id"].ToString()!,
                arguments["destination"].ToString()!, 
                cancellationToken),
            
            "perform_call_log_analysis" => await PerformCallLogAnalysisAsync(
                arguments["call_log"].ToString()!, cancellationToken),
            
            "get_products_by_category" => await GetProductsByCategoryAsync(
                arguments["category"].ToString()!, cancellationToken),
            
            "search_products_by_category_and_price" => await SearchProductsByCategoryAndPriceAsync(
                arguments["category"].ToString()!,
                Convert.ToSingle(arguments["price"]), 
                cancellationToken),
            
            "order_products" => await OrderProductsAsync(
                arguments["product_id"].ToString()!,
                Convert.ToInt32(arguments["quantity"]), 
                cancellationToken),
            
            _ => throw new InvalidOperationException($"Function {functionName} not found")
        };

        return result is string str ? str : JsonSerializer.Serialize(result);
    }

    public List<object> GetToolsList()
    {
        return new List<object>
        {
            new
            {
                type = "function",
                name = "perform_search_based_qna",
                description = "call this function to respond to the user query on Contoso retail policies, procedures and general QnA",
                parameters = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" }
                }
            },
            new
            {
                type = "function",
                name = "create_delivery_order",
                description = "call this function to create a delivery order based on order id and destination location",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        order_id = new { type = "string" },
                        destination = new { type = "string" }
                    },
                    required = new[] { "order_id", "destination" }
                }
            },
            new
            {
                type = "function",
                name = "perform_call_log_analysis",
                description = "call this function to analyze call log based on input call log conversation text",
                parameters = new
                {
                    type = "object",
                    properties = new { call_log = new { type = "string" } },
                    required = new[] { "call_log" }
                }
            },
            new
            {
                type = "function",
                name = "get_products_by_category",
                description = "call this function to get all the products under a category",
                parameters = new
                {
                    type = "object",
                    properties = new { category = new { type = "string" } },
                    required = new[] { "category" }
                }
            },
            new
            {
                type = "function",
                name = "search_products_by_category_and_price",
                description = "call this function to search for products by category and price range",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        category = new { type = "string" },
                        price = new { type = "number" }
                    },
                    required = new[] { "category", "price" }
                }
            },
            new
            {
                type = "function",
                name = "order_products",
                description = "call this function to order products by product id and quantity",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        product_id = new { type = "string" },
                        quantity = new { type = "integer" }
                    },
                    required = new[] { "product_id", "quantity" }
                }
            }
        };
    }

    private async Task<string> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST {Url}", url);
        
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        
        var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}