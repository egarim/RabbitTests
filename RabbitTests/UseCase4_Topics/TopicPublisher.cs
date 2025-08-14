using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitTests.Infrastructure;
using System.Text;

namespace RabbitTests.UseCase4_Topics;

/// <summary>
/// Publisher service for Use Case 4: Topic-Based Routing (Topic Exchange)
/// Publishes messages with hierarchical routing keys to a topic exchange for pattern-based routing
/// </summary>
public class TopicPublisher : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<TopicPublisher> _logger;
    private readonly string _exchangeName;
    private bool _disposed = false;

    public TopicPublisher(IChannel channel, ILogger<TopicPublisher> logger, string exchangeName = "test-topic-exchange")
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = exchangeName;
    }

    /// <summary>
    /// Initializes the topic exchange for pattern-based routing
    /// </summary>
    /// <param name="durable">Whether the exchange should be durable</param>
    public async Task InitializeAsync(bool durable = false)
    {
        try
        {
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, durable, false);
            _logger.LogInformation("Topic exchange '{ExchangeName}' declared (durable: {Durable})", _exchangeName, durable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to declare topic exchange '{ExchangeName}'", _exchangeName);
            throw;
        }
    }

    /// <summary>
    /// Publishes a message with a hierarchical routing key
    /// </summary>
    /// <param name="routingKey">The hierarchical routing key (e.g., "logs.error.database")</param>
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
    /// Publishes an event message with hierarchical routing
    /// </summary>
    /// <param name="eventMessage">The event message to publish</param>
    /// <param name="persistent">Whether the message should be persistent</param>
    public async Task PublishEventMessageAsync(EventMessage eventMessage, bool persistent = false)
    {
        try
        {
            var body = MessageHelpers.SerializeMessage(eventMessage);
            var properties = new BasicProperties
            {
                MessageId = eventMessage.Id,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(eventMessage.Timestamp).ToUnixTimeSeconds()),
                Type = eventMessage.EventType,
                Headers = new Dictionary<string, object>
                {
                    ["system"] = eventMessage.System ?? string.Empty,
                    ["level"] = eventMessage.Level ?? string.Empty,
                    ["component"] = eventMessage.Component ?? string.Empty,
                    ["eventType"] = eventMessage.EventType ?? string.Empty
                }
            };
            
            if (persistent)
            {
                properties.Persistent = true;
            }

            // Use hierarchical routing key: system.level.component
            var routingKey = eventMessage.GetRoutingKey();
            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
                
            _logger.LogDebug("Published event message with routing key '{RoutingKey}': {EventType}", routingKey, eventMessage.EventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event message with routing key '{RoutingKey}': {EventType}", 
                eventMessage.GetRoutingKey(), eventMessage.EventType);
            throw;
        }
    }

    /// <summary>
    /// Publishes test log messages with hierarchical routing keys
    /// </summary>
    /// <param name="messagesPerPattern">Number of messages per routing pattern</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishTestLogMessagesAsync(int messagesPerPattern = 3, bool persistent = false)
    {
        var patterns = new[]
        {
            ("logs.info.web", "Web server information"),
            ("logs.warning.web", "Web server warning"),
            ("logs.error.web", "Web server error"),
            ("logs.info.database", "Database information"),
            ("logs.warning.database", "Database warning"),
            ("logs.error.database", "Database error"),
            ("logs.info.auth", "Authentication information"),
            ("logs.warning.auth", "Authentication warning"),
            ("logs.error.auth", "Authentication error"),
            ("logs.debug.cache", "Cache debug information")
        };

        foreach (var (routingKey, description) in patterns)
        {
            for (int i = 1; i <= messagesPerPattern; i++)
            {
                var message = $"{description} - Message {i} at {DateTime.UtcNow:HH:mm:ss.fff}";
                await PublishMessageAsync(routingKey, message, persistent);
            }
        }
        
        _logger.LogInformation("Published {Count} test log messages across {PatternCount} routing patterns", 
            patterns.Length * messagesPerPattern, patterns.Length);
    }

    /// <summary>
    /// Publishes test microservice event messages
    /// </summary>
    /// <param name="messagesPerService">Number of messages per service</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishTestMicroserviceEventsAsync(int messagesPerService = 2, bool persistent = false)
    {
        var events = new[]
        {
            new EventMessage { System = "user", Level = "service", Component = "registration", EventType = "UserRegistered" },
            new EventMessage { System = "user", Level = "service", Component = "login", EventType = "UserLoggedIn" },
            new EventMessage { System = "user", Level = "service", Component = "profile", EventType = "ProfileUpdated" },
            new EventMessage { System = "order", Level = "service", Component = "creation", EventType = "OrderCreated" },
            new EventMessage { System = "order", Level = "service", Component = "payment", EventType = "PaymentProcessed" },
            new EventMessage { System = "order", Level = "service", Component = "shipping", EventType = "OrderShipped" },
            new EventMessage { System = "inventory", Level = "service", Component = "stock", EventType = "StockUpdated" },
            new EventMessage { System = "inventory", Level = "service", Component = "alert", EventType = "LowStockAlert" },
            new EventMessage { System = "notification", Level = "service", Component = "email", EventType = "EmailSent" },
            new EventMessage { System = "notification", Level = "service", Component = "sms", EventType = "SmsSent" }
        };

        foreach (var eventTemplate in events)
        {
            for (int i = 1; i <= messagesPerService; i++)
            {
                var eventMessage = new EventMessage
                {
                    Id = MessageHelpers.GenerateMessageId(),
                    System = eventTemplate.System,
                    Level = eventTemplate.Level,
                    Component = eventTemplate.Component,
                    EventType = eventTemplate.EventType,
                    Message = $"{eventTemplate.EventType} - Event {i} at {DateTime.UtcNow:HH:mm:ss.fff}",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>
                    {
                        ["eventNumber"] = i,
                        ["batchId"] = Guid.NewGuid().ToString("N")[..8]
                    }
                };
                
                await PublishEventMessageAsync(eventMessage, persistent);
            }
        }
        
        _logger.LogInformation("Published {Count} test microservice event messages across {ServiceCount} services", 
            events.Length * messagesPerService, events.Length);
    }

    /// <summary>
    /// Publishes messages with complex hierarchical patterns
    /// </summary>
    /// <param name="messagesPerPattern">Number of messages per pattern</param>
    /// <param name="persistent">Whether messages should be persistent</param>
    public async Task PublishComplexPatternMessagesAsync(int messagesPerPattern = 2, bool persistent = false)
    {
        var complexPatterns = new[]
        {
            "system.app1.module.auth.login.success",
            "system.app1.module.auth.login.failure",
            "system.app1.module.data.query.slow",
            "system.app1.module.data.query.fast",
            "system.app2.service.user.create.validation",
            "system.app2.service.user.update.notification",
            "system.app2.service.order.process.payment",
            "system.app2.service.order.process.shipping",
            "analytics.realtime.user.session.start",
            "analytics.realtime.user.session.end",
            "analytics.batch.report.daily.generate",
            "analytics.batch.report.weekly.generate",
            "monitoring.health.database.connection.ok",
            "monitoring.health.database.connection.fail",
            "monitoring.performance.api.response.slow",
            "monitoring.performance.api.response.normal"
        };

        foreach (var routingKey in complexPatterns)
        {
            for (int i = 1; i <= messagesPerPattern; i++)
            {
                var message = $"Complex pattern message for '{routingKey}' - #{i} at {DateTime.UtcNow:HH:mm:ss.fff}";
                await PublishMessageAsync(routingKey, message, persistent);
            }
        }
        
        _logger.LogInformation("Published {Count} complex pattern messages across {PatternCount} routing patterns", 
            complexPatterns.Length * messagesPerPattern, complexPatterns.Length);
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
                _logger.LogDebug("TopicPublisher disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing TopicPublisher");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Represents an event message for topic-based routing scenarios
/// </summary>
public class EventMessage
{
    public string Id { get; set; } = MessageHelpers.GenerateMessageId();
    public string System { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Generates hierarchical routing key from event properties
    /// </summary>
    public string GetRoutingKey()
    {
        return $"{System}.{Level}.{Component}";
    }

    /// <summary>
    /// Gets a display name for the event
    /// </summary>
    public string GetDisplayName()
    {
        return $"{System}/{Level}/{Component} - {EventType}";
    }
}