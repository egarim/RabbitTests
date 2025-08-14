using System.Text.Json;

namespace RabbitTests.UseCase5_RPC;

/// <summary>
/// Represents an RPC request message
/// </summary>
public class RPCRequest
{
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Represents an RPC response message
/// </summary>
public class RPCResponse
{
    public string Id { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public TimeSpan ProcessingTime => ReceivedAt.HasValue ? ProcessedAt - ReceivedAt.Value : TimeSpan.Zero;
}

/// <summary>
/// Factory for creating common RPC requests
/// </summary>
public static class RPCRequestFactory
{
    /// <summary>
    /// Creates a simple calculation request
    /// </summary>
    public static RPCRequest CreateCalculationRequest(string operation, double a, double b, string? clientId = null)
    {
        return new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "calculate",
            Parameters = new Dictionary<string, object>
            {
                ["operation"] = operation,
                ["a"] = a,
                ["b"] = b
            },
            ClientId = clientId ?? "test-client",
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an echo request
    /// </summary>
    public static RPCRequest CreateEchoRequest(string message, string? clientId = null)
    {
        return new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "echo",
            Parameters = new Dictionary<string, object>
            {
                ["message"] = message
            },
            ClientId = clientId ?? "test-client",
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a delay request (for testing timeouts)
    /// </summary>
    public static RPCRequest CreateDelayRequest(int delayMs, string? clientId = null)
    {
        return new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "delay",
            Parameters = new Dictionary<string, object>
            {
                ["delayMs"] = delayMs
            },
            ClientId = clientId ?? "test-client",
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an error request (for testing error handling)
    /// </summary>
    public static RPCRequest CreateErrorRequest(string errorMessage, string? clientId = null)
    {
        return new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "error",
            Parameters = new Dictionary<string, object>
            {
                ["errorMessage"] = errorMessage
            },
            ClientId = clientId ?? "test-client",
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a data processing request
    /// </summary>
    public static RPCRequest CreateDataProcessingRequest(List<int> data, string operation, string? clientId = null)
    {
        return new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "processData",
            Parameters = new Dictionary<string, object>
            {
                ["data"] = data,
                ["operation"] = operation
            },
            ClientId = clientId ?? "test-client",
            CreatedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Common RPC request processors for testing
/// </summary>
public static class RPCRequestProcessors
{
    /// <summary>
    /// Default request processor that handles common test scenarios
    /// </summary>
    public static async Task<RPCResponse> DefaultProcessor(RPCRequest request)
    {
        await Task.Delay(10); // Simulate some processing time

        return request.Method switch
        {
            "echo" => ProcessEcho(request),
            "calculate" => ProcessCalculation(request),
            "delay" => await ProcessDelay(request),
            "error" => ProcessError(request),
            "processData" => ProcessData(request),
            _ => new RPCResponse
            {
                Id = Guid.NewGuid().ToString(),
                Success = false,
                ErrorMessage = $"Unknown method: {request.Method}"
            }
        };
    }

    private static RPCResponse ProcessEcho(RPCRequest request)
    {
        var message = request.Parameters.GetValueOrDefault("message", "").ToString();
        
        return new RPCResponse
        {
            Id = Guid.NewGuid().ToString(),
            Success = true,
            Result = $"Echo: {message}"
        };
    }

    private static RPCResponse ProcessCalculation(RPCRequest request)
    {
        try
        {
            var operation = request.Parameters.GetValueOrDefault("operation", "").ToString();
            var a = Convert.ToDouble(request.Parameters.GetValueOrDefault("a", 0));
            var b = Convert.ToDouble(request.Parameters.GetValueOrDefault("b", 0));

            var result = operation?.ToLower() switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" => b != 0 ? a / b : throw new DivideByZeroException("Cannot divide by zero"),
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            return new RPCResponse
            {
                Id = Guid.NewGuid().ToString(),
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new RPCResponse
            {
                Id = Guid.NewGuid().ToString(),
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static async Task<RPCResponse> ProcessDelay(RPCRequest request)
    {
        var delayMs = Convert.ToInt32(request.Parameters.GetValueOrDefault("delayMs", 1000));
        await Task.Delay(delayMs);

        return new RPCResponse
        {
            Id = Guid.NewGuid().ToString(),
            Success = true,
            Result = $"Delayed for {delayMs}ms"
        };
    }

    private static RPCResponse ProcessError(RPCRequest request)
    {
        var errorMessage = request.Parameters.GetValueOrDefault("errorMessage", "Test error").ToString();
        
        return new RPCResponse
        {
            Id = Guid.NewGuid().ToString(),
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    private static RPCResponse ProcessData(RPCRequest request)
    {
        try
        {
            var dataObject = request.Parameters.GetValueOrDefault("data", new List<int>());
            var operation = request.Parameters.GetValueOrDefault("operation", "sum").ToString();
            
            // Handle the data parameter which comes as an object from the dictionary
            List<int> data;
            if (dataObject is List<int> intList)
            {
                data = intList;
            }
            else if (dataObject is JsonElement jsonElement)
            {
                data = JsonSerializer.Deserialize<List<int>>(jsonElement.GetRawText()) ?? new List<int>();
            }
            else
            {
                // Try to parse as JSON string
                var dataJson = dataObject?.ToString() ?? "[]";
                data = JsonSerializer.Deserialize<List<int>>(dataJson) ?? new List<int>();
            }

            var result = operation?.ToLower() switch
            {
                "sum" => data.Sum(),
                "average" => data.Count > 0 ? data.Average() : 0,
                "max" => data.Count > 0 ? data.Max() : 0,
                "min" => data.Count > 0 ? data.Min() : 0,
                "count" => data.Count,
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            return new RPCResponse
            {
                Id = Guid.NewGuid().ToString(),
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new RPCResponse
            {
                Id = Guid.NewGuid().ToString(),
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}