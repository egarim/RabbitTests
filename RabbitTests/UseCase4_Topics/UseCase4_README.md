# Use Case 4: Topic-Based Routing (Topic Exchange)

## Overview
This implementation demonstrates the **Topic-Based Routing** messaging pattern using RabbitMQ's topic exchanges. In this pattern, messages are published with hierarchical routing keys and consumers use wildcard patterns to selectively receive messages, enabling flexible and powerful routing scenarios common in event-driven architectures and complex logging systems.

## Key Concepts Demonstrated

### 1. Topic Exchange Operations
- Topic exchange declaration and configuration
- Hierarchical routing keys (e.g., "logs.error.database")
- Pattern-based message routing using wildcards
- Exchange durability and message persistence

### 2. Wildcard Pattern Matching
- **Single-word wildcard (*)**: Matches exactly one word in routing key
- **Multi-word wildcard (#)**: Matches zero or more words in routing key
- **Complex patterns**: Combining wildcards with exact words
- **Pattern precedence**: Handling overlapping routing patterns

### 3. Hierarchical Message Routing
- **System.Level.Component**: Three-tier hierarchical routing
- **Event-driven architecture**: Microservice event routing
- **Selective subscription**: Fine-grained message filtering
- **Dynamic binding**: Adding/removing patterns at runtime

### 4. Advanced Routing Scenarios
- **Multiple patterns**: Single consumer with multiple binding patterns
- **Overlapping patterns**: Complex routing rules with pattern overlap
- **Pattern matching logic**: Custom pattern validation and routing
- **Performance optimization**: Efficient pattern matching algorithms

## Files Structure

```
RabbitTests/
??? UseCase4_Topics/
    ??? TopicPublisher.cs       # Publishes messages with hierarchical routing keys
    ??? TopicSubscriber.cs      # Subscribes using wildcard patterns
    ??? TopicTests.cs          # Comprehensive test suite
    ??? UseCase4_README.md     # This documentation
??? Infrastructure/
    ??? TestBase.cs            # Base test class with setup/teardown
    ??? RabbitMQConnection.cs  # Connection management utilities
    ??? MessageHelpers.cs      # Message serialization and utilities
```

## Architecture Overview

```mermaid
graph TB
    subgraph "Use Case 4: Topic-Based Routing Pattern"
        Publisher[TopicPublisher]
        Exchange{{Topic Exchange}}
        Queue1[(Pattern Queue 1)]
        Queue2[(Pattern Queue 2)]
        Queue3[(Pattern Queue 3)]
        Queue4[(Pattern Queue N)]
        
        Subscriber1[TopicSubscriber #1<br/>Pattern: logs.*]
        Subscriber2[TopicSubscriber #2<br/>Pattern: *.error.*]
        Subscriber3[TopicSubscriber #3<br/>Pattern: logs.#]
        SubscriberN[TopicSubscriber #N<br/>Pattern: user.service.*]
        
        Publisher -->|Hierarchical Routing Keys| Exchange
        Exchange -->|Pattern Matching| Queue1
        Exchange -->|Pattern Matching| Queue2
        Exchange -->|Pattern Matching| Queue3
        Exchange -->|Pattern Matching| Queue4
        
        Queue1 --> Subscriber1
        Queue2 --> Subscriber2
        Queue3 --> Subscriber3
        Queue4 --> SubscriberN
    end
    
    subgraph "Infrastructure"
        TestBase[TestBase]
        Connection[RabbitMQConnection]
        Helpers[MessageHelpers]
        
        TestBase -.->|Uses| Connection
        TestBase -.->|Uses| Helpers
        Publisher -.->|Inherits from| TestBase
        Subscriber1 -.->|Inherits from| TestBase
    end
```

## Message Flow Patterns

### Topic-Based Routing with Wildcards
```mermaid
sequenceDiagram
    participant P as TopicPublisher
    participant E as Topic Exchange
    participant S1 as Subscriber (logs.*)
    participant S2 as Subscriber (*.error.*)
    participant S3 as Subscriber (logs.#)
    
    P->>E: Publish "logs.info.web"
    P->>E: Publish "logs.error.database"
    P->>E: Publish "system.error.auth"
    
    E->>S1: Route "logs.info.web" (matches logs.*)
    E->>S1: Route "logs.error.database" (matches logs.*)
    
    E->>S2: Route "logs.error.database" (matches *.error.*)
    E->>S2: Route "system.error.auth" (matches *.error.*)
    
    E->>S3: Route "logs.info.web" (matches logs.#)
    E->>S3: Route "logs.error.database" (matches logs.#)
```

### Hierarchical Event Routing
```mermaid
sequenceDiagram
    participant P as TopicPublisher
    participant E as Topic Exchange
    participant U as User Service Subscriber
    participant O as Order Service Subscriber
    participant A as All Services Subscriber
    participant AU as Audit Subscriber
    
    P->>E: Publish "user.service.registration"
    P->>E: Publish "order.service.payment"
    P->>E: Publish "notification.service.email"
    
    E->>U: Route to User Service (user.service.*)
    E->>O: Route to Order Service (order.service.*)
    
    E->>A: Route to All Services (*.service.*)
    E->>A: Route to All Services (*.service.*)
    E->>A: Route to All Services (*.service.*)
    
    E->>AU: Route to Audit (#)
    E->>AU: Route to Audit (#)
    E->>AU: Route to Audit (#)
```

### Dynamic Pattern Management
```mermaid
graph LR
    subgraph "Pattern Evolution"
        A[Initial Pattern: logs.info.*] --> B[Add Pattern: logs.error.*]
        B --> C[Remove Pattern: logs.info.*]
        C --> D[Add Pattern: logs.#]
        D --> E[Final: logs.error.* + logs.#]
    end
```

## Class Relationships

```mermaid
classDiagram
    class TestBase {
        +RabbitMQConnection Connection
        +ILogger Logger
        +IChannel Channel
        +OneTimeSetUpAsync()
        +SetUp()
        +TearDown()
        +CleanupTestResources()
        +Dispose()
    }
    
    class TopicPublisher {
        -IChannel _channel
        -ILogger _logger
        -string _exchangeName
        +InitializeAsync(bool durable)
        +PublishMessageAsync(string routingKey, string message, bool persistent)
        +PublishEventMessageAsync(EventMessage event, bool persistent)
        +PublishTestLogMessagesAsync(int messagesPerPattern, bool persistent)
        +PublishTestMicroserviceEventsAsync(int messagesPerService, bool persistent)
        +PublishComplexPatternMessagesAsync(int messagesPerPattern, bool persistent)
        +Dispose()
    }
    
    class TopicSubscriber {
        -IChannel _channel
        -ILogger _logger
        -string _exchangeName
        -string _subscriberId
        +InitializeAsync(string[] bindingPatterns, bool useNamedQueue, bool exclusive, bool durable)
        +StartConsumingAsync(bool autoAck)
        +ConsumeMessagesAsync(int expectedCount, bool autoAck, TimeSpan timeout)
        +ConsumeEventMessagesAsync(int expectedCount, bool autoAck, TimeSpan timeout)
        +AddBindingPatternsAsync(string[] newPatterns)
        +RemoveBindingPatternsAsync(string[] patternsToRemove)
        +GetReceivedMessages()
        +GetEventsByPattern()
        +GetStats()
        +Dispose()
    }
    
    class EventMessage {
        +string Id
        +string System
        +string Level
        +string Component
        +string EventType
        +string Message
        +DateTime Timestamp
        +Dictionary Data
        +GetRoutingKey()
        +GetDisplayName()
    }
    
    class TopicSubscriberStats {
        +string SubscriberId
        +int MessagesReceived
        +DateTime StartTime
        +DateTime LastMessageTime
        +string[] BoundPatterns
        +string QueueName
    }
    
    class TopicTests {
        +WildcardRouting_SingleAndMultiWordWildcards_Should_MatchCorrectPatterns()
        +HierarchicalRouting_SystemLevelComponent_Should_RouteByHierarchy()
        +ComplexPatterns_MultipleOverlappingRules_Should_HandleComplexRouting()
        +EventDrivenArchitecture_MicroserviceEvents_Should_RouteToCorrectHandlers()
        +DynamicTopicPatterns_ChangePatternsAtRuntime_Should_UpdateRoutingBehavior()
        +TopicPersistence_DurableTopicExchange_Should_HandlePersistentMessages()
        +TopicStatistics_PatternMatchingAndCounting_Should_TrackCorrectly()
    }
    
    TestBase <|-- TopicTests
    TestBase ..> RabbitMQConnection : uses
    TestBase ..> MessageHelpers : uses
    TopicTests ..> TopicPublisher : creates
    TopicTests ..> TopicSubscriber : creates
    TopicPublisher ..> EventMessage : uses
    TopicSubscriber ..> EventMessage : uses
    TopicSubscriber ..> TopicSubscriberStats : uses
```

## Core Classes

### TopicPublisher
Responsible for publishing messages with hierarchical routing keys to topic exchanges.

**Key Features:**
- Initialize topic exchanges with different durability settings
- Publish messages with custom routing keys
- Support for structured EventMessage objects
- Complex pattern message publishing for testing
- Persistent message support

**Example Usage:**
```csharp
var publisher = new TopicPublisher(channel, logger, "my-topic-exchange");
await publisher.InitializeAsync(durable: false);

// Publish with simple routing key
await publisher.PublishMessageAsync("logs.error.database", "Database connection failed", false);

// Publish structured event
var eventMessage = new EventMessage
{
    System = "user",
    Level = "service", 
    Component = "authentication",
    EventType = "LoginFailed",
    Message = "Invalid credentials provided"
};
await publisher.PublishEventMessageAsync(eventMessage, persistent: true);
```

### TopicSubscriber
Consumes messages from topic exchanges using wildcard pattern matching.

**Key Features:**
- Configurable wildcard pattern bindings (* and # wildcards)
- Multiple pattern support per subscriber
- Dynamic pattern management (add/remove at runtime)
- Event-driven message processing
- Pattern-specific message tracking and statistics

**Example Usage:**
```csharp
var subscriber = new TopicSubscriber(channel, logger, "my-topic-exchange", "error-handler");

// Initialize with multiple patterns
await subscriber.InitializeAsync(new[] { "*.error.*", "logs.warning.#" });

// Consume specific number of messages
var messages = await subscriber.ConsumeMessagesAsync(10, autoAck: false);

// Add new patterns dynamically
await subscriber.AddBindingPatternsAsync(new[] { "system.critical.*" });

// Get statistics
var stats = subscriber.GetStats();
var messagesByPattern = subscriber.GetMessagesByPattern();
```

### EventMessage
Represents structured events with hierarchical routing support.

**Key Features:**
- System.Level.Component hierarchy
- Automatic routing key generation
- Metadata support with custom data
- Timestamp tracking
- Event type classification

## Test Scenarios

### 1. WildcardRouting_SingleAndMultiWordWildcards_Should_MatchCorrectPatterns
- **Purpose**: Verify wildcard pattern matching (* and #)
- **Test**: Publish log messages, verify pattern-based routing
- **Validation**: Correct messages reach appropriate subscribers

### 2. HierarchicalRouting_SystemLevelComponent_Should_RouteByHierarchy
- **Purpose**: Demonstrate microservice event routing
- **Test**: Route events by system.level.component hierarchy
- **Validation**: Events reach correct service handlers

### 3. ComplexPatterns_MultipleOverlappingRules_Should_HandleComplexRouting
- **Purpose**: Test complex overlapping routing patterns
- **Test**: Multiple subscribers with overlapping pattern rules
- **Validation**: Messages correctly routed to all matching patterns

### 4. EventDrivenArchitecture_MicroserviceEvents_Should_RouteToCorrectHandlers
- **Purpose**: Simulate real-world microservice communication
- **Test**: Route events between user, order, inventory, and audit services
- **Validation**: Event types and systems correctly filtered

### 5. DynamicTopicPatterns_ChangePatternsAtRuntime_Should_UpdateRoutingBehavior
- **Purpose**: Test runtime pattern management
- **Test**: Add/remove binding patterns during execution
- **Validation**: Routing behavior changes correctly

### 6. TopicPersistence_DurableTopicExchange_Should_HandlePersistentMessages
- **Purpose**: Verify persistence and durability features
- **Test**: Durable exchanges with persistent messages
- **Validation**: Messages survive and are processed correctly

### 7. TopicStatistics_PatternMatchingAndCounting_Should_TrackCorrectly
- **Purpose**: Validate pattern matching statistics
- **Test**: Track message counts per pattern
- **Validation**: Statistics accurately reflect message distribution

## Usage Examples

### Basic Topic Routing
```csharp
// Create publisher
var publisher = new TopicPublisher(channel, logger, "logs-exchange");
await publisher.InitializeAsync();

// Create subscriber for error logs
var errorSubscriber = new TopicSubscriber(channel, logger, "logs-exchange", "error-handler");
await errorSubscriber.InitializeAsync(new[] { "*.error.*" });

// Publish messages
await publisher.PublishMessageAsync("web.error.timeout", "Request timeout occurred");
await publisher.PublishMessageAsync("db.error.connection", "Database connection failed");
await publisher.PublishMessageAsync("web.info.startup", "Web server started"); // Won't match

// Consume matching messages
var errorMessages = await errorSubscriber.ConsumeMessagesAsync(2);
```

### Microservice Event Routing
```csharp
// Create event publisher
var eventPublisher = new TopicPublisher(channel, logger, "microservices-events");
await eventPublisher.InitializeAsync();

// Create service-specific subscribers
var userServiceSubscriber = new TopicSubscriber(channel, logger, "microservices-events", "user-service");
var orderServiceSubscriber = new TopicSubscriber(channel, logger, "microservices-events", "order-service");
var auditSubscriber = new TopicSubscriber(channel, logger, "microservices-events", "audit-service");

await userServiceSubscriber.InitializeAsync(new[] { "user.#" });
await orderServiceSubscriber.InitializeAsync(new[] { "order.#" });
await auditSubscriber.InitializeAsync(new[] { "#" }); // All events

// Publish microservice events
await eventPublisher.PublishTestMicroserviceEventsAsync(messagesPerService: 5);

// Services automatically receive relevant events
```

### Dynamic Pattern Management
```csharp
// Start with basic pattern
var dynamicSubscriber = new TopicSubscriber(channel, logger, "logs", "dynamic-handler");
await dynamicSubscriber.InitializeAsync(new[] { "logs.info.*" });

// Later add error handling
await dynamicSubscriber.AddBindingPatternsAsync(new[] { "logs.error.*", "logs.warning.*" });

// Remove info pattern, keep error/warning
await dynamicSubscriber.RemoveBindingPatternsAsync(new[] { "logs.info.*" });

// Add catch-all for debugging
await dynamicSubscriber.AddBindingPatternsAsync(new[] { "logs.debug.#" });
```

## Configuration Options

### Publisher Configuration
- **Exchange Name**: Custom topic exchange name
- **Durability**: Whether the exchange survives server restarts
- **Persistence**: Whether messages are saved to disk
- **Routing Key Strategy**: Custom hierarchical routing key patterns

### Subscriber Configuration
- **Subscriber ID**: Unique identifier for the subscriber
- **Binding Patterns**: Array of wildcard patterns to subscribe to
- **Queue Type**: Named vs temporary, exclusive vs shared
- **Acknowledgment Mode**: Auto vs manual message acknowledgment
- **QoS Settings**: Prefetch count for fair message distribution

## Pattern Matching Rules

### Single-word Wildcard (*)
- Matches exactly one word in the routing key
- `logs.*.web` matches: `logs.error.web`, `logs.info.web`
- Does not match: `logs.web`, `logs.error.web.server`

### Multi-word Wildcard (#)
- Matches zero or more words in the routing key
- `logs.#` matches: `logs`, `logs.error`, `logs.error.web.server`
- `user.service.#` matches: `user.service.auth`, `user.service.auth.login.success`

### Combined Patterns
- `*.error.#` matches: `web.error.timeout`, `db.error.connection.lost`
- `logs.*.database` matches: `logs.error.database`, `logs.warning.database`

## Best Practices Demonstrated

1. **Hierarchical Design**: Use consistent System.Level.Component routing keys
2. **Pattern Strategy**: Design patterns for overlap and specificity balance
3. **Resource Management**: Proper disposal of publishers and subscribers
4. **Error Handling**: Graceful handling of pattern mismatches and failures
5. **Performance**: Efficient pattern matching and message routing
6. **Monitoring**: Statistics tracking for pattern effectiveness
7. **Flexibility**: Dynamic pattern management for changing requirements

## Running the Tests

```bash
# Run all Use Case 4 tests
dotnet test --filter "TestFixture=TopicTests"

# Run specific test
dotnet test --filter "TestMethod=WildcardRouting_SingleAndMultiWordWildcards_Should_MatchCorrectPatterns"

# Run with detailed output
dotnet test --filter "FullyQualifiedName~UseCase4_Topics" --verbosity normal
```

## Prerequisites

- RabbitMQ server running on localhost:5672
- Admin user credentials (admin/password)
- .NET 9 runtime
- RabbitMQ.Client NuGet package

## Performance Considerations

- **Pattern Complexity**: Simple patterns perform better than complex overlapping ones
- **Exchange Management**: Topic exchanges are optimized for pattern matching
- **Queue Strategy**: Use exclusive queues for single consumers, shared for load balancing
- **Memory Usage**: Monitor pattern count and message retention
- **Connection Sharing**: Reuse connections across multiple publishers/subscribers
- **Binding Management**: Minimize binding changes during high message throughput

## Real-World Applications

- **Logging Systems**: Route logs by severity, component, and system
- **Event-Driven Architecture**: Microservice communication and event handling
- **Monitoring**: Route metrics and alerts by system and criticality
- **Notification Systems**: Selective notification delivery based on user preferences
- **Data Processing**: Route data streams to appropriate processing pipelines

This implementation provides a comprehensive foundation for understanding RabbitMQ topic exchanges and serves as a robust starting point for complex event-driven messaging architectures.