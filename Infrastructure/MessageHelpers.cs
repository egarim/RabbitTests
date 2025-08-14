using System.Text;
using System.Text.Json;

namespace RabbitTests.Infrastructure;

public static class MessageHelpers
{
    public static byte[] SerializeMessage<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        return Encoding.UTF8.GetBytes(json);
    }

    public static T DeserializeMessage<T>(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Failed to deserialize message");
    }

    public static string GenerateUniqueQueueName(string prefix = "test")
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    public static string GenerateUniqueExchangeName(string prefix = "test")
    {
        return $"{prefix}_exchange_{Guid.NewGuid():N}";
    }
}

public class WorkMessage
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}