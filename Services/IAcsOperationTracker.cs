using CrossTenantChat.Models;

namespace CrossTenantChat.Services;

public interface IAcsOperationTracker
{
    /// <summary>
    /// Start tracking a new operation
    /// </summary>
    /// <param name="operationType">Type of operation (e.g., TokenExchange, ThreadCreation, MessageSend)</param>
    /// <param name="description">Human-readable description of the operation</param>
    /// <param name="userId">User ID associated with the operation</param>
    /// <param name="tenantName">Tenant name associated with the operation</param>
    /// <returns>Operation ID for tracking</returns>
    string StartOperation(string operationType, string description, string? userId = null, string? tenantName = null);

    /// <summary>
    /// Add a step to an existing operation
    /// </summary>
    /// <param name="operationId">Operation ID</param>
    /// <param name="stepName">Name of the step</param>
    /// <param name="description">Description of the step</param>
    /// <param name="isSuccessful">Whether the step was successful</param>
    /// <param name="metadata">Additional metadata for the step</param>
    /// <param name="errorMessage">Error message if step failed</param>
    void AddStep(string operationId, string stepName, string description, bool isSuccessful = true, 
        Dictionary<string, object>? metadata = null, string? errorMessage = null);

    /// <summary>
    /// Complete an operation
    /// </summary>
    /// <param name="operationId">Operation ID</param>
    /// <param name="isSuccessful">Whether the overall operation was successful</param>
    /// <param name="errorMessage">Error message if operation failed</param>
    void CompleteOperation(string operationId, bool isSuccessful = true, string? errorMessage = null);

    /// <summary>
    /// Get all operations for a specific user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>List of operations for the user</returns>
    List<AcsOperation> GetOperationsForUser(string userId);

    /// <summary>
    /// Get recent operations (last N operations)
    /// </summary>
    /// <param name="count">Number of recent operations to retrieve</param>
    /// <returns>List of recent operations</returns>
    List<AcsOperation> GetRecentOperations(int count = 50);

    /// <summary>
    /// Get all operations
    /// </summary>
    /// <returns>All tracked operations</returns>
    List<AcsOperation> GetAllOperations();

    /// <summary>
    /// Clear all operations (useful for cleanup)
    /// </summary>
    void ClearOperations();
}

public class AcsOperation
{
    public string Id { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? TenantName { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public List<OperationStep> Steps { get; set; } = new();
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
}

public class OperationStep
{
    public string StepName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}