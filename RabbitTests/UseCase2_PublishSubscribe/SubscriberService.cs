using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitTests.Infrastructure;
using System.Collections.Concurrent;
using System.Text;

namespace RabbitTests.UseCase2_PublishSubscribe;

/// <summary>
/// Subscriber service for Use Case 2: Publish-Subscribe Pattern (Fanout Exchange)
/// Subscribes to messages from a fanout exchange using its own exclusive queue
/// </summary>
public class SubscriberService : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<SubscriberService> _logger;
    private readonly string _exchangeName;
    private readonly string _subscriberId;
    private string _queueName = string.Empty;
    private string _consumerTag = string.Empty;
    private bool _isConsuming = false;
    private bool _disposed = false;

    private readonly ConcurrentQueue<string> _receivedMessages = new();
    private readonly ConcurrentQueue<NotificationMessage> _receivedNotifications = new();
    private readonly SubscriberStats _stats = new();

    // Events for message processing
    public event Action<string, string>? OnMessageReceived; // message, subscriberId
    public event Action<NotificationMessage, string>? OnNotificationReceived; // notification, subscriberId
    public event Action<Exception, string>? OnError; // exception, subscriberId

    public SubscriberService(IChannel channel, ILogger<SubscriberService> logger, string exchangeName = "test-fanout-exchange", string? subscriberId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = exchangeName;
        _subscriberId = subscriberId ?? $"subscriber-{Guid.NewGuid():N}";
        _stats.SubscriberId = _subscriberId;
        _stats.StartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes the subscriber with its own exclusive queue bound to the fanout exchange
    /// </summary>
    /// <param name="useTemporaryQueue">Whether to use a temporary auto-delete queue</param>
    /// <param name="exclusive">Whether the queue should be exclusive to this connection</param>
    public async Task InitializeAsync(bool useTemporaryQueue = true, bool exclusive = true)
    {
        try
        {
            // Declare the exchange (idempotent)
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Fanout, false, false);

            // Declare queue - use temporary exclusive queue for pub/sub pattern
            if (useTemporaryQueue)
            {
                var queueResult = await _channel.QueueDeclareAsync("", false, exclusive, true);
                _queueName = queueResult.QueueName;
            }
            else
            {
                _queueName = $"subscriber-queue-{_subscriberId}";
                await _channel.QueueDeclareAsync(_queueName, false, exclusive, true);
            }

            // Bind queue to exchange (no routing key needed for fanout)
            await _channel.QueueBindAsync(_queueName, _exchangeName, "");

            _logger.LogInformation("Subscriber '{SubscriberId}' initialized with queue '{QueueName}' bound to exchange '{ExchangeName}'",
                _subscriberId, _queueName, _exchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize subscriber '{SubscriberId}'", _subscriberId);
            throw;
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
            _logger.LogWarning("Subscriber '{SubscriberId}' is already consuming", _subscriberId);
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
                    _logger.LogError(ex, "Error processing message in subscriber '{SubscriberId}'", _subscriberId);
                    OnError?.Invoke(ex, _subscriberId);
                }
            };

            _consumerTag = await _channel.BasicConsumeAsync(_queueName, autoAck, consumer);
            _isConsuming = true;

            _logger.LogInformation("Subscriber '{SubscriberId}' started consuming from queue '{QueueName}' (autoAck: {AutoAck})",
                _subscriberId, _queueName, autoAck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start consuming for subscriber '{SubscriberId}'", _subscriberId);
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

            _logger.LogInformation("Subscriber '{SubscriberId}' stopped consuming", _subscriberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping consumer for subscriber '{SubscriberId}'", _subscriberId);
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
                    _logger.LogDebug("Subscriber '{SubscriberId}' received message: {Message}", _subscriberId, message);
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

        _logger.LogInformation("Subscriber '{SubscriberId}' consumed {Count}/{Expected} messages",
            _subscriberId, receivedMessages.Count, messageCount);

        return receivedMessages;
    }

    /// <summary>
    /// Consumes notifications and returns them
    /// </summary>
    /// <param name="notificationCount">Number of notifications to consume</param>
    /// <param name="autoAck">Whether to auto-acknowledge messages</param>
    /// <param name="timeout">Timeout for receiving notifications</param>
    /// <returns>List of received notifications</returns>
    public async Task<List<NotificationMessage>> ConsumeNotificationsAsync(int notificationCount, bool autoAck = true, TimeSpan? timeout = null)
    {
        var receivedNotifications = new List<NotificationMessage>();
        var timeoutTime = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(30));
        
        await StartConsumingAsync(autoAck);

        try
        {
            while (receivedNotifications.Count < notificationCount && DateTime.UtcNow < timeoutTime)
            {
                if (_receivedNotifications.TryDequeue(out var notification))
                {
                    receivedNotifications.Add(notification);
                    _logger.LogDebug("Subscriber '{SubscriberId}' received notification: {Type} - {Message}", 
                        _subscriberId, notification.Type, notification.Message);
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

        _logger.LogInformation("Subscriber '{SubscriberId}' consumed {Count}/{Expected} notifications",
            _subscriberId, receivedNotifications.Count, notificationCount);

        return receivedNotifications;
    }

    /// <summary>
    /// Processes an incoming message
    /// </summary>
    private async Task ProcessMessageAsync(BasicDeliverEventArgs ea, bool autoAck)
    {
        try
        {
            var messageBody = Encoding.UTF8.GetString(ea.Body.ToArray());
            
            // Update statistics
            _stats.MessagesReceived++;
            _stats.LastMessageTime = DateTime.UtcNow;

            // Try to deserialize as notification first, fallback to plain text
            try
            {
                var notification = MessageHelpers.DeserializeMessage<NotificationMessage>(ea.Body.ToArray());
                _receivedNotifications.Enqueue(notification);
                OnNotificationReceived?.Invoke(notification, _subscriberId);
                _logger.LogDebug("Subscriber '{SubscriberId}' processed notification: {Type}", _subscriberId, notification.Type);
            }
            catch
            {
                // If deserialization fails, treat as plain text message
                _receivedMessages.Enqueue(messageBody);
                OnMessageReceived?.Invoke(messageBody, _subscriberId);
                _logger.LogDebug("Subscriber '{SubscriberId}' processed text message", _subscriberId);
            }

            // Manual acknowledgment if needed
            if (!autoAck)
            {
                await _channel.BasicAckAsync(ea.DeliveryTag, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message in subscriber '{SubscriberId}'", _subscriberId);
            
            // Reject message without requeue on processing error
            if (!autoAck)
            {
                await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Gets the current statistics for this subscriber
    /// </summary>
    public SubscriberStats GetStats()
    {
        return new SubscriberStats
        {
            SubscriberId = _stats.SubscriberId,
            MessagesReceived = _stats.MessagesReceived,
            StartTime = _stats.StartTime,
            LastMessageTime = _stats.LastMessageTime,
            QueueName = _queueName
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
    /// Gets all received notifications
    /// </summary>
    public List<NotificationMessage> GetReceivedNotifications()
    {
        return _receivedNotifications.ToList();
    }

    /// <summary>
    /// Clears all received messages and notifications
    /// </summary>
    public void ClearReceived()
    {
        _receivedMessages.Clear();
        _receivedNotifications.Clear();
        _stats.MessagesReceived = 0;
    }

    /// <summary>
    /// Gets the subscriber ID
    /// </summary>
    public string SubscriberId => _subscriberId;

    /// <summary>
    /// Gets the queue name (available after initialization)
    /// </summary>
    public string QueueName => _queueName;

    /// <summary>
    /// Indicates if the subscriber is currently consuming
    /// </summary>
    public bool IsConsuming => _isConsuming;

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopConsumingAsync().Wait();
                _logger.LogDebug("Subscriber '{SubscriberId}' disposed", _subscriberId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing subscriber '{SubscriberId}'", _subscriberId);
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Statistics for a subscriber
/// </summary>
public class SubscriberStats
{
    public string SubscriberId { get; set; } = string.Empty;
    public int MessagesReceived { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? LastMessageTime { get; set; }
    public string QueueName { get; set; } = string.Empty;
}