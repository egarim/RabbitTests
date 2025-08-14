using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitTests.Infrastructure;
using System.Text;

namespace RabbitTests.UseCase2_PublishSubscribe;

/// <summary>
/// Publisher service for Use Case 2: Publish-Subscribe Pattern (Fanout Exchange)
/// Publishes messages to a fanout exchange which broadcasts to all bound queues
/// </summary>
public class PublisherService : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<PublisherService> _logger;
    private readonly string _exchangeName;
    private bool _disposed = false;

    public PublisherService(IChannel channel, ILogger<PublisherService> logger, string exchangeName = "test-fanout-exchange")
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = exchangeName;
    }

    /// <summary>
    /// Initializes the fanout exchange
    /// </summary>
    /// <param name="durable">Whether the exchange should be durable</param>
    public async Task InitializeAsync(bool durable = false)
    {
        try
        {
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Fanout, durable, false);
            _logger.LogInformation("Fanout exchange '{ExchangeName}' declared (durable: {Durable})", _exchangeName, durable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to declare fanout exchange '{ExchangeName}'", _exchangeName);
            throw;
        }
    }

    /// <summary>
    /// Publishes a broadcast message to the fanout exchange
    /// </summary>
    /// <param name="message">The message to broadcast</param>
    /// <param name="persistent">Whether the message should be persistent</param>
    public async Task PublishBroadcastMessageAsync(string message, bool persistent = false)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(message);
            var properties = new BasicProperties();
            
            if (persistent)
            {
                properties.Persistent = true;
            }

            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: "",
                mandatory: false,
                basicProperties: properties,
                body: body);
                
            _logger.LogDebug("Published broadcast message: {Message}", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish broadcast message: {Message}", message);
            throw;
        }
    }

    /// <summary>
    /// Publishes a notification message with metadata
    /// </summary>
    /// <param name="notification">The notification to broadcast</param>
    /// <param name="persistent">Whether the message should be persistent</param>
    public async Task PublishNotificationAsync(NotificationMessage notification, bool persistent = false)
    {
        try
        {
            var body = MessageHelpers.SerializeMessage(notification);
            var properties = new BasicProperties
            {
                MessageId = notification.Id,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(notification.Timestamp).ToUnixTimeSeconds()),
                Type = notification.Type
            };
            
            if (persistent)
            {
                properties.Persistent = true;
            }

            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: "",
                mandatory: false,
                basicProperties: properties,
                body: body);
                
            _logger.LogDebug("Published notification: {Type} - {Message}", notification.Type, notification.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish notification: {Type} - {Message}", notification.Type, notification.Message);
            throw;
        }
    }

    /// <summary>
    /// Publishes multiple broadcast messages in sequence
    /// </summary>
    /// <param name="messages">List of messages to broadcast</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    /// <param name="delayMs">Delay between messages in milliseconds</param>
    public async Task PublishBroadcastMessagesAsync(IEnumerable<string> messages, bool persistent = false, int delayMs = 0)
    {
        foreach (var message in messages)
        {
            await PublishBroadcastMessageAsync(message, persistent);
            
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }
        }
        
        _logger.LogInformation("Published {Count} broadcast messages", messages.Count());
    }

    /// <summary>
    /// Publishes multiple notifications in sequence
    /// </summary>
    /// <param name="notifications">List of notifications to broadcast</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    /// <param name="delayMs">Delay between messages in milliseconds</param>
    public async Task PublishNotificationsAsync(IEnumerable<NotificationMessage> notifications, bool persistent = false, int delayMs = 0)
    {
        foreach (var notification in notifications)
        {
            await PublishNotificationAsync(notification, persistent);
            
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }
        }
        
        _logger.LogInformation("Published {Count} notifications", notifications.Count());
    }

    /// <summary>
    /// Publishes test broadcast messages for testing purposes
    /// </summary>
    /// <param name="count">Number of test messages to publish</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishTestBroadcastMessagesAsync(int count, bool persistent = false)
    {
        var messages = Enumerable.Range(1, count).Select(i => $"Broadcast message {i} - {DateTime.UtcNow:HH:mm:ss.fff}");
        await PublishBroadcastMessagesAsync(messages, persistent);
    }

    /// <summary>
    /// Publishes test notifications for testing purposes
    /// </summary>
    /// <param name="count">Number of test notifications to publish</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishTestNotificationsAsync(int count, bool persistent = false)
    {
        var notifications = new List<NotificationMessage>();
        var types = new[] { "INFO", "WARNING", "ERROR", "SUCCESS" };
        
        for (int i = 1; i <= count; i++)
        {
            var type = types[(i - 1) % types.Length];
            notifications.Add(new NotificationMessage
            {
                Id = MessageHelpers.GenerateMessageId(),
                Type = type,
                Message = $"Test notification {i} of type {type}",
                Timestamp = DateTime.UtcNow,
                Source = "PublisherService"
            });
        }
        
        await PublishNotificationsAsync(notifications, persistent);
    }

    /// <summary>
    /// Gets the exchange name used by this publisher
    /// </summary>
    public string ExchangeName => _exchangeName;

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _logger.LogDebug("PublisherService disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing PublisherService");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Represents a notification message for publish-subscribe scenarios
/// </summary>
public class NotificationMessage
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
}