using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;
using RabbitTests.UseCase1_WorkQueue;

namespace RabbitTests.UseCase1_WorkQueue;

/// <summary>
/// Demonstration program for Use Case 1: Work Queue Pattern
/// This class shows how to use the WorkQueueProducer and WorkQueueConsumer
/// </summary>
public class WorkQueueDemo
{
    private readonly ILogger<WorkQueueDemo> _logger;
    private readonly RabbitMQConnection _connection;

    public WorkQueueDemo(ILogger<WorkQueueDemo> logger, RabbitMQConnection connection)
    {
        _logger = logger;
        _connection = connection;
    }

    /// <summary>
    /// Demonstrates basic work queue usage
    /// </summary>
    public async Task RunBasicDemoAsync()
    {
        _logger.LogInformation("Starting Work Queue Demo...");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        const string queueName = "demo-work-queue";
        const int messageCount = 5;

        // Create producer and consumer
        var producer = new WorkQueueProducer(channel, loggerFactory.CreateLogger<WorkQueueProducer>(), queueName);
        var consumer = new WorkQueueConsumer(channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), queueName, "demo-consumer");

        try
        {
            // Initialize queue
            await producer.InitializeAsync(durable: false);

            // Set up message processing event
            consumer.OnMessageProcessed += (message, workItem) =>
            {
                _logger.LogInformation("? Processed: {Message}", message);
            };

            _logger.LogInformation("?? Publishing {Count} work items...", messageCount);
            
            // Publish some work items
            await producer.PublishTestWorkItemsAsync(messageCount, persistent: false, processingTimeMs: 1000);
            
            _logger.LogInformation("?? Starting consumer...");
            
            // Start consuming
            await consumer.StartConsumingAsync(autoAck: false, prefetchCount: 1);
            
            // Wait for processing to complete
            await Task.Delay(TimeSpan.FromSeconds(messageCount * 1.2)); // Give some extra time
            
            // Stop consuming
            await consumer.StopConsumingAsync();
            
            // Display statistics
            var stats = consumer.GetStats();
            _logger.LogInformation("?? Demo completed! Consumer {ConsumerId} processed {MessageCount} messages", 
                stats.ConsumerId, stats.MessagesProcessed);
        }
        finally
        {
            producer.Dispose();
            consumer.Dispose();
            
            // Clean up
            await channel.QueueDeleteAsync(queueName, false, false, false);
        }
    }

    /// <summary>
    /// Demonstrates multiple consumers working together
    /// </summary>
    public async Task RunMultipleConsumersDemoAsync()
    {
        _logger.LogInformation("Starting Multiple Consumers Demo...");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        const string queueName = "demo-multi-consumer-queue";
        const int messageCount = 12;
        const int consumerCount = 3;

        var producer = new WorkQueueProducer(channel, loggerFactory.CreateLogger<WorkQueueProducer>(), queueName);
        var consumers = new List<WorkQueueConsumer>();

        try
        {
            // Initialize queue
            await producer.InitializeAsync(durable: false);

            // Create multiple consumers
            for (int i = 1; i <= consumerCount; i++)
            {
                var consumer = new WorkQueueConsumer(channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), 
                    queueName, $"worker-{i}");
                
                consumer.OnMessageProcessed += (message, workItem) =>
                {
                    _logger.LogInformation("? {ConsumerId} processed: {Message}", consumer.ConsumerId, message);
                };
                
                consumers.Add(consumer);
            }

            _logger.LogInformation("?? Publishing {Count} work items to {ConsumerCount} consumers...", messageCount, consumerCount);
            
            // Start all consumers
            foreach (var consumer in consumers)
            {
                await consumer.StartConsumingAsync(autoAck: false, prefetchCount: 1);
            }
            
            // Publish work items
            await producer.PublishTestWorkItemsAsync(messageCount, persistent: false, processingTimeMs: 800);
            
            _logger.LogInformation("?? Workers are processing...");
            
            // Wait for processing
            await Task.Delay(TimeSpan.FromSeconds(messageCount * 0.9)); // Should be faster with multiple workers
            
            // Stop all consumers
            foreach (var consumer in consumers)
            {
                await consumer.StopConsumingAsync();
            }
            
            // Display statistics
            _logger.LogInformation("?? Multi-consumer demo completed!");
            var totalProcessed = 0;
            foreach (var consumer in consumers)
            {
                var stats = consumer.GetStats();
                totalProcessed += stats.MessagesProcessed;
                _logger.LogInformation("   {ConsumerId}: {MessageCount} messages", stats.ConsumerId, stats.MessagesProcessed);
            }
            _logger.LogInformation("   Total processed: {TotalProcessed}/{TotalSent}", totalProcessed, messageCount);
        }
        finally
        {
            producer.Dispose();
            foreach (var consumer in consumers)
            {
                consumer.Dispose();
            }
            
            // Clean up
            await channel.QueueDeleteAsync(queueName, false, false, false);
        }
    }

    /// <summary>
    /// Demonstrates persistent messages and durable queues
    /// </summary>
    public async Task RunPersistentMessagesDemoAsync()
    {
        _logger.LogInformation("Starting Persistent Messages Demo...");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        const string queueName = "demo-persistent-queue";
        const int messageCount = 5;

        var producer = new WorkQueueProducer(channel, loggerFactory.CreateLogger<WorkQueueProducer>(), queueName);
        var consumer = new WorkQueueConsumer(channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), queueName, "persistent-consumer");

        try
        {
            // Initialize DURABLE queue
            await producer.InitializeAsync(durable: true);

            _logger.LogInformation("?? Publishing {Count} PERSISTENT work items to DURABLE queue...", messageCount);
            
            // Create some important work items
            var importantTasks = new[]
            {
                "Process critical order #12345",
                "Generate monthly financial report",
                "Backup customer database",
                "Send compliance notifications",
                "Update inventory levels"
            };

            var workItems = importantTasks.Select((task, i) => 
                MessageHelpers.CreateWorkItem(task, 1200)).ToList();

            // Publish PERSISTENT messages
            await producer.PublishWorkItemsAsync(workItems, persistent: true);
            
            _logger.LogInformation("?? Messages are now persistent and will survive server restarts!");
            _logger.LogInformation("?? Starting consumer to process persistent messages...");
            
            consumer.OnMessageProcessed += (message, workItem) =>
            {
                _logger.LogInformation("? Processed persistent task: {Message}", message);
            };
            
            // Process the persistent messages
            await consumer.StartConsumingAsync(autoAck: false, prefetchCount: 1);
            await Task.Delay(TimeSpan.FromSeconds(messageCount * 1.5));
            await consumer.StopConsumingAsync();
            
            var stats = consumer.GetStats();
            _logger.LogInformation("?? Persistent messages demo completed! Processed {MessageCount} critical tasks", stats.MessagesProcessed);
        }
        finally
        {
            producer.Dispose();
            consumer.Dispose();
            
            // Clean up durable queue
            await channel.QueueDeleteAsync(queueName, false, false, false);
        }
    }
}