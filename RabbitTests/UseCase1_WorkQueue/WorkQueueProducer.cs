using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitTests.Infrastructure;
using System.Text;

namespace RabbitTests.UseCase1_WorkQueue;

/// <summary>
/// Producer for Work Queue pattern - sends tasks to a queue for worker consumption
/// </summary>
public class WorkQueueProducer : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<WorkQueueProducer> _logger;
    private readonly string _queueName;
    private bool _disposed = false;

    public WorkQueueProducer(IChannel channel, ILogger<WorkQueueProducer> logger, string queueName = "work-queue")
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueName = queueName;
    }

    /// <summary>
    /// Initializes the work queue with specified durability settings
    /// </summary>
    public async Task InitializeAsync(bool durable = false)
    {
        await _channel.QueueDeclareAsync(
            queue: _queueName,
            durable: durable,
            exclusive: false,
            autoDelete: !durable, // Auto-delete if not durable
            arguments: null);

        _logger.LogInformation("Work queue '{QueueName}' initialized (durable: {Durable})", _queueName, durable);
    }

    /// <summary>
    /// Publishes a single work item to the queue
    /// </summary>
    public async Task PublishWorkItemAsync(WorkItem workItem, bool persistent = false)
    {
        var messageBody = MessageHelpers.SerializeMessage(workItem);
        
        var properties = new BasicProperties();
        if (persistent)
        {
            properties.Persistent = true;
        }

        await _channel.BasicPublishAsync(
            exchange: string.Empty, // Use default exchange
            routingKey: _queueName,
            mandatory: false,
            basicProperties: properties,
            body: messageBody);

        _logger.LogDebug("Published work item {WorkItemId} to queue {QueueName}", workItem.Id, _queueName);
    }

    /// <summary>
    /// Publishes a simple text message to the queue
    /// </summary>
    public async Task PublishTextMessageAsync(string message, bool persistent = false)
    {
        var messageBody = MessageHelpers.CreateTextMessage(message);
        
        var properties = new BasicProperties();
        if (persistent)
        {
            properties.Persistent = true;
        }

        await _channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: _queueName,
            mandatory: false,
            basicProperties: properties,
            body: messageBody);

        _logger.LogDebug("Published text message to queue {QueueName}: {Message}", _queueName, message);
    }

    /// <summary>
    /// Publishes multiple work items in batch
    /// </summary>
    public async Task PublishWorkItemsAsync(IEnumerable<WorkItem> workItems, bool persistent = false)
    {
        var itemsList = workItems.ToList();
        _logger.LogInformation("Publishing {Count} work items to queue {QueueName}", itemsList.Count, _queueName);

        foreach (var workItem in itemsList)
        {
            await PublishWorkItemAsync(workItem, persistent);
        }

        _logger.LogInformation("Successfully published {Count} work items", itemsList.Count);
    }

    /// <summary>
    /// Publishes multiple text messages in batch
    /// </summary>
    public async Task PublishTextMessagesAsync(IEnumerable<string> messages, bool persistent = false)
    {
        var messagesList = messages.ToList();
        _logger.LogInformation("Publishing {Count} text messages to queue {QueueName}", messagesList.Count, _queueName);

        foreach (var message in messagesList)
        {
            await PublishTextMessageAsync(message, persistent);
        }

        _logger.LogInformation("Successfully published {Count} text messages", messagesList.Count);
    }

    /// <summary>
    /// Generates and publishes test work items
    /// </summary>
    public async Task PublishTestWorkItemsAsync(int count, bool persistent = false, int processingTimeMs = 1000)
    {
        var workItems = new List<WorkItem>();
        
        for (int i = 1; i <= count; i++)
        {
            workItems.Add(MessageHelpers.CreateWorkItem($"Task {i}", processingTimeMs));
        }

        await PublishWorkItemsAsync(workItems, persistent);
    }

    /// <summary>
    /// Generates and publishes test text messages
    /// </summary>
    public async Task PublishTestMessagesAsync(int count, bool persistent = false)
    {
        var messages = Enumerable.Range(1, count)
            .Select(i => $"Test message {i} - {DateTime.UtcNow:HH:mm:ss.fff}")
            .ToList();

        await PublishTextMessagesAsync(messages, persistent);
    }

    /// <summary>
    /// Gets the queue name being used by this producer
    /// </summary>
    public string QueueName => _queueName;

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Work queue producer for '{QueueName}' disposed", _queueName);
            _disposed = true;
        }
    }
}