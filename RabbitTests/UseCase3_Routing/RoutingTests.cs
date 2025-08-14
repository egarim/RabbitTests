using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;

namespace RabbitTests.UseCase3_Routing;

/// <summary>
/// Test class for Use Case 3: Routing Pattern (Direct Exchange)
/// Demonstrates direct exchange routing patterns with selective message delivery based on routing keys
/// </summary>
[TestFixture]
public class RoutingTests : TestBase
{
    [Test]
    public async Task BasicRouting_SeverityLevels_Should_RouteMessagesToCorrectConsumers()
    {
        // Arrange
        var exchangeName = $"test-basic-routing-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new RoutingProducer(Channel, loggerFactory.CreateLogger<RoutingProducer>(), exchangeName);
        
        const int messagesPerSeverity = 5;
        
        // Create consumers for different severity levels
        var infoConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "info-consumer");
        var warningConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "warning-consumer");
        var errorConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "error-consumer");
        var allSeverityConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "all-severity-consumer");

        // Initialize producer and consumers
        await producer.InitializeAsync(durable: false);
        await infoConsumer.InitializeForLogRoutingAsync(new[] { "INFO" });
        await warningConsumer.InitializeForLogRoutingAsync(new[] { "WARNING" });
        await errorConsumer.InitializeForLogRoutingAsync(new[] { "ERROR" });
        await allSeverityConsumer.InitializeForLogRoutingAsync(new[] { "INFO", "WARNING", "ERROR" });

        // Act
        Logger.LogInformation("Test: Basic Routing - Testing severity-based message routing");

        // Start all consumers
        var infoTask = infoConsumer.ConsumeLogMessagesAsync(messagesPerSeverity, autoAck: true, TimeSpan.FromSeconds(30));
        var warningTask = warningConsumer.ConsumeLogMessagesAsync(messagesPerSeverity, autoAck: true, TimeSpan.FromSeconds(30));
        var errorTask = errorConsumer.ConsumeLogMessagesAsync(messagesPerSeverity, autoAck: true, TimeSpan.FromSeconds(30));
        var allSeverityTask = allSeverityConsumer.ConsumeLogMessagesAsync(messagesPerSeverity * 3, autoAck: true, TimeSpan.FromSeconds(30));

        await Task.Delay(500); // Ensure consumers are ready

        // Publish test log messages
        await producer.PublishTestLogMessagesAsync(messagesPerSeverity);

        // Wait for all consumers to receive messages
        var results = await Task.WhenAll(infoTask, warningTask, errorTask, allSeverityTask);
        var infoMessages = results[0];
        var warningMessages = results[1];
        var errorMessages = results[2];
        var allMessages = results[3];

        // Assert
        Assert.That(infoMessages.Count, Is.EqualTo(messagesPerSeverity), 
            $"Info consumer should receive {messagesPerSeverity} INFO messages");
        Assert.That(warningMessages.Count, Is.EqualTo(messagesPerSeverity), 
            $"Warning consumer should receive {messagesPerSeverity} WARNING messages");
        Assert.That(errorMessages.Count, Is.EqualTo(messagesPerSeverity), 
            $"Error consumer should receive {messagesPerSeverity} ERROR messages");
        Assert.That(allMessages.Count, Is.EqualTo(messagesPerSeverity * 3), 
            $"All-severity consumer should receive {messagesPerSeverity * 3} messages total");

        // Verify routing specificity
        Assert.That(infoMessages.All(msg => msg.Severity == "INFO"), Is.True,
            "Info consumer should only receive INFO messages");
        Assert.That(warningMessages.All(msg => msg.Severity == "WARNING"), Is.True,
            "Warning consumer should only receive WARNING messages");
        Assert.That(errorMessages.All(msg => msg.Severity == "ERROR"), Is.True,
            "Error consumer should only receive ERROR messages");

        // Verify all-severity consumer gets all message types
        var allSeverityTypes = allMessages.Select(msg => msg.Severity).Distinct().OrderBy(s => s).ToArray();
        Assert.That(allSeverityTypes, Is.EqualTo(new[] { "ERROR", "INFO", "WARNING" }),
            "All-severity consumer should receive messages of all types");

        Logger.LogInformation("Basic routing test completed successfully. Messages routed correctly by severity level");

        // Cleanup
        infoConsumer.Dispose();
        warningConsumer.Dispose();
        errorConsumer.Dispose();
        allSeverityConsumer.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task MultipleBindings_OneQueueMultipleRoutingKeys_Should_ReceiveAllMatchingMessages()
    {
        // Arrange
        var exchangeName = $"test-multiple-bindings-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new RoutingProducer(Channel, loggerFactory.CreateLogger<RoutingProducer>(), exchangeName);
        
        var routingKeys = new[] { "orders.created", "orders.updated", "orders.cancelled" };
        const int messagesPerKey = 4;
        
        // Create consumer that binds to multiple routing keys
        var orderConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "order-consumer");

        // Initialize
        await producer.InitializeAsync(durable: false);
        await orderConsumer.InitializeAsync(routingKeys);

        // Act
        Logger.LogInformation("Test: Multiple Bindings - One queue bound to multiple routing keys");

        var consumerTask = orderConsumer.ConsumeMessagesAsync(routingKeys.Length * messagesPerKey, 
            autoAck: true, TimeSpan.FromSeconds(30));

        await Task.Delay(500);

        // Publish messages for each routing key
        await producer.PublishTestRoutingMessagesAsync(routingKeys, messagesPerKey);

        var receivedMessages = await consumerTask;
        var messagesByRoutingKey = orderConsumer.GetMessagesByRoutingKey();

        // Assert
        Assert.That(receivedMessages.Count, Is.EqualTo(routingKeys.Length * messagesPerKey),
            $"Consumer should receive all {routingKeys.Length * messagesPerKey} messages");

        // Verify each routing key received the correct number of messages
        foreach (var routingKey in routingKeys)
        {
            Assert.That(messagesByRoutingKey.ContainsKey(routingKey), Is.True,
                $"Consumer should have received messages for routing key '{routingKey}'");
            Assert.That(messagesByRoutingKey[routingKey].Count, Is.EqualTo(messagesPerKey),
                $"Consumer should have received {messagesPerKey} messages for routing key '{routingKey}'");
        }

        Logger.LogInformation("Multiple bindings test completed. Consumer received messages from all bound routing keys");

        // Cleanup
        orderConsumer.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task SelectiveConsumption_DifferentConsumers_Should_ProcessSpecificMessageTypes()
    {
        // Arrange
        var exchangeName = $"test-selective-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new RoutingProducer(Channel, loggerFactory.CreateLogger<RoutingProducer>(), exchangeName);
        
        const int messagesPerType = 6;
        
        // Create specialized consumers
        var userConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "user-service");
        var orderConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "order-service");
        var paymentConsumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "payment-service");

        // Initialize with specific routing patterns
        await producer.InitializeAsync(durable: false);
        await userConsumer.InitializeAsync(new[] { "user.created", "user.updated" });
        await orderConsumer.InitializeAsync(new[] { "order.created", "order.shipped" });
        await paymentConsumer.InitializeAsync(new[] { "payment.processed", "payment.failed" });

        // Act
        Logger.LogInformation("Test: Selective Consumption - Different consumers for different message types");

        // Start consumers
        var userTask = userConsumer.ConsumeMessagesAsync(messagesPerType * 2, autoAck: true, TimeSpan.FromSeconds(30));
        var orderTask = orderConsumer.ConsumeMessagesAsync(messagesPerType * 2, autoAck: true, TimeSpan.FromSeconds(30));
        var paymentTask = paymentConsumer.ConsumeMessagesAsync(messagesPerType * 2, autoAck: true, TimeSpan.FromSeconds(30));

        await Task.Delay(500);

        // Publish messages for different service types
        var routingKeys = new[] { "user.created", "user.updated", "order.created", "order.shipped", 
                                 "payment.processed", "payment.failed" };
        await producer.PublishTestRoutingMessagesAsync(routingKeys, messagesPerType);

        var results = await Task.WhenAll(userTask, orderTask, paymentTask);
        var userMessages = results[0];
        var orderMessages = results[1];
        var paymentMessages = results[2];

        // Assert
        Assert.That(userMessages.Count, Is.EqualTo(messagesPerType * 2),
            $"User service should receive {messagesPerType * 2} messages");
        Assert.That(orderMessages.Count, Is.EqualTo(messagesPerType * 2),
            $"Order service should receive {messagesPerType * 2} messages");
        Assert.That(paymentMessages.Count, Is.EqualTo(messagesPerType * 2),
            $"Payment service should receive {messagesPerType * 2} messages");

        // Verify message content specificity
        var userMessagesByKey = userConsumer.GetMessagesByRoutingKey();
        var orderMessagesByKey = orderConsumer.GetMessagesByRoutingKey();
        var paymentMessagesByKey = paymentConsumer.GetMessagesByRoutingKey();

        Assert.That(userMessagesByKey.ContainsKey("user.created"), Is.True);
        Assert.That(userMessagesByKey.ContainsKey("user.updated"), Is.True);
        Assert.That(orderMessagesByKey.ContainsKey("order.created"), Is.True);
        Assert.That(orderMessagesByKey.ContainsKey("order.shipped"), Is.True);
        Assert.That(paymentMessagesByKey.ContainsKey("payment.processed"), Is.True);
        Assert.That(paymentMessagesByKey.ContainsKey("payment.failed"), Is.True);

        Logger.LogInformation("Selective consumption test completed. Each service received only relevant messages");

        // Cleanup
        userConsumer.Dispose();
        orderConsumer.Dispose();
        paymentConsumer.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task DynamicRouting_ChangeBindings_Should_UpdateMessageRouting()
    {
        // Arrange
        var exchangeName = $"test-dynamic-routing-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new RoutingProducer(Channel, loggerFactory.CreateLogger<RoutingProducer>(), exchangeName);
        
        var consumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "dynamic-consumer");

        await producer.InitializeAsync(durable: false);
        // Use temporary queue and keep consumer active to avoid queue deletion
        await consumer.InitializeAsync(new[] { "level1" }, useNamedQueue: false, exclusive: true);

        // Act
        Logger.LogInformation("Test: Dynamic Routing - Changing routing key bindings during runtime");

        await consumer.StartConsumingAsync(autoAck: true);

        // Phase 1: Initial binding to "level1"
        await producer.PublishMessageAsync("level1", "Message for level1 - Phase 1", false);
        await producer.PublishMessageAsync("level2", "Message for level2 - Phase 1 (should not be received)", false);
        await Task.Delay(1000);

        var phase1Messages = consumer.GetReceivedMessages();
        consumer.ClearReceived();

        // Phase 2: Add "level2" binding (while still consuming)
        await consumer.AddRoutingKeysAsync(new[] { "level2" });
        
        await producer.PublishMessageAsync("level1", "Message for level1 - Phase 2", false);
        await producer.PublishMessageAsync("level2", "Message for level2 - Phase 2", false);
        await producer.PublishMessageAsync("level3", "Message for level3 - Phase 2 (should not be received)", false);
        await Task.Delay(1000);

        var phase2Messages = consumer.GetReceivedMessages();
        consumer.ClearReceived();

        // Phase 3: Remove "level1", keep "level2"
        await consumer.RemoveRoutingKeysAsync(new[] { "level1" });
        
        await producer.PublishMessageAsync("level1", "Message for level1 - Phase 3 (should not be received)", false);
        await producer.PublishMessageAsync("level2", "Message for level2 - Phase 3", false);
        await Task.Delay(1000);

        var phase3Messages = consumer.GetReceivedMessages();
        await consumer.StopConsumingAsync();

        // Assert
        Assert.That(phase1Messages.Count, Is.EqualTo(1),
            "Phase 1: Should receive 1 message (only level1)");
        Assert.That(phase1Messages[0].Contains("level1"), Is.True,
            "Phase 1: Should receive level1 message");

        Assert.That(phase2Messages.Count, Is.EqualTo(2),
            "Phase 2: Should receive 2 messages (level1 and level2)");
        Assert.That(phase2Messages.Any(m => m.Contains("level1")), Is.True,
            "Phase 2: Should receive level1 message");
        Assert.That(phase2Messages.Any(m => m.Contains("level2")), Is.True,
            "Phase 2: Should receive level2 message");

        Assert.That(phase3Messages.Count, Is.EqualTo(1),
            "Phase 3: Should receive 1 message (only level2)");
        Assert.That(phase3Messages[0].Contains("level2"), Is.True,
            "Phase 3: Should receive level2 message");

        Logger.LogInformation("Dynamic routing test completed. Routing keys successfully changed during runtime");

        // Cleanup
        consumer.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task MessagePersistence_DurableRoutingExchange_Should_HandlePersistentMessages()
    {
        // Arrange
        var exchangeName = $"test-durable-routing-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new RoutingProducer(Channel, loggerFactory.CreateLogger<RoutingProducer>(), exchangeName);
        
        const int messagesPerSeverity = 3;
        var consumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "persistent-consumer");

        // Act
        Logger.LogInformation("Test: Message Persistence - Using durable exchange with persistent messages");

        // Initialize with durable resources
        await producer.InitializeAsync(durable: true);
        await consumer.InitializeForLogRoutingAsync(new[] { "ERROR", "WARNING" }, useNamedQueue: true, exclusive: false, durable: true);

        var consumerTask = consumer.ConsumeLogMessagesAsync(messagesPerSeverity * 2, autoAck: false, TimeSpan.FromSeconds(30));

        await Task.Delay(500);

        // Publish persistent messages
        await producer.PublishTestLogMessagesAsync(messagesPerSeverity, persistent: true);

        var receivedMessages = await consumerTask;

        // Assert
        Assert.That(receivedMessages.Count, Is.EqualTo(messagesPerSeverity * 2),
            $"Consumer should receive {messagesPerSeverity * 2} persistent messages (ERROR + WARNING)");

        // Verify only ERROR and WARNING messages were received (not INFO or DEBUG)
        var receivedSeverities = receivedMessages.Select(msg => msg.Severity).Distinct().OrderBy(s => s).ToArray();
        Assert.That(receivedSeverities, Is.EqualTo(new[] { "ERROR", "WARNING" }),
            "Consumer should only receive ERROR and WARNING messages");

        Logger.LogInformation("Message persistence test completed successfully");

        // Cleanup durable resources
        consumer.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task RoutingStatistics_MessageCounting_Should_TrackMessagesCorrectly()
    {
        // Arrange
        var exchangeName = $"test-routing-stats-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var producer = new RoutingProducer(Channel, loggerFactory.CreateLogger<RoutingProducer>(), exchangeName);
        
        var routingKeys = new[] { "stats.test1", "stats.test2" };
        const int messagesPerKey = 7;
        
        await producer.InitializeAsync(durable: false);

        var startTime = DateTime.UtcNow;
        var consumer = new RoutingConsumer(Channel, loggerFactory.CreateLogger<RoutingConsumer>(), 
            exchangeName, "stats-consumer");

        // Act
        Logger.LogInformation("Test: Routing Statistics - Tracking message counts and routing info");

        await consumer.InitializeAsync(routingKeys);

        var consumeTask = consumer.ConsumeMessagesAsync(routingKeys.Length * messagesPerKey, 
            autoAck: true, TimeSpan.FromSeconds(30));
        await Task.Delay(200);

        await producer.PublishTestRoutingMessagesAsync(routingKeys, messagesPerKey);
        
        var receivedMessages = await consumeTask;
        var endTime = DateTime.UtcNow;

        var stats = consumer.GetStats();
        var messagesByRoutingKey = consumer.GetMessagesByRoutingKey();

        // Assert
        Assert.That(receivedMessages.Count, Is.EqualTo(routingKeys.Length * messagesPerKey));
        Assert.That(stats.MessagesReceived, Is.EqualTo(routingKeys.Length * messagesPerKey));
        Assert.That(stats.ConsumerId, Is.EqualTo("stats-consumer"));
        Assert.That(stats.StartTime, Is.GreaterThanOrEqualTo(startTime));
        Assert.That(stats.LastMessageTime, Is.Not.Null);
        Assert.That(stats.LastMessageTime, Is.LessThanOrEqualTo(endTime));
        Assert.That(stats.BoundRoutingKeys, Is.EqualTo(routingKeys));
        Assert.That(string.IsNullOrEmpty(stats.QueueName), Is.False);

        // Verify routing key distribution
        foreach (var routingKey in routingKeys)
        {
            Assert.That(messagesByRoutingKey.ContainsKey(routingKey), Is.True,
                $"Should have messages for routing key '{routingKey}'");
            Assert.That(messagesByRoutingKey[routingKey].Count, Is.EqualTo(messagesPerKey),
                $"Should have {messagesPerKey} messages for routing key '{routingKey}'");
        }

        Logger.LogInformation("Routing statistics: ID={ConsumerId}, Messages={MessageCount}, RoutingKeys=[{RoutingKeys}], Duration={Duration}ms", 
            stats.ConsumerId, stats.MessagesReceived, string.Join(", ", stats.BoundRoutingKeys),
            stats.LastMessageTime?.Subtract(stats.StartTime).TotalMilliseconds);

        // Cleanup
        consumer.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [TearDown]
    public override async Task TearDown()
    {
        // Clean up test exchanges - the specific exchange names are generated per test now
        await base.TearDown();
    }
}