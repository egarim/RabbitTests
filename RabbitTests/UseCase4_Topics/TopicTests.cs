using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;
using NUnit.Framework;

namespace RabbitTests.UseCase4_Topics;

/// <summary>
/// Test class for Use Case 4: Topic-Based Routing (Topic Exchange)
/// Demonstrates topic exchange routing patterns with wildcard pattern matching
/// </summary>
[TestFixture]
public class TopicTests : TestBase
{
    [Test]
    public async Task WildcardRouting_SingleAndMultiWordWildcards_Should_MatchCorrectPatterns()
    {
        // Arrange
        var exchangeName = $"test-wildcard-routing-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        // Create subscribers with different wildcard patterns
        var allLogsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "all-logs-subscriber");
        var errorOnlySubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "error-only-subscriber");
        var webLogsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "web-logs-subscriber");
        var databaseAllSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "database-all-subscriber");

        // Initialize publisher and subscribers with different patterns
        await publisher.InitializeAsync(durable: false);
        await allLogsSubscriber.InitializeAsync(new[] { "logs.#" }); // All logs (multi-word wildcard)
        await errorOnlySubscriber.InitializeAsync(new[] { "logs.error.*" }); // Only error logs from any component (single-word wildcard)
        await webLogsSubscriber.InitializeAsync(new[] { "logs.*.web" }); // All web logs regardless of level
        await databaseAllSubscriber.InitializeAsync(new[] { "logs.*.database", "logs.debug.*" }); // Database logs + all debug logs

        // Act
        Logger.LogInformation("Test: Wildcard Routing - Testing * and # wildcard pattern matching");

        var allLogsTask = allLogsSubscriber.ConsumeMessagesAsync(10, autoAck: true, TimeSpan.FromSeconds(30));
        var errorOnlyTask = errorOnlySubscriber.ConsumeMessagesAsync(3, autoAck: true, TimeSpan.FromSeconds(30));
        var webLogsTask = webLogsSubscriber.ConsumeMessagesAsync(3, autoAck: true, TimeSpan.FromSeconds(30));
        var databaseAllTask = databaseAllSubscriber.ConsumeMessagesAsync(4, autoAck: true, TimeSpan.FromSeconds(30));

        await Task.Delay(500); // Ensure consumers are ready

        // Publish test log messages with hierarchical routing keys
        await publisher.PublishTestLogMessagesAsync(messagesPerPattern: 1);

        var results = await Task.WhenAll(allLogsTask, errorOnlyTask, webLogsTask, databaseAllTask);
        var allLogsMessages = results[0];
        var errorOnlyMessages = results[1];
        var webLogsMessages = results[2];
        var databaseAllMessages = results[3];

        // Assert
        Assert.That(allLogsMessages.Count, Is.EqualTo(10), 
            "All logs subscriber should receive all 10 log messages (logs.#)");

        Assert.That(errorOnlyMessages.Count, Is.EqualTo(3), 
            "Error-only subscriber should receive 3 error messages (logs.error.*)");

        Assert.That(webLogsMessages.Count, Is.EqualTo(3), 
            "Web logs subscriber should receive 3 web messages (logs.*.web)");

        Assert.That(databaseAllMessages.Count, Is.EqualTo(4), 
            "Database subscriber should receive 3 database logs + 1 debug cache = 4 messages");

        Logger.LogInformation("Wildcard routing test completed successfully. Pattern matching worked correctly");

        // Cleanup
        allLogsSubscriber.Dispose();
        errorOnlySubscriber.Dispose();
        webLogsSubscriber.Dispose();
        databaseAllSubscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task HierarchicalRouting_SystemLevelComponent_Should_RouteByHierarchy()
    {
        // Arrange
        var exchangeName = $"test-hierarchical-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        // Create subscribers for different system components
        var userServiceSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "user-service-subscriber");
        var orderServiceSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "order-service-subscriber");
        var allServicesSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "all-services-subscriber");
        var notificationAllSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "notification-all-subscriber");

        // Initialize with hierarchical patterns
        await publisher.InitializeAsync(durable: false);
        await userServiceSubscriber.InitializeAsync(new[] { "user.service.*" }); // All user service events
        await orderServiceSubscriber.InitializeAsync(new[] { "order.service.*" }); // All order service events
        await allServicesSubscriber.InitializeAsync(new[] { "*.service.*" }); // All service-level events from any system
        await notificationAllSubscriber.InitializeAsync(new[] { "notification.#" }); // All notification events

        // Act
        Logger.LogInformation("Test: Hierarchical Routing - Testing system.level.component routing");

        const int messagesPerService = 2;
        var userTask = userServiceSubscriber.ConsumeEventMessagesAsync(messagesPerService * 3, autoAck: true, TimeSpan.FromSeconds(30)); // 3 user service types
        var orderTask = orderServiceSubscriber.ConsumeEventMessagesAsync(messagesPerService * 3, autoAck: true, TimeSpan.FromSeconds(30)); // 3 order service types
        var allServicesTask = allServicesSubscriber.ConsumeEventMessagesAsync(messagesPerService * 8, autoAck: true, TimeSpan.FromSeconds(30)); // 8 service events total
        var notificationTask = notificationAllSubscriber.ConsumeEventMessagesAsync(messagesPerService * 2, autoAck: true, TimeSpan.FromSeconds(30)); // 2 notification types

        await Task.Delay(500);

        // Publish microservice events
        await publisher.PublishTestMicroserviceEventsAsync(messagesPerService);

        var results = await Task.WhenAll(userTask, orderTask, allServicesTask, notificationTask);
        var userEvents = results[0];
        var orderEvents = results[1];
        var allServiceEvents = results[2];
        var notificationEvents = results[3];

        // Assert
        Assert.That(userEvents.Count, Is.EqualTo(messagesPerService * 3),
            $"User service subscriber should receive {messagesPerService * 3} user service events");

        Assert.That(orderEvents.Count, Is.EqualTo(messagesPerService * 3),
            $"Order service subscriber should receive {messagesPerService * 3} order service events");

        Assert.That(allServiceEvents.Count, Is.EqualTo(messagesPerService * 8),
            $"All services subscriber should receive {messagesPerService * 8} service-level events");

        Assert.That(notificationEvents.Count, Is.EqualTo(messagesPerService * 2),
            $"Notification subscriber should receive {messagesPerService * 2} notification events");

        // Verify event types
        Assert.That(userEvents.All(e => e.System == "user"), Is.True,
            "User service subscriber should only receive user system events");

        Assert.That(orderEvents.All(e => e.System == "order"), Is.True,
            "Order service subscriber should only receive order system events");

        Assert.That(allServiceEvents.All(e => e.Level == "service"), Is.True,
            "All services subscriber should only receive service-level events");

        Assert.That(notificationEvents.All(e => e.System == "notification"), Is.True,
            "Notification subscriber should only receive notification system events");

        Logger.LogInformation("Hierarchical routing test completed successfully. System.level.component routing worked correctly");

        // Cleanup
        userServiceSubscriber.Dispose();
        orderServiceSubscriber.Dispose();
        allServicesSubscriber.Dispose();
        notificationAllSubscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task ComplexPatterns_MultipleOverlappingRules_Should_HandleComplexRouting()
    {
        // Arrange
        var exchangeName = $"test-complex-patterns-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        // Create subscribers with complex, overlapping patterns
        var systemApp1Subscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "system-app1-subscriber");
        var authPatternsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "auth-patterns-subscriber");
        var analyticsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "analytics-subscriber");
        var monitoringSlowSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "monitoring-slow-subscriber");

        // Initialize with complex patterns
        await publisher.InitializeAsync(durable: false);
        await systemApp1Subscriber.InitializeAsync(new[] { "system.app1.#" }); // All app1 events
        await authPatternsSubscriber.InitializeAsync(new[] { "*.*.*.auth.*", "*.*.module.auth.#" }); // Auth-related patterns
        await analyticsSubscriber.InitializeAsync(new[] { "analytics.#" }); // All analytics
        await monitoringSlowSubscriber.InitializeAsync(new[] { "*.*.*.*.slow", "monitoring.performance.#" }); // Slow operations and performance

        // Act
        Logger.LogInformation("Test: Complex Patterns - Testing multiple overlapping routing rules");

        const int messagesPerPattern = 2;
        var app1Task = systemApp1Subscriber.ConsumeMessagesAsync(8, autoAck: true, TimeSpan.FromSeconds(30)); // 4 app1 patterns * 2
        var authTask = authPatternsSubscriber.ConsumeMessagesAsync(4, autoAck: true, TimeSpan.FromSeconds(30)); // 2 auth patterns * 2
        var analyticsTask = analyticsSubscriber.ConsumeMessagesAsync(8, autoAck: true, TimeSpan.FromSeconds(30)); // 4 analytics patterns * 2
        var monitoringSlowTask = monitoringSlowSubscriber.ConsumeMessagesAsync(6, autoAck: true, TimeSpan.FromSeconds(30)); // 1 slow + 2 performance * 2

        await Task.Delay(500);

        // Publish complex pattern messages
        await publisher.PublishComplexPatternMessagesAsync(messagesPerPattern);

        var results = await Task.WhenAll(app1Task, authTask, analyticsTask, monitoringSlowTask);
        var app1Messages = results[0];
        var authMessages = results[1];
        var analyticsMessages = results[2];
        var monitoringSlowMessages = results[3];

        // Assert
        Assert.That(app1Messages.Count, Is.EqualTo(8),
            "System app1 subscriber should receive 8 app1-related messages");

        Assert.That(authMessages.Count, Is.EqualTo(4),
            "Auth patterns subscriber should receive 4 auth-related messages");

        Assert.That(analyticsMessages.Count, Is.EqualTo(8),
            "Analytics subscriber should receive 8 analytics messages");

        Assert.That(monitoringSlowMessages.Count, Is.EqualTo(6),
            "Monitoring slow subscriber should receive 6 slow/performance messages");

        // Verify message content specificity
        Assert.That(app1Messages.All(m => m.Contains("system.app1")), Is.True,
            "App1 subscriber should only receive app1 system messages");

        Assert.That(authMessages.All(m => m.Contains("auth")), Is.True,
            "Auth subscriber should only receive auth-related messages");

        Assert.That(analyticsMessages.All(m => m.Contains("analytics")), Is.True,
            "Analytics subscriber should only receive analytics messages");

        Assert.That(monitoringSlowMessages.All(m => m.Contains("slow") || m.Contains("monitoring.performance")), Is.True,
            "Monitoring slow subscriber should only receive slow/performance messages");

        Logger.LogInformation("Complex patterns test completed successfully. Overlapping routing rules handled correctly");

        // Cleanup
        systemApp1Subscriber.Dispose();
        authPatternsSubscriber.Dispose();
        analyticsSubscriber.Dispose();
        monitoringSlowSubscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task EventDrivenArchitecture_MicroserviceEvents_Should_RouteToCorrectHandlers()
    {
        // Arrange
        var exchangeName = $"test-event-driven-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        // Create subscribers representing different microservice event handlers
        var userEventsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "user-events-handler");
        var orderEventsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "order-events-handler");
        var inventoryEventsSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "inventory-events-handler");
        var auditLogSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "audit-log-handler");

        // Initialize with microservice-specific patterns
        await publisher.InitializeAsync(durable: false);
        await userEventsSubscriber.InitializeAsync(new[] { "user.#" }); // All user events
        await orderEventsSubscriber.InitializeAsync(new[] { "order.#" }); // All order events
        await inventoryEventsSubscriber.InitializeAsync(new[] { "inventory.#" }); // All inventory events
        await auditLogSubscriber.InitializeAsync(new[] { "#" }); // All events for audit logging

        // Act
        Logger.LogInformation("Test: Event-Driven Architecture - Simulating microservice event routing");

        const int messagesPerService = 2;
        var userTask = userEventsSubscriber.ConsumeEventMessagesAsync(messagesPerService * 3, autoAck: true, TimeSpan.FromSeconds(30));
        var orderTask = orderEventsSubscriber.ConsumeEventMessagesAsync(messagesPerService * 3, autoAck: true, TimeSpan.FromSeconds(30));
        var inventoryTask = inventoryEventsSubscriber.ConsumeEventMessagesAsync(messagesPerService * 2, autoAck: true, TimeSpan.FromSeconds(30));
        var auditTask = auditLogSubscriber.ConsumeEventMessagesAsync(messagesPerService * 10, autoAck: true, TimeSpan.FromSeconds(30)); // All events

        await Task.Delay(500);

        // Publish microservice events
        await publisher.PublishTestMicroserviceEventsAsync(messagesPerService);

        var results = await Task.WhenAll(userTask, orderTask, inventoryTask, auditTask);
        var userEvents = results[0];
        var orderEvents = results[1];
        var inventoryEvents = results[2];
        var auditEvents = results[3];

        // Assert
        Assert.That(userEvents.Count, Is.EqualTo(messagesPerService * 3),
            $"User events handler should receive {messagesPerService * 3} user events");

        Assert.That(orderEvents.Count, Is.EqualTo(messagesPerService * 3),
            $"Order events handler should receive {messagesPerService * 3} order events");

        Assert.That(inventoryEvents.Count, Is.EqualTo(messagesPerService * 2),
            $"Inventory events handler should receive {messagesPerService * 2} inventory events");

        Assert.That(auditEvents.Count, Is.EqualTo(messagesPerService * 10),
            $"Audit log handler should receive all {messagesPerService * 10} events");

        // Verify event specificity and business logic
        var userEventTypes = userEvents.Select(e => e.EventType).Distinct().ToArray();
        var orderEventTypes = orderEvents.Select(e => e.EventType).Distinct().ToArray();
        var inventoryEventTypes = inventoryEvents.Select(e => e.EventType).Distinct().ToArray();

        Assert.That(userEventTypes, Contains.Item("UserRegistered"));
        Assert.That(userEventTypes, Contains.Item("UserLoggedIn"));
        Assert.That(userEventTypes, Contains.Item("ProfileUpdated"));

        Assert.That(orderEventTypes, Contains.Item("OrderCreated"));
        Assert.That(orderEventTypes, Contains.Item("PaymentProcessed"));
        Assert.That(orderEventTypes, Contains.Item("OrderShipped"));

        Assert.That(inventoryEventTypes, Contains.Item("StockUpdated"));
        Assert.That(inventoryEventTypes, Contains.Item("LowStockAlert"));

        // Verify audit receives all systems
        var auditSystems = auditEvents.Select(e => e.System).Distinct().OrderBy(s => s).ToArray();
        Assert.That(auditSystems, Is.EqualTo(new[] { "inventory", "notification", "order", "user" }),
            "Audit handler should receive events from all systems");

        Logger.LogInformation("Event-driven architecture test completed successfully. Microservice events routed correctly");

        // Cleanup
        userEventsSubscriber.Dispose();
        orderEventsSubscriber.Dispose();
        inventoryEventsSubscriber.Dispose();
        auditLogSubscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task DynamicTopicPatterns_ChangePatternsAtRuntime_Should_UpdateRoutingBehavior()
    {
        // Arrange
        var exchangeName = $"test-dynamic-topic-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        var dynamicSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "dynamic-subscriber");

        await publisher.InitializeAsync(durable: false);
        await dynamicSubscriber.InitializeAsync(new[] { "logs.info.*" }, useNamedQueue: false, exclusive: true);

        // Act
        Logger.LogInformation("Test: Dynamic Topic Patterns - Changing binding patterns during runtime");

        await dynamicSubscriber.StartConsumingAsync(autoAck: true);

        // Phase 1: Initially bound to "logs.info.*"
        await publisher.PublishMessageAsync("logs.info.web", "Info web message - Phase 1", false);
        await publisher.PublishMessageAsync("logs.error.web", "Error web message - Phase 1 (should not be received)", false);
        await publisher.PublishMessageAsync("logs.info.database", "Info database message - Phase 1", false);
        await Task.Delay(1000);

        var phase1Messages = dynamicSubscriber.GetReceivedMessages();
        dynamicSubscriber.ClearReceived();

        // Phase 2: Add "logs.error.*" pattern
        await dynamicSubscriber.AddBindingPatternsAsync(new[] { "logs.error.*" });
        
        await publisher.PublishMessageAsync("logs.info.web", "Info web message - Phase 2", false);
        await publisher.PublishMessageAsync("logs.error.web", "Error web message - Phase 2", false);
        await publisher.PublishMessageAsync("logs.warning.web", "Warning web message - Phase 2 (should not be received)", false);
        await Task.Delay(1000);

        var phase2Messages = dynamicSubscriber.GetReceivedMessages();
        dynamicSubscriber.ClearReceived();

        // Phase 3: Remove "logs.info.*", add "logs.#" (all logs)
        await dynamicSubscriber.RemoveBindingPatternsAsync(new[] { "logs.info.*" });
        await dynamicSubscriber.AddBindingPatternsAsync(new[] { "logs.#" });
        
        await publisher.PublishMessageAsync("logs.info.web", "Info web message - Phase 3", false);
        await publisher.PublishMessageAsync("logs.error.web", "Error web message - Phase 3", false);
        await publisher.PublishMessageAsync("logs.warning.database", "Warning database message - Phase 3", false);
        await publisher.PublishMessageAsync("logs.debug.cache", "Debug cache message - Phase 3", false);
        await Task.Delay(1000);

        var phase3Messages = dynamicSubscriber.GetReceivedMessages();
        await dynamicSubscriber.StopConsumingAsync();

        // Assert
        Assert.That(phase1Messages.Count, Is.EqualTo(2),
            "Phase 1: Should receive 2 messages (logs.info.*)");
        Assert.That(phase1Messages.All(m => m.Contains("Info")), Is.True,
            "Phase 1: Should only receive info messages");

        Assert.That(phase2Messages.Count, Is.EqualTo(2),
            "Phase 2: Should receive 2 messages (logs.info.* + logs.error.*)");
        Assert.That(phase2Messages.Any(m => m.Contains("Info")), Is.True,
            "Phase 2: Should receive info message");
        Assert.That(phase2Messages.Any(m => m.Contains("Error")), Is.True,
            "Phase 2: Should receive error message");

        Assert.That(phase3Messages.Count, Is.EqualTo(4),
            "Phase 3: Should receive 4 messages (logs.# - all logs)");
        Assert.That(phase3Messages.Any(m => m.Contains("Info")), Is.True,
            "Phase 3: Should receive info message");
        Assert.That(phase3Messages.Any(m => m.Contains("Error")), Is.True,
            "Phase 3: Should receive error message");
        Assert.That(phase3Messages.Any(m => m.Contains("Warning")), Is.True,
            "Phase 3: Should receive warning message");
        Assert.That(phase3Messages.Any(m => m.Contains("Debug")), Is.True,
            "Phase 3: Should receive debug message");

        Logger.LogInformation("Dynamic topic patterns test completed. Binding patterns successfully changed during runtime");

        // Cleanup
        dynamicSubscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task TopicPersistence_DurableTopicExchange_Should_HandlePersistentMessages()
    {
        // Arrange
        var exchangeName = $"test-durable-topic-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        var persistentSubscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "persistent-subscriber");

        // Act
        Logger.LogInformation("Test: Topic Persistence - Using durable exchange with persistent messages");

        // Initialize with durable resources
        await publisher.InitializeAsync(durable: true);
        await persistentSubscriber.InitializeAsync(new[] { "logs.error.*", "logs.warning.*" }, 
            useNamedQueue: true, exclusive: false, durable: true);

        const int messagesPerPattern = 3;
        var consumeTask = persistentSubscriber.ConsumeMessagesAsync(6, autoAck: false, TimeSpan.FromSeconds(30)); // 2 patterns * 3 messages

        await Task.Delay(500);

        // Publish persistent messages
        await publisher.PublishTestLogMessagesAsync(messagesPerPattern, persistent: true);

        var receivedMessages = await consumeTask;

        // Assert
        Assert.That(receivedMessages.Count, Is.EqualTo(6),
            "Persistent subscriber should receive 6 messages (error + warning logs)");

        // Verify only error and warning messages were received (not info or debug)
        Assert.That(receivedMessages.All(m => m.Contains("error") || m.Contains("warning")), Is.True,
            "Should only receive error and warning log messages");

        Logger.LogInformation("Topic persistence test completed successfully");

        // Cleanup durable resources
        persistentSubscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [Test]
    public async Task TopicStatistics_PatternMatchingAndCounting_Should_TrackCorrectly()
    {
        // Arrange
        var exchangeName = $"test-topic-stats-{Guid.NewGuid():N}";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var publisher = new TopicPublisher(Channel, loggerFactory.CreateLogger<TopicPublisher>(), exchangeName);
        
        var patterns = new[] { "user.service.*", "order.#", "*.*.email" };
        var subscriber = new TopicSubscriber(Channel, loggerFactory.CreateLogger<TopicSubscriber>(), 
            exchangeName, "stats-subscriber");

        await publisher.InitializeAsync(durable: false);
        await subscriber.InitializeAsync(patterns);

        // Act
        Logger.LogInformation("Test: Topic Statistics - Tracking pattern matches and message counts");

        const int messagesPerService = 2;
        var startTime = DateTime.UtcNow;
        
        var consumeTask = subscriber.ConsumeEventMessagesAsync(8, autoAck: true, TimeSpan.FromSeconds(30)); // Should match 8 events

        await Task.Delay(200);

        await publisher.PublishTestMicroserviceEventsAsync(messagesPerService);
        
        var receivedEvents = await consumeTask;
        var endTime = DateTime.UtcNow;

        var stats = subscriber.GetStats();
        var eventsByPattern = subscriber.GetEventsByPattern();

        // Assert
        Assert.That(receivedEvents.Count, Is.EqualTo(8));
        Assert.That(stats.MessagesReceived, Is.EqualTo(8));
        Assert.That(stats.SubscriberId, Is.EqualTo("stats-subscriber"));
        Assert.That(stats.StartTime, Is.GreaterThanOrEqualTo(startTime));
        Assert.That(stats.LastMessageTime, Is.Not.Null);
        Assert.That(stats.LastMessageTime, Is.LessThanOrEqualTo(endTime));
        Assert.That(stats.BoundPatterns, Is.EqualTo(patterns));
        Assert.That(string.IsNullOrEmpty(stats.QueueName), Is.False);

        // Verify pattern matching worked correctly
        Assert.That(eventsByPattern["user.service.*"].Count, Is.EqualTo(6), // user.service.registration, login, profile * 2
            "Should have 6 events matching 'user.service.*' pattern");
        
        Assert.That(eventsByPattern["order.#"].Count, Is.EqualTo(6), // order.service.creation, payment, shipping * 2
            "Should have 6 events matching 'order.#' pattern");
        
        Assert.That(eventsByPattern["*.*.email"].Count, Is.EqualTo(2), // notification.service.email * 2
            "Should have 2 events matching '*.*.email' pattern");

        Logger.LogInformation("Topic statistics: ID={SubscriberId}, Messages={MessageCount}, Patterns=[{Patterns}], Duration={Duration}ms", 
            stats.SubscriberId, stats.MessagesReceived, string.Join(", ", stats.BoundPatterns),
            stats.LastMessageTime?.Subtract(stats.StartTime).TotalMilliseconds);

        // Cleanup
        subscriber.Dispose();
        await SafeDeleteExchange(exchangeName);
    }

    [TearDown]
    public override async Task TearDown()
    {
        // Clean up test exchanges
        await base.TearDown();
    }
}