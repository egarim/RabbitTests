using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitTests.Infrastructure;

namespace RabbitTests.UseCase1_WorkQueue;

/// <summary>
/// Consumer for Work Queue pattern - processes tasks from a queue
/// </summary>
public class WorkQueueConsumer : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<WorkQueueConsumer> _logger;
    private readonly string _queueName;
    private readonly string _consumerId;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;
    private bool _disposed = false;
    private readonly ConsumerStats _stats;

    public WorkQueueConsumer(IChannel channel, ILogger<WorkQueueConsumer> logger, string queueName = "work-queue", string? consumerId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueName = queueName;
        _consumerId = consumerId ?? $"consumer-{Guid.NewGuid():N}";
        _stats = new ConsumerStats 
        { 
            ConsumerId = _consumerId,
            StartTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Starts consuming messages from the work queue
    /// </summary>
    public async Task StartConsumingAsync(bool autoAck = false, int prefetchCount = 1)
    {
        // Set QoS to control how many messages this consumer can process at once
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: (ushort)prefetchCount, global: false);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += async (sender, eventArgs) =>
        {
            await ProcessMessageAsync(eventArgs, autoAck);
        };

        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: autoAck,
            consumer: _consumer);

        _logger.LogInformation("Consumer {ConsumerId} started consuming from queue {QueueName} (autoAck: {AutoAck}, prefetch: {PrefetchCount})", 
            _consumerId, _queueName, autoAck, prefetchCount);
    }

    /// <summary>
    /// Processes a single message
    /// </summary>
    private async Task ProcessMessageAsync(BasicDeliverEventArgs eventArgs, bool autoAck)
    {
        try
        {
            _stats.LastMessageTime = DateTime.UtcNow;
            
            // Try to deserialize as WorkItem first, fallback to text
            string messageContent;
            WorkItem? workItem = null;
            
            try
            {
                workItem = MessageHelpers.DeserializeMessage<WorkItem>(eventArgs.Body.ToArray());
                messageContent = $"WorkItem: {workItem.Task}";
                
                // Simulate processing time
                if (workItem.ProcessingTimeMs > 0)
                {
                    await Task.Delay(workItem.ProcessingTimeMs);
                }
                
                workItem.WorkerId = _consumerId;
                workItem.ProcessedAt = DateTime.UtcNow;
            }
            catch
            {
                // Fallback to text message
                messageContent = MessageHelpers.GetTextFromMessage(eventArgs.Body.ToArray());
                await Task.Delay(100); // Small processing delay for text messages
            }

            _stats.MessagesProcessed++;
            var messageId = workItem?.Id ?? $"text-{_stats.MessagesProcessed}";
            _stats.ProcessedMessageIds.Add(messageId);

            _logger.LogDebug("Consumer {ConsumerId} processed message {MessageId}: {Content}", 
                _consumerId, messageId, messageContent);

            // Send acknowledgment if not auto-ack
            if (!autoAck)
            {
                await _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
            }

            // Raise event for test verification
            OnMessageProcessed?.Invoke(messageContent, workItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer {ConsumerId} error processing message", _consumerId);
            
            // Reject message and requeue on error (if not auto-ack)
            if (!autoAck)
            {
                await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true);
            }
        }
    }

    /// <summary>
    /// Stops consuming messages
    /// </summary>
    public async Task StopConsumingAsync()
    {
        if (_consumerTag != null)
        {
            await _channel.BasicCancelAsync(_consumerTag);
            _consumerTag = null;
            _logger.LogInformation("Consumer {ConsumerId} stopped consuming from queue {QueueName}", _consumerId, _queueName);
        }
    }

    /// <summary>
    /// Consumes a specific number of messages and then stops
    /// </summary>
    public async Task<List<string>> ConsumeMessagesAsync(int messageCount, bool autoAck = false, TimeSpan? timeout = null)
    {
        var messages = new List<string>();
        var completionSource = new TaskCompletionSource<bool>();
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);

        // Setup temporary event handler
        Action<string, WorkItem?> tempHandler = (content, workItem) =>
        {
            messages.Add(content);
            if (messages.Count >= messageCount)
            {
                completionSource.TrySetResult(true);
            }
        };

        OnMessageProcessed += tempHandler;

        try
        {
            await StartConsumingAsync(autoAck);
            
            // Wait for messages or timeout
            using var cts = new CancellationTokenSource(actualTimeout);
            cts.Token.Register(() => completionSource.TrySetCanceled());
            
            await completionSource.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Consumer {ConsumerId} timed out waiting for {MessageCount} messages. Received {ActualCount}",
                _consumerId, messageCount, messages.Count);
        }
        finally
        {
            OnMessageProcessed -= tempHandler;
            await StopConsumingAsync();
        }

        return messages;
    }

    /// <summary>
    /// Gets consumer statistics
    /// </summary>
    public ConsumerStats GetStats() => _stats;

    /// <summary>
    /// Event raised when a message is processed
    /// </summary>
    public event Action<string, WorkItem?>? OnMessageProcessed;

    /// <summary>
    /// Gets the consumer ID
    /// </summary>
    public string ConsumerId => _consumerId;

    /// <summary>
    /// Gets the queue name being consumed
    /// </summary>
    public string QueueName => _queueName;

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopConsumingAsync().Wait(TimeSpan.FromSeconds(5));
                _logger.LogInformation("Work queue consumer {ConsumerId} disposed. Processed {MessageCount} messages", 
                    _consumerId, _stats.MessagesProcessed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing consumer {ConsumerId}", _consumerId);
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}