using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitTests.Infrastructure;
using System.Collections.Concurrent;
using System.Text;

namespace RabbitTests.UseCase3_Routing;

/// <summary>
/// Consumer service for Use Case 3: Routing Pattern (Direct Exchange)
/// Consumes messages from a direct exchange based on specific routing key bindings
/// </summary>
public class RoutingConsumer : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<RoutingConsumer> _logger;
    private readonly string _exchangeName;
    private readonly string _consumerId;
    private string _queueName = string.Empty;
    private string _consumerTag = string.Empty;
    private bool _isConsuming = false;
    private bool _disposed = false;
    
    private readonly List<string> _boundRoutingKeys = new();
    private readonly ConcurrentQueue<string> _receivedMessages = new();
    private readonly ConcurrentQueue<LogMessage> _receivedLogMessages = new();
    private readonly ConcurrentDictionary<string, List<string>> _messagesByRoutingKey = new();
    private readonly RoutingConsumerStats _stats = new();

    // Events for message processing
    public event Action<string, string, string>? OnMessageReceived; // message, routingKey, consumerId
    public event Action<LogMessage, string, string>? OnLogMessageReceived; // logMessage, routingKey, consumerId
    public event Action<Exception, string>? OnError; // exception, consumerId

    public RoutingConsumer(IChannel channel, ILogger<RoutingConsumer> logger, string exchangeName = "test-direct-exchange", string? consumerId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = exchangeName;
        _consumerId = consumerId ?? $"consumer-{Guid.NewGuid():N}";
        _stats.ConsumerId = _consumerId;
        _stats.StartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes the consumer with a queue bound to specific routing keys
    /// </summary>
    /// <param name="routingKeys">Routing keys to bind to</param>
    /// <param name="useNamedQueue">Whether to use a named queue (false for temporary)</param>
    /// <param name="exclusive">Whether the queue should be exclusive to this connection</param>
    /// <param name="durable">Whether the exchange and queue should be durable</param>
    public async Task InitializeAsync(string[] routingKeys, bool useNamedQueue = false, bool exclusive = false, bool durable = false)
    {
        try
        {
            // Declare the exchange (idempotent)
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Direct, durable, false);

            // Declare queue
            if (useNamedQueue)
            {
                _queueName = $"routing-queue-{_consumerId}";
                await _channel.QueueDeclareAsync(_queueName, durable, exclusive, !durable);
            }
            else
            {
                var queueResult = await _channel.QueueDeclareAsync("", false, exclusive, true);
                _queueName = queueResult.QueueName;
            }

            // Bind queue to exchange with each routing key
            foreach (var routingKey in routingKeys)
            {
                await _channel.QueueBindAsync(_queueName, _exchangeName, routingKey);
                _boundRoutingKeys.Add(routingKey);
                _messagesByRoutingKey[routingKey] = new List<string>();
            }

            _logger.LogInformation("Consumer '{ConsumerId}' initialized with queue '{QueueName}' bound to exchange '{ExchangeName}' with routing keys: [{RoutingKeys}] (durable: {Durable})",
                _consumerId, _queueName, _exchangeName, string.Join(", ", routingKeys), durable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize consumer '{ConsumerId}'", _consumerId);
            throw;
        }
    }

    /// <summary>
    /// Initializes consumer for severity-based log routing
    /// </summary>
    /// <param name="severityLevels">Severity levels to subscribe to (info, warning, error, debug)</param>
    /// <param name="useNamedQueue">Whether to use a named queue</param>
    /// <param name="exclusive">Whether the queue should be exclusive</param>
    /// <param name="durable">Whether resources should be durable</param>
    public async Task InitializeForLogRoutingAsync(string[] severityLevels, bool useNamedQueue = false, bool exclusive = false, bool durable = false)
    {
        var routingKeys = severityLevels.Select(s => s.ToLowerInvariant()).ToArray();
        await InitializeAsync(routingKeys, useNamedQueue, exclusive, durable);
    }

    /// <summary>
    /// Adds additional routing key bindings to existing queue
    /// </summary>
    /// <param name="newRoutingKeys">New routing keys to bind</param>
    public async Task AddRoutingKeysAsync(string[] newRoutingKeys)
    {
        if (string.IsNullOrEmpty(_queueName))
        {
            throw new InvalidOperationException("Consumer must be initialized before adding routing keys");
        }

        foreach (var routingKey in newRoutingKeys)
        {
            if (!_boundRoutingKeys.Contains(routingKey))
            {
                await _channel.QueueBindAsync(_queueName, _exchangeName, routingKey);
                _boundRoutingKeys.Add(routingKey);
                _messagesByRoutingKey[routingKey] = new List<string>();
                
                _logger.LogInformation("Added routing key '{RoutingKey}' to consumer '{ConsumerId}'", routingKey, _consumerId);
            }
        }
    }

    /// <summary>
    /// Removes routing key bindings from the queue
    /// </summary>
    /// <param name="routingKeysToRemove">Routing keys to unbind</param>
    public async Task RemoveRoutingKeysAsync(string[] routingKeysToRemove)
    {
        if (string.IsNullOrEmpty(_queueName))
        {
            throw new InvalidOperationException("Consumer must be initialized before removing routing keys");
        }

        foreach (var routingKey in routingKeysToRemove)
        {
            if (_boundRoutingKeys.Contains(routingKey))
            {
                await _channel.QueueUnbindAsync(_queueName, _exchangeName, routingKey);
                _boundRoutingKeys.Remove(routingKey);
                
                _logger.LogInformation("Removed routing key '{RoutingKey}' from consumer '{ConsumerId}'", routingKey, _consumerId);
            }
        }
    }

    /// <summary>
    /// Starts consuming messages from the bound queue
    /// </summary>
    /// <param name="autoAck">Whether to auto-acknowledge messages</param>
    public async Task StartConsumingAsync(bool autoAck = true)
    {
        if (_isConsuming)
        {
            _logger.LogWarning("Consumer '{ConsumerId}' is already consuming", _consumerId);
            return;
        }

        try
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);
            
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    await ProcessMessageAsync(ea, autoAck);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message in consumer '{ConsumerId}'", _consumerId);
                    OnError?.Invoke(ex, _consumerId);
                }
            };

            _consumerTag = await _channel.BasicConsumeAsync(_queueName, autoAck, consumer);
            _isConsuming = true;

            _logger.LogInformation("Consumer '{ConsumerId}' started consuming from queue '{QueueName}' (autoAck: {AutoAck})",
                _consumerId, _queueName, autoAck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start consuming for consumer '{ConsumerId}'", _consumerId);
            throw;
        }
    }

    /// <summary>
    /// Stops consuming messages
    /// </summary>
    public async Task StopConsumingAsync()
    {
        if (!_isConsuming || string.IsNullOrEmpty(_consumerTag))
        {
            return;
        }

        try
        {
            await _channel.BasicCancelAsync(_consumerTag);
            _isConsuming = false;
            _consumerTag = string.Empty;

            _logger.LogInformation("Consumer '{ConsumerId}' stopped consuming", _consumerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping consumer for consumer '{ConsumerId}'", _consumerId);
        }
    }

    /// <summary>
    /// Consumes a specific number of messages and returns them
    /// </summary>
    /// <param name="messageCount">Number of messages to consume</param>
    /// <param name="autoAck">Whether to auto-acknowledge messages</param>
    /// <param name="timeout">Timeout for receiving messages</param>
    /// <returns>List of received messages</returns>
    public async Task<List<string>> ConsumeMessagesAsync(int messageCount, bool autoAck = true, TimeSpan? timeout = null)
    {
        var receivedMessages = new List<string>();
        var timeoutTime = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(30));
        
        await StartConsumingAsync(autoAck);

        try
        {
            while (receivedMessages.Count < messageCount && DateTime.UtcNow < timeoutTime)
            {
                if (_receivedMessages.TryDequeue(out var message))
                {
                    receivedMessages.Add(message);
                    _logger.LogDebug("Consumer '{ConsumerId}' received message: {Message}", _consumerId, message);
                }
                else
                {
                    await Task.Delay(100); // Small delay to avoid busy waiting
                }
            }
        }
        finally
        {
            await StopConsumingAsync();
        }

        _logger.LogInformation("Consumer '{ConsumerId}' consumed {Count}/{Expected} messages",
            _consumerId, receivedMessages.Count, messageCount);

        return receivedMessages;
    }

    /// <summary>
    /// Consumes log messages and returns them
    /// </summary>
    /// <param name="messageCount">Number of log messages to consume</param>
    /// <param name="autoAck">Whether to auto-acknowledge messages</param>
    /// <param name="timeout">Timeout for receiving messages</param>
    /// <returns>List of received log messages</returns>
    public async Task<List<LogMessage>> ConsumeLogMessagesAsync(int messageCount, bool autoAck = true, TimeSpan? timeout = null)
    {
        var receivedLogMessages = new List<LogMessage>();
        var timeoutTime = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(30));
        
        await StartConsumingAsync(autoAck);

        try
        {
            while (receivedLogMessages.Count < messageCount && DateTime.UtcNow < timeoutTime)
            {
                if (_receivedLogMessages.TryDequeue(out var logMessage))
                {
                    receivedLogMessages.Add(logMessage);
                    _logger.LogDebug("Consumer '{ConsumerId}' received log message: {Severity} - {Message}", 
                        _consumerId, logMessage.Severity, logMessage.Message);
                }
                else
                {
                    await Task.Delay(100);
                }
            }
        }
        finally
        {
            await StopConsumingAsync();
        }

        _logger.LogInformation("Consumer '{ConsumerId}' consumed {Count}/{Expected} log messages",
            _consumerId, receivedLogMessages.Count, messageCount);

        return receivedLogMessages;
    }

    /// <summary>
    /// Processes an incoming message
    /// </summary>
    private async Task ProcessMessageAsync(BasicDeliverEventArgs ea, bool autoAck)
    {
        try
        {
            var messageBody = Encoding.UTF8.GetString(ea.Body.ToArray());
            var routingKey = ea.RoutingKey;
            
            // Update statistics
            _stats.MessagesReceived++;
            _stats.LastMessageTime = DateTime.UtcNow;
            
            // Track messages by routing key
            if (_messagesByRoutingKey.ContainsKey(routingKey))
            {
                _messagesByRoutingKey[routingKey].Add(messageBody);
            }

            // Try to deserialize as log message first, fallback to plain text
            try
            {
                var logMessage = MessageHelpers.DeserializeMessage<LogMessage>(ea.Body.ToArray());
                _receivedLogMessages.Enqueue(logMessage);
                OnLogMessageReceived?.Invoke(logMessage, routingKey, _consumerId);
                _logger.LogDebug("Consumer '{ConsumerId}' processed log message with routing key '{RoutingKey}': {Severity}", 
                    _consumerId, routingKey, logMessage.Severity);
            }
            catch
            {
                // If deserialization fails, treat as plain text message
                _receivedMessages.Enqueue(messageBody);
                OnMessageReceived?.Invoke(messageBody, routingKey, _consumerId);
                _logger.LogDebug("Consumer '{ConsumerId}' processed text message with routing key '{RoutingKey}'", 
                    _consumerId, routingKey);
            }

            // Manual acknowledgment if needed
            if (!autoAck)
            {
                await _channel.BasicAckAsync(ea.DeliveryTag, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message in consumer '{ConsumerId}'", _consumerId);
            
            // Reject message without requeue on processing error
            if (!autoAck)
            {
                await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Gets the current statistics for this consumer
    /// </summary>
    public RoutingConsumerStats GetStats()
    {
        return new RoutingConsumerStats
        {
            ConsumerId = _stats.ConsumerId,
            MessagesReceived = _stats.MessagesReceived,
            StartTime = _stats.StartTime,
            LastMessageTime = _stats.LastMessageTime,
            QueueName = _queueName,
            BoundRoutingKeys = _boundRoutingKeys.ToList()
        };
    }

    /// <summary>
    /// Gets all received messages
    /// </summary>
    public List<string> GetReceivedMessages()
    {
        return _receivedMessages.ToList();
    }

    /// <summary>
    /// Gets all received log messages
    /// </summary>
    public List<LogMessage> GetReceivedLogMessages()
    {
        return _receivedLogMessages.ToList();
    }

    /// <summary>
    /// Gets messages grouped by routing key
    /// </summary>
    public Dictionary<string, List<string>> GetMessagesByRoutingKey()
    {
        return _messagesByRoutingKey.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
    }

    /// <summary>
    /// Clears all received messages and log messages
    /// </summary>
    public void ClearReceived()
    {
        _receivedMessages.Clear();
        _receivedLogMessages.Clear();
        foreach (var key in _messagesByRoutingKey.Keys)
        {
            _messagesByRoutingKey[key].Clear();
        }
        _stats.MessagesReceived = 0;
    }

    /// <summary>
    /// Gets the consumer ID
    /// </summary>
    public string ConsumerId => _consumerId;

    /// <summary>
    /// Gets the queue name (available after initialization)
    /// </summary>
    public string QueueName => _queueName;

    /// <summary>
    /// Gets the bound routing keys
    /// </summary>
    public IReadOnlyList<string> BoundRoutingKeys => _boundRoutingKeys.AsReadOnly();

    /// <summary>
    /// Indicates if the consumer is currently consuming
    /// </summary>
    public bool IsConsuming => _isConsuming;

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopConsumingAsync().Wait();
                _logger.LogDebug("Consumer '{ConsumerId}' disposed", _consumerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing consumer '{ConsumerId}'", _consumerId);
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Statistics for a routing consumer
/// </summary>
public class RoutingConsumerStats
{
    public string ConsumerId { get; set; } = string.Empty;
    public int MessagesReceived { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? LastMessageTime { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public List<string> BoundRoutingKeys { get; set; } = new();
}