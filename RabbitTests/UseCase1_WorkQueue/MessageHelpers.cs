using System.Text;
using System.Text.Json;

namespace RabbitTests.Infrastructure;

/// <summary>
/// Utility classes for message serialization and testing helpers
/// </summary>
public static class MessageHelpers
{
    /// <summary>
    /// Serializes an object to JSON bytes for RabbitMQ message
    /// </summary>
    public static byte[] SerializeMessage<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Deserializes JSON bytes from RabbitMQ message to object
    /// </summary>
    public static T DeserializeMessage<T>(byte[] messageBody)
    {
        var json = Encoding.UTF8.GetString(messageBody);
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Failed to deserialize message");
    }

    /// <summary>
    /// Creates a simple text message
    /// </summary>
    public static byte[] CreateTextMessage(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Converts byte array back to text
    /// </summary>
    public static string GetTextFromMessage(byte[] messageBody)
    {
        return Encoding.UTF8.GetString(messageBody);
    }

    /// <summary>
    /// Generates a unique message ID for testing
    /// </summary>
    public static string GenerateMessageId()
    {
        return $"msg-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Creates a test work item message
    /// </summary>
    public static WorkItem CreateWorkItem(string task, int processingTimeMs = 1000)
    {
        return new WorkItem
        {
            Id = GenerateMessageId(),
            Task = task,
            CreatedAt = DateTime.UtcNow,
            ProcessingTimeMs = processingTimeMs
        };
    }
}

/// <summary>
/// Represents a work item for testing work queue scenarios
/// </summary>
public class WorkItem
{
    public string Id { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ProcessingTimeMs { get; set; }
    public string WorkerId { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// Result class for tracking test operations
/// </summary>
public class TestResult
{
    public List<string> ProcessedMessages { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
    public int MessageCount => ProcessedMessages.Count;
}

/// <summary>
/// Consumer statistics for testing
/// </summary>
public class ConsumerStats
{
    public string ConsumerId { get; set; } = string.Empty;
    public int MessagesProcessed { get; set; }
    public List<string> ProcessedMessageIds { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? LastMessageTime { get; set; }
}