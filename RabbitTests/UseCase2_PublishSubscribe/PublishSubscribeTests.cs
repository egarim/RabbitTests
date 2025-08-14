using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;

namespace RabbitTests.UseCase2_PublishSubscribe;

/// <summary>
/// Test class for Use Case 2: Publish-Subscribe Pattern (Fanout Exchange)
/// Demonstrates fanout exchange broadcasting patterns with different scenarios
/// </summary>
[TestFixture]
public class PublishSubscribeTests : TestBase
{
    private const string TestExchangeName = "test-fanout-exchange";

    [Test]
    public async Task BasicFanout_OnePublisherThreeSubscribers_Should_DeliverMessagesToAllSubscribers()
    {
        // Arrange
        var exchangeName = $"test-basic-fanout-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), exchangeName);
        
        const int subscriberCount = 3;
        const int messageCount = 10;
        var subscribers = new List<SubscriberService>();

        // Create multiple subscribers
        for (int i = 1; i <= subscriberCount; i++)
        {
            var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
                exchangeName, $"subscriber-{i}");
            subscribers.Add(subscriber);
        }

        // Initialize publisher and subscribers
        await publisher.InitializeAsync(durable: false);
        
        foreach (var subscriber in subscribers)
        {
            await subscriber.InitializeAsync(useTemporaryQueue: true, exclusive: true);
        }

        // Act
        Logger.LogInformation("Test: Basic Fanout - Publishing {MessageCount} messages to {SubscriberCount} subscribers", 
            messageCount, subscriberCount);

        // Start all subscribers consuming
        var consumerTasks = subscribers.Select(s => 
            s.ConsumeMessagesAsync(messageCount, autoAck: true, TimeSpan.FromSeconds(30))).ToList();

        // Small delay to ensure subscribers are ready
        await Task.Delay(500);

        // Publish messages
        await publisher.PublishTestBroadcastMessagesAsync(messageCount);

        // Wait for all subscribers to receive messages
        var allReceivedMessages = await Task.WhenAll(consumerTasks);

        // Assert
        for (int i = 0; i < subscriberCount; i++)
        {
            var receivedMessages = allReceivedMessages[i];
            Assert.That(receivedMessages.Count, Is.EqualTo(messageCount), 
                $"Subscriber {i + 1} should have received {messageCount} messages, but received {receivedMessages.Count}");
        }

        // Verify each subscriber received the same messages (fanout behavior)
        var firstSubscriberMessages = allReceivedMessages[0].OrderBy(m => m).ToList();
        for (int i = 1; i < subscriberCount; i++)
        {
            var otherSubscriberMessages = allReceivedMessages[i].OrderBy(m => m).ToList();
            Assert.That(otherSubscriberMessages.Count, Is.EqualTo(firstSubscriberMessages.Count),
                $"Subscriber {i + 1} received different number of messages than subscriber 1");
            
            // Note: In fanout, all subscribers should receive the same set of messages
            for (int j = 0; j < messageCount; j++)
            {
                Assert.That(otherSubscriberMessages[j].Contains($"Broadcast message {j + 1}"), Is.True,
                    $"Subscriber {i + 1} missing message {j + 1}");
            }
        }

        Logger.LogInformation("Basic fanout test completed successfully. All {SubscriberCount} subscribers received all {MessageCount} messages", 
            subscriberCount, messageCount);

        // Cleanup
        foreach (var subscriber in subscribers)
        {
            subscriber.Dispose();
        }
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task DynamicSubscribers_AddRemoveSubscribers_Should_HandleSubscriptionChanges()
    {
        // Arrange
        var exchangeName = $"test-dynamic-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), exchangeName);
        
        await publisher.InitializeAsync(durable: false);

        const int messageCount = 15;
        var allReceivedMessages = new List<(string subscriberId, List<string> messages)>();

        // Act
        Logger.LogInformation("Test: Dynamic Subscribers - Adding/removing subscribers during publishing");

        // Phase 1: Start with 2 subscribers
        var subscriber1 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "dynamic-sub-1");
        var subscriber2 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "dynamic-sub-2");

        await subscriber1.InitializeAsync();
        await subscriber2.InitializeAsync();
        await subscriber1.StartConsumingAsync(autoAck: true);
        await subscriber2.StartConsumingAsync(autoAck: true);

        // Publish first batch
        await publisher.PublishTestBroadcastMessagesAsync(5);
        await Task.Delay(1000); // Allow messages to be processed

        // Phase 2: Add third subscriber
        var subscriber3 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "dynamic-sub-3");
        await subscriber3.InitializeAsync();
        await subscriber3.StartConsumingAsync(autoAck: true);

        // Publish second batch
        await publisher.PublishTestBroadcastMessagesAsync(5);
        await Task.Delay(1000);

        // Phase 3: Remove first subscriber, continue with 2 and 3
        await subscriber1.StopConsumingAsync();

        // Publish third batch
        await publisher.PublishTestBroadcastMessagesAsync(5);
        await Task.Delay(1000);

        // Stop remaining subscribers
        await subscriber2.StopConsumingAsync();
        await subscriber3.StopConsumingAsync();

        // Collect results
        var sub1Messages = subscriber1.GetReceivedMessages();
        var sub2Messages = subscriber2.GetReceivedMessages();
        var sub3Messages = subscriber3.GetReceivedMessages();

        // Assert
        // Subscriber 1 should have received first 2 batches (10 messages)
        Assert.That(sub1Messages.Count, Is.EqualTo(10), 
            "Subscriber 1 should have received 10 messages (first 2 batches)");

        // Subscriber 2 should have received all 3 batches (15 messages)
        Assert.That(sub2Messages.Count, Is.EqualTo(15), 
            "Subscriber 2 should have received 15 messages (all 3 batches)");

        // Subscriber 3 should have received last 2 batches (10 messages)
        Assert.That(sub3Messages.Count, Is.EqualTo(10), 
            "Subscriber 3 should have received 10 messages (last 2 batches)");

        Logger.LogInformation("Dynamic subscribers test completed. Sub1: {Sub1Count}, Sub2: {Sub2Count}, Sub3: {Sub3Count} messages", 
            sub1Messages.Count, sub2Messages.Count, sub3Messages.Count);

        // Cleanup
        subscriber1.Dispose();
        subscriber2.Dispose();
        subscriber3.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task TemporaryQueues_ExclusiveAutoDelete_Should_HandleTemporarySubscribers()
    {
        // Arrange
        var exchangeName = $"test-temp-queues-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), exchangeName);
        
        await publisher.InitializeAsync(durable: false);

        const int messageCount = 8;
        var subscriber1 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "temp-subscriber-1");
        var subscriber2 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "temp-subscriber-2");

        // Act
        Logger.LogInformation("Test: Temporary Queues - Using exclusive auto-delete queues");

        // Initialize with temporary queues (exclusive, auto-delete)
        await subscriber1.InitializeAsync(useTemporaryQueue: true, exclusive: true);
        await subscriber2.InitializeAsync(useTemporaryQueue: true, exclusive: true);

        // Verify queue names are auto-generated
        Assert.That(string.IsNullOrEmpty(subscriber1.QueueName), Is.False, "Subscriber 1 should have a queue name");
        Assert.That(string.IsNullOrEmpty(subscriber2.QueueName), Is.False, "Subscriber 2 should have a queue name");
        Assert.That(subscriber1.QueueName, Is.Not.EqualTo(subscriber2.QueueName), 
            "Subscribers should have different auto-generated queue names");

        Logger.LogInformation("Temporary queues created: '{Queue1}' and '{Queue2}'", 
            subscriber1.QueueName, subscriber2.QueueName);

        // Start consuming and publish messages
        var task1 = subscriber1.ConsumeMessagesAsync(messageCount, autoAck: true, TimeSpan.FromSeconds(30));
        var task2 = subscriber2.ConsumeMessagesAsync(messageCount, autoAck: true, TimeSpan.FromSeconds(30));

        await Task.Delay(500); // Ensure subscribers are ready

        await publisher.PublishTestBroadcastMessagesAsync(messageCount);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.That(results[0].Count, Is.EqualTo(messageCount), 
            $"Subscriber 1 should have received {messageCount} messages");
        Assert.That(results[1].Count, Is.EqualTo(messageCount), 
            $"Subscriber 2 should have received {messageCount} messages");

        Logger.LogInformation("Temporary queues test completed successfully");

        // Cleanup - queues should auto-delete when subscribers disconnect
        subscriber1.Dispose();
        subscriber2.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task BroadcastNotifications_SystemWideNotifications_Should_DeliverToAllSubscribers()
    {
        // Arrange
        var exchangeName = $"test-notifications-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), exchangeName);
        
        const int subscriberCount = 4;
        const int notificationCount = 12;
        var subscribers = new List<SubscriberService>();

        // Create subscribers representing different system components
        var componentNames = new[] { "UserService", "OrderService", "PaymentService", "NotificationService" };
        for (int i = 0; i < subscriberCount; i++)
        {
            var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
                exchangeName, componentNames[i]);
            subscribers.Add(subscriber);
        }

        // Initialize publisher and subscribers
        await publisher.InitializeAsync(durable: false);
        
        foreach (var subscriber in subscribers)
        {
            await subscriber.InitializeAsync(useTemporaryQueue: true, exclusive: true);
        }

        // Act
        Logger.LogInformation("Test: Broadcast Notifications - Simulating system-wide notifications to {SubscriberCount} components", 
            subscriberCount);

        // Start all subscribers consuming notifications
        var consumerTasks = subscribers.Select(s => 
            s.ConsumeNotificationsAsync(notificationCount, autoAck: true, TimeSpan.FromSeconds(30))).ToList();

        await Task.Delay(500); // Ensure subscribers are ready

        // Publish system-wide notifications
        await publisher.PublishTestNotificationsAsync(notificationCount);

        // Wait for all subscribers to receive notifications
        var allReceivedNotifications = await Task.WhenAll(consumerTasks);

        // Assert
        for (int i = 0; i < subscriberCount; i++)
        {
            var receivedNotifications = allReceivedNotifications[i];
            Assert.That(receivedNotifications.Count, Is.EqualTo(notificationCount), 
                $"Component {componentNames[i]} should have received {notificationCount} notifications, but received {receivedNotifications.Count}");

            // Verify notification types
            var infoCount = receivedNotifications.Count(n => n.Type == "INFO");
            var warningCount = receivedNotifications.Count(n => n.Type == "WARNING");
            var errorCount = receivedNotifications.Count(n => n.Type == "ERROR");
            var successCount = receivedNotifications.Count(n => n.Type == "SUCCESS");

            Assert.That(infoCount + warningCount + errorCount + successCount, Is.EqualTo(notificationCount),
                $"Component {componentNames[i]} should have received notifications of all types");

            Logger.LogInformation("Component {ComponentName} received: {Info} INFO, {Warning} WARNING, {Error} ERROR, {Success} SUCCESS", 
                componentNames[i], infoCount, warningCount, errorCount, successCount);
        }

        // Verify all components received the same notifications (fanout behavior)
        var firstComponentNotifications = allReceivedNotifications[0]
            .OrderBy(n => n.Id).Select(n => n.Id).ToList();
        
        for (int i = 1; i < subscriberCount; i++)
        {
            var otherComponentNotifications = allReceivedNotifications[i]
                .OrderBy(n => n.Id).Select(n => n.Id).ToList();
            
            Assert.That(otherComponentNotifications, Is.EqualTo(firstComponentNotifications),
                $"Component {componentNames[i]} should have received the same notifications as {componentNames[0]}");
        }

        Logger.LogInformation("Broadcast notifications test completed successfully. All {ComponentCount} components received all {NotificationCount} notifications", 
            subscriberCount, notificationCount);

        // Cleanup
        foreach (var subscriber in subscribers)
        {
            subscriber.Dispose();
        }
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task MessagePersistence_DurableExchange_Should_HandlePersistentMessages()
    {
        // Arrange
        var exchangeName = $"test-durable-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), exchangeName);
        
        const int messageCount = 6;
        var subscriber1 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "persistent-subscriber-1");
        var subscriber2 = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "persistent-subscriber-2");

        // Act
        Logger.LogInformation("Test: Message Persistence - Using durable exchange with persistent messages");

        // Initialize with durable exchange
        await publisher.InitializeAsync(durable: true);
        await subscriber1.InitializeAsync(useTemporaryQueue: false, exclusive: false);
        await subscriber2.InitializeAsync(useTemporaryQueue: false, exclusive: false);

        // Start consuming and publish persistent messages
        var task1 = subscriber1.ConsumeMessagesAsync(messageCount, autoAck: false, TimeSpan.FromSeconds(30));
        var task2 = subscriber2.ConsumeMessagesAsync(messageCount, autoAck: false, TimeSpan.FromSeconds(30));

        await Task.Delay(500);

        // Publish persistent messages
        await publisher.PublishTestBroadcastMessagesAsync(messageCount, persistent: true);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.That(results[0].Count, Is.EqualTo(messageCount), 
            $"Subscriber 1 should have received {messageCount} persistent messages");
        Assert.That(results[1].Count, Is.EqualTo(messageCount), 
            $"Subscriber 2 should have received {messageCount} persistent messages");

        // Verify messages contain expected content
        foreach (var message in results[0])
        {
            Assert.That(message.Contains("Broadcast message"), Is.True,
                "Persistent message should contain expected content");
        }

        Logger.LogInformation("Message persistence test completed successfully");

        // Cleanup durable resources
        subscriber1.Dispose();
        subscriber2.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task SubscriberStatistics_MessageCounting_Should_TrackMessagesCorrectly()
    {
        // Arrange
        var exchangeName = $"test-stats-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), exchangeName);
        
        const int messageCount = 10;
        var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
            exchangeName, "stats-subscriber");

        await publisher.InitializeAsync(durable: false);
        await subscriber.InitializeAsync();

        var startTime = DateTime.UtcNow;

        // Act
        Logger.LogInformation("Test: Subscriber Statistics - Tracking message counts and timing");

        var consumeTask = subscriber.ConsumeMessagesAsync(messageCount, autoAck: true, TimeSpan.FromSeconds(30));
        await Task.Delay(200);

        await publisher.PublishTestBroadcastMessagesAsync(messageCount);
        
        var receivedMessages = await consumeTask;
        var endTime = DateTime.UtcNow;

        var stats = subscriber.GetStats();

        // Assert
        Assert.That(receivedMessages.Count, Is.EqualTo(messageCount));
        Assert.That(stats.MessagesReceived, Is.EqualTo(messageCount));
        Assert.That(stats.SubscriberId, Is.EqualTo("stats-subscriber"));
        Assert.That(stats.StartTime, Is.GreaterThanOrEqualTo(startTime));
        Assert.That(stats.LastMessageTime, Is.Not.Null);
        Assert.That(stats.LastMessageTime, Is.LessThanOrEqualTo(endTime));
        Assert.That(string.IsNullOrEmpty(stats.QueueName), Is.False);

        Logger.LogInformation("Subscriber statistics: ID={SubscriberId}, Messages={MessageCount}, Queue={QueueName}, Duration={Duration}ms", 
            stats.SubscriberId, stats.MessagesReceived, stats.QueueName, 
            stats.LastMessageTime?.Subtract(stats.StartTime).TotalMilliseconds);

        // Cleanup
        subscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [TearDown]
    public override async Task TearDown()
    {
        // Clean up test exchanges - the specific exchange names are generated per test now
        await base.TearDown();
    }
}