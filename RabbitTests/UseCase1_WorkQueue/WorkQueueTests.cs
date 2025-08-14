using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;

namespace RabbitTests.UseCase1_WorkQueue;

/// <summary>
/// Test class for Use Case 1: Simple Producer-Consumer Pattern (Work Queue)
/// Demonstrates basic RabbitMQ work queue patterns with different scenarios
/// </summary>
[TestFixture]
public class WorkQueueTests : TestBase
{
    private const string TestQueueName = "test-work-queue";
    private const string DurableQueueName = "test-work-queue-durable";

    [Test]
    public async Task SingleProducerSingleConsumer_Should_ProcessAllMessages()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new WorkQueueProducer(Channel, loggerFactory.CreateLogger<WorkQueueProducer>(), TestQueueName);
        var consumer = new WorkQueueConsumer(Channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), TestQueueName);

        await producer.InitializeAsync(durable: false);

        const int messageCount = 10;
        var testResult = new TestResult { StartTime = DateTime.UtcNow };

        // Act
        Logger.LogInformation("Test: Single Producer Single Consumer - Publishing {MessageCount} messages", messageCount);
        
        // Publish messages
        await producer.PublishTestMessagesAsync(messageCount);
        
        // Consume messages
        var processedMessages = await consumer.ConsumeMessagesAsync(messageCount, autoAck: true, TimeSpan.FromSeconds(30));
        testResult.ProcessedMessages = processedMessages;
        testResult.EndTime = DateTime.UtcNow;

        // Assert
        Assert.That(processedMessages.Count, Is.EqualTo(messageCount), 
            $"Expected {messageCount} messages, but received {processedMessages.Count}");
        
        Logger.LogInformation("Test completed successfully. Processed {Count} messages in {Duration}ms", 
            processedMessages.Count, testResult.Duration.TotalMilliseconds);

        // Verify each message was received
        for (int i = 1; i <= messageCount; i++)
        {
            Assert.That(processedMessages.Any(m => m.Contains($"Test message {i}")), Is.True,
                $"Message {i} was not found in processed messages");
        }
    }

    [Test]
    public async Task SingleProducerMultipleConsumers_Should_DistributeMessagesRoundRobin()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new WorkQueueProducer(Channel, loggerFactory.CreateLogger<WorkQueueProducer>(), TestQueueName);
        
        const int consumerCount = 3;
        const int messageCount = 20;
        var consumers = new List<WorkQueueConsumer>();
        var consumerTasks = new List<Task<List<string>>>();

        // Create multiple consumers
        for (int i = 0; i < consumerCount; i++)
        {
            var consumer = new WorkQueueConsumer(Channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), 
                TestQueueName, $"consumer-{i + 1}");
            consumers.Add(consumer);
        }

        await producer.InitializeAsync(durable: false);

        // Act
        Logger.LogInformation("Test: Single Producer Multiple Consumers - Publishing {MessageCount} messages to {ConsumerCount} consumers", 
            messageCount, consumerCount);

        // Start all consumers
        foreach (var consumer in consumers)
        {
            var task = consumer.ConsumeMessagesAsync(messageCount / consumerCount + 2, autoAck: true, TimeSpan.FromSeconds(30));
            consumerTasks.Add(task);
        }

        // Small delay to ensure consumers are ready
        await Task.Delay(500);

        // Publish messages
        await producer.PublishTestWorkItemsAsync(messageCount, persistent: false, processingTimeMs: 100);

        // Wait for all consumers to finish or timeout
        var allMessages = new List<string>();
        var completedTasks = await Task.WhenAll(consumerTasks);
        
        foreach (var messages in completedTasks)
        {
            allMessages.AddRange(messages);
        }

        // Assert
        Assert.That(allMessages.Count, Is.EqualTo(messageCount), 
            $"Expected {messageCount} total messages, but processed {allMessages.Count}");

        // Verify round-robin distribution (each consumer should get approximately equal messages)
        var stats = consumers.Select(c => c.GetStats()).ToList();
        var minMessages = stats.Min(s => s.MessagesProcessed);
        var maxMessages = stats.Max(s => s.MessagesProcessed);
        var difference = maxMessages - minMessages;

        Assert.That(difference, Is.LessThanOrEqualTo(2), 
            "Message distribution should be roughly equal among consumers (difference <= 2)");

        Logger.LogInformation("Message distribution: {Distribution}", 
            string.Join(", ", stats.Select(s => $"{s.ConsumerId}: {s.MessagesProcessed}")));

        // Cleanup
        foreach (var consumer in consumers)
        {
            consumer.Dispose();
        }
    }

    [Test]
    public async Task MessageAcknowledgment_ManualAck_Should_HandleAcknowledgmentsCorrectly()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new WorkQueueProducer(Channel, loggerFactory.CreateLogger<WorkQueueProducer>(), TestQueueName);
        var consumer = new WorkQueueConsumer(Channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), TestQueueName);

        await producer.InitializeAsync(durable: false);

        const int messageCount = 5;

        // Act
        Logger.LogInformation("Test: Manual Acknowledgment - Publishing {MessageCount} messages", messageCount);

        // Publish messages
        await producer.PublishTestMessagesAsync(messageCount);

        // Consume with manual acknowledgment
        var processedMessages = await consumer.ConsumeMessagesAsync(messageCount, autoAck: false, TimeSpan.FromSeconds(30));

        // Assert
        Assert.That(processedMessages.Count, Is.EqualTo(messageCount),
            $"Expected {messageCount} messages with manual ack, but received {processedMessages.Count}");

        Logger.LogInformation("Manual acknowledgment test completed successfully");
    }

    [Test]
    public async Task MessageAcknowledgment_AutoAck_Should_ProcessMessagesAutomatically()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new WorkQueueProducer(Channel, loggerFactory.CreateLogger<WorkQueueProducer>(), TestQueueName);
        var consumer = new WorkQueueConsumer(Channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), TestQueueName);

        await producer.InitializeAsync(durable: false);

        const int messageCount = 5;

        // Act
        Logger.LogInformation("Test: Auto Acknowledgment - Publishing {MessageCount} messages", messageCount);

        // Publish messages
        await producer.PublishTestMessagesAsync(messageCount);

        // Consume with auto acknowledgment
        var processedMessages = await consumer.ConsumeMessagesAsync(messageCount, autoAck: true, TimeSpan.FromSeconds(30));

        // Assert
        Assert.That(processedMessages.Count, Is.EqualTo(messageCount),
            $"Expected {messageCount} messages with auto ack, but received {processedMessages.Count}");

        Logger.LogInformation("Auto acknowledgment test completed successfully");
    }

    [Test]
    public async Task MessagePersistence_DurableQueue_Should_PersistMessages()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new WorkQueueProducer(Channel, loggerFactory.CreateLogger<WorkQueueProducer>(), DurableQueueName);
        var consumer = new WorkQueueConsumer(Channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), DurableQueueName);

        // Initialize durable queue with persistent messages
        await producer.InitializeAsync(durable: true);

        const int messageCount = 8;
        var workItems = new List<WorkItem>();
        
        for (int i = 1; i <= messageCount; i++)
        {
            workItems.Add(MessageHelpers.CreateWorkItem($"Persistent Task {i}", 200));
        }

        // Act
        Logger.LogInformation("Test: Message Persistence - Publishing {MessageCount} persistent messages to durable queue", messageCount);

        // Publish persistent messages to durable queue
        await producer.PublishWorkItemsAsync(workItems, persistent: true);

        // Consume messages
        var processedMessages = await consumer.ConsumeMessagesAsync(messageCount, autoAck: false, TimeSpan.FromSeconds(30));

        // Assert
        Assert.That(processedMessages.Count, Is.EqualTo(messageCount),
            $"Expected {messageCount} persistent messages, but received {processedMessages.Count}");

        // Verify messages contain our test data
        foreach (var workItem in workItems)
        {
            Assert.That(processedMessages.Any(m => m.Contains(workItem.Task)), Is.True,
                $"Persistent message '{workItem.Task}' was not found in processed messages");
        }

        Logger.LogInformation("Message persistence test completed successfully");

        // Cleanup durable queue
        await Channel.QueueDeleteAsync(DurableQueueName, false, false, false);
    }

    [Test]
    public async Task WorkItem_Processing_Should_SimulateRealWorkload()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new WorkQueueProducer(Channel, loggerFactory.CreateLogger<WorkQueueProducer>(), TestQueueName);
        var consumer = new WorkQueueConsumer(Channel, loggerFactory.CreateLogger<WorkQueueConsumer>(), TestQueueName);

        await producer.InitializeAsync(durable: false);

        const int messageCount = 6;
        const int processingTimeMs = 500; // Simulate some processing time
        var workItems = new List<WorkItem>();

        for (int i = 1; i <= messageCount; i++)
        {
            workItems.Add(MessageHelpers.CreateWorkItem($"Complex Task {i}", processingTimeMs));
        }

        var startTime = DateTime.UtcNow;

        // Act
        Logger.LogInformation("Test: WorkItem Processing - Publishing {MessageCount} work items with {ProcessingTime}ms processing time", 
            messageCount, processingTimeMs);

        // Publish work items
        await producer.PublishWorkItemsAsync(workItems, persistent: false);

        // Process work items
        var processedMessages = await consumer.ConsumeMessagesAsync(messageCount, autoAck: false, TimeSpan.FromSeconds(60));

        var endTime = DateTime.UtcNow;
        var totalTime = endTime - startTime;

        // Assert
        Assert.That(processedMessages.Count, Is.EqualTo(messageCount),
            $"Expected {messageCount} work items, but processed {processedMessages.Count}");

        // Verify processing took appropriate time (should be at least processingTime * messageCount)
        var expectedMinTime = TimeSpan.FromMilliseconds(processingTimeMs * messageCount * 0.8); // 80% tolerance
        Assert.That(totalTime, Is.GreaterThan(expectedMinTime),
            $"Processing time {totalTime.TotalMilliseconds}ms seems too fast for {messageCount} items with {processingTimeMs}ms each");

        var stats = consumer.GetStats();
        Assert.That(stats.MessagesProcessed, Is.EqualTo(messageCount));
        Assert.That(stats.ProcessedMessageIds.Count, Is.EqualTo(messageCount));

        Logger.LogInformation("WorkItem processing test completed. Total time: {TotalTime}ms, Stats: {Stats}", 
            totalTime.TotalMilliseconds, 
            $"Processed: {stats.MessagesProcessed}, Consumer: {stats.ConsumerId}");
    }

    [TearDown]
    public override async Task TearDown()
    {
        // Clean up test queues
        await SafeDeleteQueue(TestQueueName);
        await SafeDeleteQueue(DurableQueueName);
        await base.TearDown();
    }
}