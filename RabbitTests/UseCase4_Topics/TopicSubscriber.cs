using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitTests.Infrastructure;
using System.Collections.Concurrent;
using System.Text;

namespace RabbitTests.UseCase4_Topics;

/// <summary>
/// Subscriber service for Use Case 4: Topic-Based Routing (Topic Exchange)
/// Subscribes to messages using wildcard patterns (* and #) for flexible topic-based routing
/// </summary>
public class TopicSubscriber : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<TopicSubscriber> _logger;
    private readonly string _exchangeName;
    private readonly string _subscriberId;
    private readonly ConcurrentQueue<string> _receivedMessages = new();
    private readonly ConcurrentDictionary<string, List<string>> _messagesByPattern = new();
    private readonly ConcurrentDictionary<string, List<EventMessage>> _eventsByPattern = new();
    private string? _queueName;
    private string? _consumerTag;
    private TopicSubscriberStats _stats = new();
    private bool _disposed = false;

    public TopicSubscriber(IChannel channel, ILogger<TopicSubscriber> logger, string exchangeName, string subscriberId)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exchangeName = exchangeName;
        _subscriberId = subscriberId;
        _stats.SubscriberId = subscriberId;
        _stats.StartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes the subscriber with topic patterns for binding to the exchange
    /// </summary>
    /// <param name="bindingPatterns">Topic patterns to bind to (supports * and # wildcards)</param>
    /// <param name="useNamedQueue">Whether to use a named queue (false for temporary queue)</param>
    /// <param name="exclusive">Whether the queue should be exclusive</param>
    /// <param name="durable">Whether the queue should be durable</param>
    public async Task InitializeAsync(string[] bindingPatterns, bool useNamedQueue = false, bool exclusive = true, bool durable = false)
    {
        try
        {
            // Declare queue
            var queueName = useNamedQueue ? $"topic-{_subscriberId}-queue" : string.Empty;
            var result = await _channel.QueueDeclareAsync(queueName, durable, exclusive, !durable);
            _queueName = result.QueueName;

            // Bind queue to exchange with topic patterns
            foreach (var pattern in bindingPatterns)
            {
                await _channel.QueueBindAsync(_queueName, _exchangeName, pattern);
                _messagesByPattern[pattern] = new List<string>();
                _eventsByPattern[pattern] = new List<EventMessage>();
                _logger.LogDebug("Bound queue '{QueueName}' to exchange '{ExchangeName}' with pattern '{Pattern}'", 
                    _queueName, _exchangeName, pattern);
            }

            _stats.BoundPatterns = bindingPatterns.ToArray();
            _stats.QueueName = _queueName;

            _logger.LogInformation("TopicSubscriber '{SubscriberId}' initialized with {PatternCount} binding patterns: [{Patterns}]", 
                _subscriberId, bindingPatterns.Length, string.Join(", ", bindingPatterns));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TopicSubscriber '{SubscriberId}'", _subscriberId);
            throw;
        }
    }

    /// <summary>
    /// Starts consuming messages asynchronously using event-driven approach
    /// </summary>
    /// <param name="autoAck">Whether to automatically acknowledge messages</param>
    public async Task StartConsumingAsync(bool autoAck = true)
    {
        try
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);
            
            consumer.ReceivedAsync += async (model, eventArgs) =>
            {
                try
                {
                    var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                    var routingKey = eventArgs.RoutingKey;
                    
                    _receivedMessages.Enqueue(message);
                    _stats.MessagesReceived++;
                    _stats.LastMessageTime = DateTime.UtcNow;

                    // Try to match message to binding patterns and store
                    foreach (var pattern in _stats.BoundPatterns)
                    {
                        if (IsPatternMatch(routingKey, pattern))
                        {
                            _messagesByPattern[pattern].Add(message);
                            
                            // Try to deserialize as EventMessage
                            try
                            {
                                var eventMessage = MessageHelpers.DeserializeMessage<EventMessage>(eventArgs.Body.ToArray());
                                _eventsByPattern[pattern].Add(eventMessage);
                            }
                            catch
                            {
                                // Not an EventMessage, continue with text message only
                            }
                            break;
                        }
                    }

                    _logger.LogDebug("TopicSubscriber '{SubscriberId}' received message with routing key '{RoutingKey}': {Message}", 
                        _subscriberId, routingKey, message.Length > 100 ? message[..100] + "..." : message);

                    if (!autoAck)
                    {
                        await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message in TopicSubscriber '{SubscriberId}'", _subscriberId);
                    
                    if (!autoAck)
                    {
                        await _channel.BasicNackAsync(eventArgs.DeliveryTag, false, true);
                    }
                }
            };

            _consumerTag = await _channel.BasicConsumeAsync(_queueName, autoAck, consumer);
            _logger.LogInformation("TopicSubscriber '{SubscriberId}' started consuming (autoAck: {AutoAck})", _subscriberId, autoAck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start consuming in TopicSubscriber '{SubscriberId}'", _subscriberId);
            throw;
        }
    }

    /// <summary>
    /// Consumes a specific number of messages with timeout
    /// </summary>
    /// <param name="expectedCount">Number of messages to consume</param>
    /// <param name="autoAck">Whether to automatically acknowledge messages</param>
    /// <param name="timeout">Maximum time to wait for messages</param>
    /// <returns>List of received messages</returns>
    public async Task<List<string>> ConsumeMessagesAsync(int expectedCount, bool autoAck = true, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var receivedMessages = new List<string>();
        var startTime = DateTime.UtcNow;

        try
        {
            await StartConsumingAsync(autoAck);

            while (receivedMessages.Count < expectedCount && DateTime.UtcNow - startTime < actualTimeout)
            {
                while (_receivedMessages.TryDequeue(out var message))
                {
                    receivedMessages.Add(message);
                    if (receivedMessages.Count >= expectedCount)
                        break;
                }

                if (receivedMessages.Count < expectedCount)
                {
                    await Task.Delay(100);
                }
            }

            await StopConsumingAsync();
            
            _logger.LogInformation("TopicSubscriber '{SubscriberId}' consumed {Count}/{Expected} messages in {Duration}ms", 
                _subscriberId, receivedMessages.Count, expectedCount, (DateTime.UtcNow - startTime).TotalMilliseconds);

            return receivedMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming messages in TopicSubscriber '{SubscriberId}'", _subscriberId);
            throw;
        }
    }

    /// <summary>
    /// Consumes EventMessage objects with pattern matching
    /// </summary>
    /// <param name="expectedCount">Number of events to consume</param>
    /// <param name="autoAck">Whether to automatically acknowledge messages</param>
    /// <param name="timeout">Maximum time to wait for events</param>
    /// <returns>List of received event messages</returns>
    public async Task<List<EventMessage>> ConsumeEventMessagesAsync(int expectedCount, bool autoAck = true, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var receivedEvents = new List<EventMessage>();
        var startTime = DateTime.UtcNow;

        try
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);
            
            consumer.ReceivedAsync += async (model, eventArgs) =>
            {
                try
                {
                    var eventMessage = MessageHelpers.DeserializeMessage<EventMessage>(eventArgs.Body.ToArray());
                    var routingKey = eventArgs.RoutingKey;
                    
                    receivedEvents.Add(eventMessage);
                    _stats.MessagesReceived++;
                    _stats.LastMessageTime = DateTime.UtcNow;

                    // Store in pattern-specific collections
                    foreach (var pattern in _stats.BoundPatterns)
                    {
                        if (IsPatternMatch(routingKey, pattern))
                        {
                            _eventsByPattern[pattern].Add(eventMessage);
                            break;
                        }
                    }

                    _logger.LogDebug("TopicSubscriber '{SubscriberId}' received event '{EventType}' with routing key '{RoutingKey}'", 
                        _subscriberId, eventMessage.EventType, routingKey);

                    if (!autoAck)
                    {
                        await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event message in TopicSubscriber '{SubscriberId}'", _subscriberId);
                    
                    if (!autoAck)
                    {
                        await _channel.BasicNackAsync(eventArgs.DeliveryTag, false, true);
                    }
                }
            };

            _consumerTag = await _channel.BasicConsumeAsync(_queueName, autoAck, consumer);

            while (receivedEvents.Count < expectedCount && DateTime.UtcNow - startTime < actualTimeout)
            {
                await Task.Delay(100);
            }

            await StopConsumingAsync();
            
            _logger.LogInformation("TopicSubscriber '{SubscriberId}' consumed {Count}/{Expected} event messages in {Duration}ms", 
                _subscriberId, receivedEvents.Count, expectedCount, (DateTime.UtcNow - startTime).TotalMilliseconds);

            return receivedEvents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming event messages in TopicSubscriber '{SubscriberId}'", _subscriberId);
            throw;
        }
    }

    /// <summary>
    /// Stops consuming messages
    /// </summary>
    public async Task StopConsumingAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_consumerTag))
            {
                await _channel.BasicCancelAsync(_consumerTag);
                _consumerTag = null;
                _logger.LogDebug("TopicSubscriber '{SubscriberId}' stopped consuming", _subscriberId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping consumer in TopicSubscriber '{SubscriberId}'", _subscriberId);
        }
    }

    /// <summary>
    /// Adds new binding patterns to the existing subscription
    /// </summary>
    /// <param name="newPatterns">New patterns to bind</param>
    public async Task AddBindingPatternsAsync(string[] newPatterns)
    {
        foreach (var pattern in newPatterns)
        {
            if (!_stats.BoundPatterns.Contains(pattern))
            {
                await _channel.QueueBindAsync(_queueName, _exchangeName, pattern);
                _messagesByPattern[pattern] = new List<string>();
                _eventsByPattern[pattern] = new List<EventMessage>();
                
                var updatedPatterns = _stats.BoundPatterns.ToList();
                updatedPatterns.Add(pattern);
                _stats.BoundPatterns = updatedPatterns.ToArray();
                
                _logger.LogInformation("Added binding pattern '{Pattern}' to TopicSubscriber '{SubscriberId}'", pattern, _subscriberId);
            }
        }
    }

    /// <summary>
    /// Removes binding patterns from the subscription
    /// </summary>
    /// <param name="patternsToRemove">Patterns to unbind</param>
    public async Task RemoveBindingPatternsAsync(string[] patternsToRemove)
    {
        foreach (var pattern in patternsToRemove)
        {
            if (_stats.BoundPatterns.Contains(pattern))
            {
                await _channel.QueueUnbindAsync(_queueName, _exchangeName, pattern);
                _messagesByPattern.TryRemove(pattern, out _);
                _eventsByPattern.TryRemove(pattern, out _);
                
                var updatedPatterns = _stats.BoundPatterns.Where(p => p != pattern).ToArray();
                _stats.BoundPatterns = updatedPatterns;
                
                _logger.LogInformation("Removed binding pattern '{Pattern}' from TopicSubscriber '{SubscriberId}'", pattern, _subscriberId);
            }
        }
    }

    /// <summary>
    /// Checks if a routing key matches a topic pattern
    /// </summary>
    /// <param name="routingKey">The routing key to check</param>
    /// <param name="pattern">The topic pattern with wildcards</param>
    /// <returns>True if the routing key matches the pattern</returns>
    private bool IsPatternMatch(string routingKey, string pattern)
    {
        var routingParts = routingKey.Split('.');
        var patternParts = pattern.Split('.');

        return IsPatternMatchRecursive(routingParts, 0, patternParts, 0);
    }

    /// <summary>
    /// Recursive pattern matching for topic patterns
    /// </summary>
    private bool IsPatternMatchRecursive(string[] routingParts, int routingIndex, string[] patternParts, int patternIndex)
    {
        // End of both - match
        if (routingIndex >= routingParts.Length && patternIndex >= patternParts.Length)
            return true;

        // End of pattern but not routing - no match (unless last pattern part is #)
        if (patternIndex >= patternParts.Length)
            return false;

        // # matches zero or more words
        if (patternParts[patternIndex] == "#")
        {
            // # at end of pattern matches everything remaining
            if (patternIndex == patternParts.Length - 1)
                return true;

            // Try matching # with different number of words
            for (int i = routingIndex; i <= routingParts.Length; i++)
            {
                if (IsPatternMatchRecursive(routingParts, i, patternParts, patternIndex + 1))
                    return true;
            }
            return false;
        }

        // End of routing but not pattern - no match
        if (routingIndex >= routingParts.Length)
            return false;

        // * matches exactly one word
        if (patternParts[patternIndex] == "*")
        {
            return IsPatternMatchRecursive(routingParts, routingIndex + 1, patternParts, patternIndex + 1);
        }

        // Exact word match
        if (routingParts[routingIndex] == patternParts[patternIndex])
        {
            return IsPatternMatchRecursive(routingParts, routingIndex + 1, patternParts, patternIndex + 1);
        }

        return false;
    }

    /// <summary>
    /// Gets all received messages
    /// </summary>
    public List<string> GetReceivedMessages()
    {
        return _receivedMessages.ToList();
    }

    /// <summary>
    /// Gets messages grouped by binding pattern
    /// </summary>
    public Dictionary<string, List<string>> GetMessagesByPattern()
    {
        return _messagesByPattern.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
    }

    /// <summary>
    /// Gets event messages grouped by binding pattern
    /// </summary>
    public Dictionary<string, List<EventMessage>> GetEventsByPattern()
    {
        return _eventsByPattern.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
    }

    /// <summary>
    /// Clears all received messages
    /// </summary>
    public void ClearReceived()
    {
        while (_receivedMessages.TryDequeue(out _)) { }
        
        foreach (var pattern in _messagesByPattern.Keys.ToList())
        {
            _messagesByPattern[pattern].Clear();
        }
        
        foreach (var pattern in _eventsByPattern.Keys.ToList())
        {
            _eventsByPattern[pattern].Clear();
        }
    }

    /// <summary>
    /// Gets subscriber statistics
    /// </summary>
    public TopicSubscriberStats GetStats()
    {
        return _stats with { }; // Return a copy
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopConsumingAsync().GetAwaiter().GetResult();
                _logger.LogDebug("TopicSubscriber '{SubscriberId}' disposed", _subscriberId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing TopicSubscriber '{SubscriberId}'", _subscriberId);
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Statistics for topic subscriber
/// </summary>
public record TopicSubscriberStats
{
    public string SubscriberId { get; set; } = string.Empty;
    public int MessagesReceived { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? LastMessageTime { get; set; }
    public string[] BoundPatterns { get; set; } = Array.Empty<string>();
    public string? QueueName { get; set; }
}