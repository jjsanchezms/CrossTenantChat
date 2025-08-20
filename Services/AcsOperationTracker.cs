using System.Collections.Concurrent;
using CrossTenantChat.Models;

namespace CrossTenantChat.Services;

public class AcsOperationTracker : IAcsOperationTracker
{
    private readonly ILogger<AcsOperationTracker> _logger;
    private readonly ConcurrentDictionary<string, AcsOperation> _operations = new();
    private readonly object _lock = new object();

    public AcsOperationTracker(ILogger<AcsOperationTracker> logger)
    {
        _logger = logger;
    }

    public string StartOperation(string operationType, string description, string? userId = null, string? tenantName = null)
    {
        var operationId = Guid.NewGuid().ToString();
        
        var operation = new AcsOperation
        {
            Id = operationId,
            OperationType = operationType,
            Description = description,
            UserId = userId,
            TenantName = tenantName,
            StartTime = DateTime.UtcNow,
            IsCompleted = false,
            IsSuccessful = false
        };

        _operations[operationId] = operation;

        _logger.LogInformation("🚀 Started operation [{OperationType}]: {Description} (ID: {OperationId})", 
            operationType, description, operationId);

        return operationId;
    }

    public void AddStep(string operationId, string stepName, string description, bool isSuccessful = true,
        Dictionary<string, object>? metadata = null, string? errorMessage = null)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            _logger.LogWarning("⚠️ Attempted to add step to non-existent operation: {OperationId}", operationId);
            return;
        }

        var step = new OperationStep
        {
            StepName = stepName,
            Description = description,
            Timestamp = DateTime.UtcNow,
            IsSuccessful = isSuccessful,
            ErrorMessage = errorMessage,
            Metadata = metadata ?? new Dictionary<string, object>()
        };

        lock (_lock)
        {
            operation.Steps.Add(step);
        }

        var statusEmoji = isSuccessful ? "✅" : "❌";
        _logger.LogInformation("{StatusEmoji} Operation [{OperationType}] Step: {StepName} - {Description} (ID: {OperationId})", 
            statusEmoji, operation.OperationType, stepName, description, operationId);

        if (!string.IsNullOrEmpty(errorMessage))
        {
            _logger.LogWarning("   Error: {ErrorMessage}", errorMessage);
        }
    }

    public void CompleteOperation(string operationId, bool isSuccessful = true, string? errorMessage = null)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            _logger.LogWarning("⚠️ Attempted to complete non-existent operation: {OperationId}", operationId);
            return;
        }

        operation.EndTime = DateTime.UtcNow;
        operation.IsCompleted = true;
        operation.IsSuccessful = isSuccessful;
        operation.ErrorMessage = errorMessage;

        var statusEmoji = isSuccessful ? "🎉" : "💥";
        var duration = operation.Duration?.TotalMilliseconds ?? 0;
        _logger.LogInformation("{StatusEmoji} Completed operation [{OperationType}]: {Description} in {Duration:F0}ms (ID: {OperationId})", 
            statusEmoji, operation.OperationType, operation.Description, duration, operationId);

        if (!string.IsNullOrEmpty(errorMessage))
        {
            _logger.LogError("   Final Error: {ErrorMessage}", errorMessage);
        }
    }

    public List<AcsOperation> GetOperationsForUser(string userId)
    {
        return _operations.Values
            .Where(op => op.UserId == userId)
            .OrderByDescending(op => op.StartTime)
            .ToList();
    }

    public List<AcsOperation> GetRecentOperations(int count = 50)
    {
        return _operations.Values
            .OrderByDescending(op => op.StartTime)
            .Take(count)
            .ToList();
    }

    public List<AcsOperation> GetAllOperations()
    {
        return _operations.Values
            .OrderByDescending(op => op.StartTime)
            .ToList();
    }

    public void ClearOperations()
    {
        _operations.Clear();
        _logger.LogInformation("🧹 Cleared all tracked operations");
    }
}