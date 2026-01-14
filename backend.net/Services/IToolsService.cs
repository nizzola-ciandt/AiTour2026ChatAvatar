namespace AiTourBackend.Services;

public interface IToolsService
{
    Task<string> PerformSearchBasedQnaAsync(string query, CancellationToken cancellationToken = default);
    Task<string> CreateDeliveryOrderAsync(string orderId, string destination, CancellationToken cancellationToken = default);
    Task<string> PerformCallLogAnalysisAsync(string callLog, CancellationToken cancellationToken = default);
    Task<object> GetProductsByCategoryAsync(string category, CancellationToken cancellationToken = default);
    Task<object> SearchProductsByCategoryAndPriceAsync(string category, float price, CancellationToken cancellationToken = default);
    Task<object> OrderProductsAsync(string productId, int quantity, CancellationToken cancellationToken = default);
    Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
    List<object> GetToolsList();
}