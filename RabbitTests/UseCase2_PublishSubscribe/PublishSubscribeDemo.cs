using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;

namespace RabbitTests.UseCase2_PublishSubscribe;

/// <summary>
/// Interactive demonstration of Use Case 2: Publish-Subscribe Pattern (Fanout Exchange)
/// Run this to see the publish-subscribe pattern in action
/// </summary>
public class PublishSubscribeDemo : TestBase
{
    private const string DemoExchangeName = "demo-fanout-exchange";

    /// <summary>
    /// Demonstrates basic publish-subscribe pattern with multiple subscribers
    /// </summary>
    public async Task RunBasicPubSubDemoAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), DemoExchangeName);
        
        Console.WriteLine("=== RabbitMQ Publish-Subscribe Demo ===");
        Console.WriteLine();

        // Initialize publisher
        await publisher.InitializeAsync(durable: false);
        Console.WriteLine("? Fanout exchange initialized");

        // Create multiple subscribers representing different services
        var services = new[] { "UserService", "EmailService", "LoggingService", "AnalyticsService" };
        var subscribers = new List<SubscriberService>();

        foreach (var serviceName in services)
        {
            var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
                DemoExchangeName, serviceName);
            await subscriber.InitializeAsync(useTemporaryQueue: true, exclusive: true);
            
            // Set up event handlers for real-time feedback
            subscriber.OnMessageReceived += (message, subscriberId) =>
            {
                Console.WriteLine($"  [{subscriberId}] Received: {message}");
            };

            subscribers.Add(subscriber);
        }

        Console.WriteLine($"? Created {subscribers.Count} subscribers (services)");
        Console.WriteLine();

        // Start all subscribers
        foreach (var subscriber in subscribers)
        {
            await subscriber.StartConsumingAsync(autoAck: true);
        }

        Console.WriteLine("? All subscribers started and ready");
        Console.WriteLine();

        // Demonstrate broadcasting
        Console.WriteLine("?? Broadcasting messages to all subscribers...");
        Console.WriteLine();

        var messages = new[]
        {
            "?? System notification: Maintenance window scheduled",
            "?? New feature deployed: Enhanced search functionality",
            "??  Alert: High CPU usage detected on server 01",
            "? Status update: All systems operational",
            "?? Daily report: User activity summary available"
        };

        foreach (var message in messages)
        {
            Console.WriteLine($"Publishing: {message}");
            await publisher.PublishBroadcastMessageAsync(message);
            await Task.Delay(1500); // Allow time to see the broadcasts
            Console.WriteLine();
        }

        // Stop subscribers and show statistics
        Console.WriteLine("?? Subscriber Statistics:");
        Console.WriteLine();

        foreach (var subscriber in subscribers)
        {
            await subscriber.StopConsumingAsync();
            var stats = subscriber.GetStats();
            Console.WriteLine($"  {stats.SubscriberId}:");
            Console.WriteLine($"    - Messages received: {stats.MessagesReceived}");
            Console.WriteLine($"    - Queue name: {stats.QueueName}");
            Console.WriteLine($"    - Active time: {(stats.LastMessageTime - stats.StartTime)?.TotalSeconds:F1}s");
            Console.WriteLine();
        }

        // Cleanup
        foreach (var subscriber in subscribers)
        {
            subscriber.Dispose();
        }

        Console.WriteLine("? Demo completed successfully!");
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates dynamic subscription management
    /// </summary>
    public async Task RunDynamicSubscriptionDemoAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), DemoExchangeName);
        
        Console.WriteLine("=== Dynamic Subscription Demo ===");
        Console.WriteLine();

        await publisher.InitializeAsync(durable: false);

        var allSubscribers = new List<SubscriberService>();

        // Phase 1: Start with core services
        Console.WriteLine("Phase 1: Starting core services...");
        var coreServices = new[] { "CoreService1", "CoreService2" };
        
        foreach (var serviceName in coreServices)
        {
            var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
                DemoExchangeName, serviceName);
            await subscriber.InitializeAsync();
            await subscriber.StartConsumingAsync();
            
            subscriber.OnMessageReceived += (message, subscriberId) =>
            {
                Console.WriteLine($"  [{subscriberId}] {message}");
            };
            
            allSubscribers.Add(subscriber);
            Console.WriteLine($"? {serviceName} started");
        }

        Console.WriteLine();
        Console.WriteLine("?? Broadcasting to core services...");
        await publisher.PublishBroadcastMessageAsync("Message 1: Core services only");
        await publisher.PublishBroadcastMessageAsync("Message 2: Core services only");
        await Task.Delay(1000);
        Console.WriteLine();

        // Phase 2: Add optional services
        Console.WriteLine("Phase 2: Adding optional services...");
        var optionalServices = new[] { "OptionalService1", "OptionalService2", "OptionalService3" };
        
        foreach (var serviceName in optionalServices)
        {
            var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
                DemoExchangeName, serviceName);
            await subscriber.InitializeAsync();
            await subscriber.StartConsumingAsync();
            
            subscriber.OnMessageReceived += (message, subscriberId) =>
            {
                Console.WriteLine($"  [{subscriberId}] {message}");
            };
            
            allSubscribers.Add(subscriber);
            Console.WriteLine($"? {serviceName} added");
        }

        Console.WriteLine();
        Console.WriteLine("?? Broadcasting to all services...");
        await publisher.PublishBroadcastMessageAsync("Message 3: All services active");
        await publisher.PublishBroadcastMessageAsync("Message 4: All services active");
        await Task.Delay(1000);
        Console.WriteLine();

        // Phase 3: Remove some services
        Console.WriteLine("Phase 3: Removing some services...");
        var servicesToRemove = allSubscribers.Take(2).ToList();
        
        foreach (var subscriber in servicesToRemove)
        {
            await subscriber.StopConsumingAsync();
            Console.WriteLine($"? {subscriber.SubscriberId} stopped");
        }

        Console.WriteLine();
        Console.WriteLine("?? Broadcasting to remaining services...");
        await publisher.PublishBroadcastMessageAsync("Message 5: Remaining services only");
        await publisher.PublishBroadcastMessageAsync("Message 6: Remaining services only");
        await Task.Delay(1000);
        Console.WriteLine();

        // Show final statistics
        Console.WriteLine("?? Final Statistics:");
        foreach (var subscriber in allSubscribers)
        {
            var stats = subscriber.GetStats();
            Console.WriteLine($"  {stats.SubscriberId}: {stats.MessagesReceived} messages");
        }

        // Cleanup
        foreach (var subscriber in allSubscribers)
        {
            subscriber.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("? Dynamic subscription demo completed!");
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates structured notification broadcasting
    /// </summary>
    public async Task RunNotificationDemoAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        var publisher = new PublisherService(Channel, loggerFactory.CreateLogger<PublisherService>(), DemoExchangeName);
        
        Console.WriteLine("=== Notification Broadcasting Demo ===");
        Console.WriteLine();

        await publisher.InitializeAsync(durable: false);

        // Create subscribers for different notification types
        var subscribers = new List<SubscriberService>();
        var serviceTypes = new[] { "WebAPI", "MobileApp", "EmailService", "SMSService", "PushNotificationService" };

        foreach (var serviceType in serviceTypes)
        {
            var subscriber = new SubscriberService(Channel, loggerFactory.CreateLogger<SubscriberService>(), 
                DemoExchangeName, serviceType);
            await subscriber.InitializeAsync();
            
            subscriber.OnNotificationReceived += (notification, subscriberId) =>
            {
                var emoji = notification.Type switch
                {
                    "INFO" => "??",
                    "WARNING" => "??",
                    "ERROR" => "?",
                    "SUCCESS" => "?",
                    _ => "??"
                };
                
                Console.WriteLine($"  [{subscriberId}] {emoji} {notification.Type}: {notification.Message}");
            };
            
            subscribers.Add(subscriber);
        }

        Console.WriteLine($"? Created {subscribers.Count} notification subscribers");
        Console.WriteLine();

        // Start all subscribers
        foreach (var subscriber in subscribers)
        {
            await subscriber.StartConsumingAsync();
        }

        Console.WriteLine("? All notification services ready");
        Console.WriteLine();

        // Send various types of notifications
        Console.WriteLine("?? Broadcasting system notifications...");
        Console.WriteLine();

        var notifications = new[]
        {
            new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "INFO",
                Message = "System startup completed successfully",
                Timestamp = DateTime.UtcNow,
                Source = "SystemManager"
            },
            new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "SUCCESS",
                Message = "User registration: john.doe@example.com",
                Timestamp = DateTime.UtcNow,
                Source = "UserService"
            },
            new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "WARNING",
                Message = "High memory usage detected (85%)",
                Timestamp = DateTime.UtcNow,
                Source = "MonitoringService"
            },
            new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "ERROR",
                Message = "Payment processing failed for order #12345",
                Timestamp = DateTime.UtcNow,
                Source = "PaymentService"
            },
            new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "INFO",
                Message = "Daily backup completed: 2.3GB archived",
                Timestamp = DateTime.UtcNow,
                Source = "BackupService"
            }
        };

        foreach (var notification in notifications)
        {
            Console.WriteLine($"Publishing {notification.Type}: {notification.Message}");
            await publisher.PublishNotificationAsync(notification);
            await Task.Delay(1500);
            Console.WriteLine();
        }

        // Show statistics
        Console.WriteLine("?? Notification Service Statistics:");
        Console.WriteLine();

        foreach (var subscriber in subscribers)
        {
            await subscriber.StopConsumingAsync();
            var receivedNotifications = subscriber.GetReceivedNotifications();
            var stats = subscriber.GetStats();
            
            Console.WriteLine($"  {stats.SubscriberId}:");
            Console.WriteLine($"    - Total notifications: {receivedNotifications.Count}");
            
            if (receivedNotifications.Any())
            {
                var byType = receivedNotifications.GroupBy(n => n.Type);
                foreach (var group in byType)
                {
                    Console.WriteLine($"    - {group.Key}: {group.Count()}");
                }
            }
            Console.WriteLine();
        }

        // Cleanup
        foreach (var subscriber in subscribers)
        {
            subscriber.Dispose();
        }

        Console.WriteLine("? Notification demo completed!");
        Console.WriteLine();
    }

    /// <summary>
    /// Runs all demonstrations in sequence
    /// </summary>
    public async Task RunAllDemosAsync()
    {
        Console.Clear();
        Console.WriteLine("?? RabbitMQ Use Case 2: Publish-Subscribe Pattern Demonstrations");
        Console.WriteLine("================================================================");
        Console.WriteLine();

        try
        {
            await RunBasicPubSubDemoAsync();
            
            Console.WriteLine("Press any key to continue to Dynamic Subscription demo...");
            Console.ReadKey(true);
            Console.Clear();
            
            await RunDynamicSubscriptionDemoAsync();
            
            Console.WriteLine("Press any key to continue to Notification demo...");
            Console.ReadKey(true);
            Console.Clear();
            
            await RunNotificationDemoAsync();
            
            Console.WriteLine("?? All demonstrations completed successfully!");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Demo failed: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Please ensure RabbitMQ is running on localhost:5672");
            Console.WriteLine("Default credentials: admin/password");
        }
        finally
        {
            // Cleanup exchange
            await SafeDeleteExchange(DemoExchangeName);
        }
    }
}