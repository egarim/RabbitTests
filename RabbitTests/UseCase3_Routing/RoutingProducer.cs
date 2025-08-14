using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitTests.Infrastructure;
using System.Text;

namespace RabbitTests.UseCase3_Routing;

/// <summary>
/// Producer service for Use Case 3: Routing Pattern (Direct Exchange)
/// Publishes messages with specific routing keys to a direct exchange for selective routing
/// </summary>
public class RoutingProducer : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<RoutingProducer> _logger;
    private readonly string _exchangeName;
    private bool _disposed = false;

    public RoutingProducer(IChannel channel, ILogger<RoutingProducer> logger, string exchangeName = "test-direct-exchange")
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = exchangeName;
    }

    /// <summary>
    /// Initializes the direct exchange for routing
    /// </summary>
    /// <param name="durable">Whether the exchange should be durable</param>
    public async Task InitializeAsync(bool durable = false)
    {
        try
        {
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Direct, durable, false);
            _logger.LogInformation("Direct exchange '{ExchangeName}' declared (durable: {Durable})", _exchangeName, durable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to declare direct exchange '{ExchangeName}'", _exchangeName);
            throw;
        }
    }

    /// <summary>
    /// Publishes a message with a specific routing key
    /// </summary>
    /// <param name="routingKey">The routing key for message routing</param>
    /// <param name="message">The message to publish</param>
    /// <param name="persistent">Whether the message should be persistent</param>
    public async Task PublishMessageAsync(string routingKey, string message, bool persistent = false)
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
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
                
            _logger.LogDebug("Published message with routing key '{RoutingKey}': {Message}", routingKey, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message with routing key '{RoutingKey}': {Message}", routingKey, message);
            throw;
        }
    }

    /// <summary>
    /// Publishes a log message with severity-based routing
    /// </summary>
    /// <param name="logMessage">The log message to publish</param>
    /// <param name="persistent">Whether the message should be persistent</param>
    public async Task PublishLogMessageAsync(LogMessage logMessage, bool persistent = false)
    {
        try
        {
            var body = MessageHelpers.SerializeMessage(logMessage);
            var properties = new BasicProperties
            {
                MessageId = logMessage.Id,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(logMessage.Timestamp).ToUnixTimeSeconds()),
                Type = logMessage.Severity,
                Headers = new Dictionary<string, object>
                {
                    ["severity"] = logMessage.Severity,
                    ["source"] = logMessage.Source ?? string.Empty,
                    ["component"] = logMessage.Component ?? string.Empty
                }
            };
            
            if (persistent)
            {
                properties.Persistent = true;
            }

            // Use severity as routing key
            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: logMessage.Severity.ToLowerInvariant(),
                mandatory: false,
                basicProperties: properties,
                body: body);
                
            _logger.LogDebug("Published log message with severity '{Severity}': {Message}", logMessage.Severity, logMessage.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish log message with severity '{Severity}': {Message}", logMessage.Severity, logMessage.Message);
            throw;
        }
    }

    /// <summary>
    /// Publishes multiple messages with different routing keys
    /// </summary>
    /// <param name="messagesByRoutingKey">Dictionary of routing key to messages</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    /// <param name="delayMs">Delay between messages in milliseconds</param>
    public async Task PublishMessagesAsync(Dictionary<string, IEnumerable<string>> messagesByRoutingKey, bool persistent = false, int delayMs = 0)
    {
        foreach (var kvp in messagesByRoutingKey)
        {
            foreach (var message in kvp.Value)
            {
                await PublishMessageAsync(kvp.Key, message, persistent);
                
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }
        }
        
        var totalMessages = messagesByRoutingKey.Values.Sum(messages => messages.Count());
        _logger.LogInformation("Published {Count} messages across {RoutingKeyCount} routing keys", 
            totalMessages, messagesByRoutingKey.Count);
    }

    /// <summary>
    /// Publishes test log messages with different severity levels
    /// </summary>
    /// <param name="messagesPerSeverity">Number of messages per severity level</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishTestLogMessagesAsync(int messagesPerSeverity = 5, bool persistent = false)
    {
        var severities = new[] { "INFO", "WARNING", "ERROR", "DEBUG" };
        var logMessages = new List<LogMessage>();
        
        foreach (var severity in severities)
        {
            for (int i = 1; i <= messagesPerSeverity; i++)
            {
                logMessages.Add(new LogMessage
                {
                    Id = MessageHelpers.GenerateMessageId(),
                    Severity = severity,
                    Message = $"Test {severity.ToLower()} message {i} - {DateTime.UtcNow:HH:mm:ss.fff}",
                    Timestamp = DateTime.UtcNow,
                    Source = "RoutingProducer",
                    Component = "TestComponent"
                });
            }
        }
        
        foreach (var logMessage in logMessages)
        {
            await PublishLogMessageAsync(logMessage, persistent);
        }
        
        _logger.LogInformation("Published {Count} test log messages across {SeverityCount} severity levels", 
            logMessages.Count, severities.Length);
    }

    /// <summary>
    /// Publishes test messages for basic routing scenarios
    /// </summary>
    /// <param name="routingKeys">Routing keys to use</param>
    /// <param name="messagesPerKey">Number of messages per routing key</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishTestRoutingMessagesAsync(string[] routingKeys, int messagesPerKey = 3, bool persistent = false)
    {
        foreach (var routingKey in routingKeys)
        {
            for (int i = 1; i <= messagesPerKey; i++)
            {
                var message = $"Message {i} for routing key '{routingKey}' - {DateTime.UtcNow:HH:mm:ss.fff}";
                await PublishMessageAsync(routingKey, message, persistent);
            }
        }
        
        _logger.LogInformation("Published {Count} test messages across {RoutingKeyCount} routing keys", 
            routingKeys.Length * messagesPerKey, routingKeys.Length);
    }

    /// <summary>
    /// Gets the exchange name used by this producer
    /// </summary>
    public string ExchangeName => _exchangeName;

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _logger.LogDebug("RoutingProducer disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RoutingProducer");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Represents a log message for routing scenarios
/// </summary>
public class LogMessage
{
    public string Id { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Source { get; set; }
    public string? Component { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}