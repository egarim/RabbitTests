# Use Case 1: Simple Producer-Consumer Pattern (Work Queue)

## Overview
This implementation demonstrates the most basic RabbitMQ pattern where messages are sent to a queue and consumed by workers. This is also known as the **Work Queue** or **Task Queue** pattern.

## Key Concepts Demonstrated

### 1. Basic Queue Operations
- Queue declaration with different durability settings
- Message publishing to queues
- Message consumption from queues
- Proper resource cleanup

### 2. Message Distribution
- **Round-robin distribution**: Messages are distributed evenly among multiple consumers
- **Fair dispatch**: Using QoS (prefetchCount) to control message distribution
- **Load balancing**: Multiple workers processing from the same queue

### 3. Message Acknowledgment
- **Auto-acknowledgment**: Messages are acknowledged automatically when delivered
- **Manual acknowledgment**: Consumer explicitly acknowledges message processing
- **Message rejection**: Handling failed messages with requeue option

### 4. Message Persistence
- **Durable queues**: Queues that survive server restarts
- **Persistent messages**: Messages that are saved to disk
- **Temporary queues**: Auto-delete queues for testing

## Files Structure

```
RabbitTests/
??? UseCase1_WorkQueue/
?   ??? WorkQueueProducer.cs    # Publishes messages to work queue
?   ??? WorkQueueConsumer.cs    # Consumes and processes messages
?   ??? WorkQueueTests.cs       # Comprehensive test suite
?   ??? WorkQueueDemo.cs        # Interactive demonstrations
?   ??? RabbitMQConnection.cs   # Connection management utilities
?   ??? MessageHelpers.cs       # Message serialization and utilities
?   ??? UseCase1_README.md      # This documentation
??? Infrastructure/
    ??? TestBase.cs              # Base test class with setup/teardown
```

## Architecture Overview

```mermaid
graph TB
    subgraph "Use Case 1: Work Queue Pattern"
        Producer[WorkQueueProducer]
        Queue[(Work Queue)]
        Consumer1[WorkQueueConsumer #1]
        Consumer2[WorkQueueConsumer #2]
        Consumer3[WorkQueueConsumer #3]
        
        Producer -->|Publish Messages| Queue
        Queue -->|Round-Robin Distribution| Consumer1
        Queue -->|Round-Robin Distribution| Consumer2
        Queue -->|Round-Robin Distribution| Consumer3
        
        Consumer1 -->|Acknowledgment| Queue
        Consumer2 -->|Acknowledgment| Queue
        Consumer3 -->|Acknowledgment| Queue
    end
    
    subgraph "Infrastructure"
        TestBase[TestBase]
        Connection[RabbitMQConnection]
        Helpers[MessageHelpers]
        
        TestBase -.->|Uses| Connection
        TestBase -.->|Uses| Helpers
        Producer -.->|Inherits from| TestBase
        Consumer1 -.->|Inherits from| TestBase
    end
```

## Message Flow Patterns

### Single Producer - Single Consumer
```mermaid
sequenceDiagram
    participant P as Producer
    participant Q as Work Queue
    participant C as Consumer
    
    P->>Q: Publish Message 1
    P->>Q: Publish Message 2
    P->>Q: Publish Message 3
    
    Q->>C: Deliver Message 1
    C->>Q: Acknowledge Message 1
    
    Q->>C: Deliver Message 2
    C->>Q: Acknowledge Message 2
    
    Q->>C: Deliver Message 3
    C->>Q: Acknowledge Message 3
```

### Single Producer - Multiple Consumers (Round-Robin)
```mermaid
sequenceDiagram
    participant P as Producer
    participant Q as Work Queue
    participant C1 as Consumer 1
    participant C2 as Consumer 2
    participant C3 as Consumer 3
    
    P->>Q: Publish Messages 1-6
    
    Q->>C1: Deliver Message 1
    Q->>C2: Deliver Message 2
    Q->>C3: Deliver Message 3
    Q->>C1: Deliver Message 4
    Q->>C2: Deliver Message 5
    Q->>C3: Deliver Message 6
    
    C1->>Q: Acknowledge Message 1
    C2->>Q: Acknowledge Message 2
    C3->>Q: Acknowledge Message 3
    C1->>Q: Acknowledge Message 4
    C2->>Q: Acknowledge Message 5
    C3->>Q: Acknowledge Message 6
```

### Message Acknowledgment Flow
```mermaid
graph LR
    subgraph "Message Lifecycle"
        A[Message Published] --> B[Message Queued]
        B --> C[Message Delivered]
        C --> D{Processing Result}
        D -->|Success| E[Manual ACK]
        D -->|Failure| F[NACK/Reject]
        E --> G[Message Removed]
        F --> H{Requeue?}
        H -->|Yes| B
        H -->|No| I[Message Discarded]
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
    
    class WorkQueueProducer {
        -IChannel _channel
        -ILogger _logger
        -string _queueName
        +InitializeAsync(bool durable)
        +PublishWorkItemAsync(WorkItem, bool persistent)
        +PublishTextMessageAsync(string, bool persistent)
        +PublishTestWorkItemsAsync(int count)
        +Dispose()
    }
    
    class WorkQueueConsumer {
        -IChannel _channel
        -ILogger _logger
        -string _queueName
        -string _consumerId
        +StartConsumingAsync(bool autoAck, ushort prefetchCount)
        +ConsumeMessagesAsync(int count, bool autoAck)
        +StopConsumingAsync()
        +Dispose()
    }
    
    class RabbitMQConnection {
        -IConnection _connection
        -ILogger _logger
        +CreateChannel()
        +TestConnectionAsync()
        +Dispose()
    }
    
    class MessageHelpers {
        +SerializeMessage(WorkItem)
        +DeserializeMessage(byte[])
        +CreateTextMessage(string)
        +CreateWorkItem(string, int processingTime)
    }
    
    class WorkQueueTests {
        +SingleProducerSingleConsumer_Should_ProcessAllMessages()
        +SingleProducerMultipleConsumers_Should_DistributeMessagesRoundRobin()
        +MessageAcknowledgment_Should_HandleManualAck()
        +MessagePersistence_Should_SurviveServerRestart()
    }
    
    TestBase <|-- WorkQueueTests
    TestBase ..> RabbitMQConnection : uses
    TestBase ..> MessageHelpers : uses
    WorkQueueTests ..> WorkQueueProducer : creates
    WorkQueueTests ..> WorkQueueConsumer : creates
    WorkQueueProducer ..> MessageHelpers : uses
    WorkQueueConsumer ..> MessageHelpers : uses
```

## Core Classes

### WorkQueueProducer
Responsible for publishing messages to the work queue.

**Key Features:**
- Initialize queues with different durability settings
- Publish individual messages or batches
- Support for persistent messages
- Text messages and structured WorkItem objects

**Example Usage:**
```csharp
var producer = new WorkQueueProducer(channel, logger, "my-work-queue");
await producer.InitializeAsync(durable: false);
await producer.PublishTestMessagesAsync(10, persistent: false);
```

### WorkQueueConsumer
Consumes and processes messages from the work queue.

**Key Features:**
- Configurable acknowledgment modes (auto/manual)
- QoS control for fair message distribution
- Processing statistics and monitoring
- Event-driven message processing
- Graceful shutdown and cleanup

**Example Usage:**
```csharp
var consumer = new WorkQueueConsumer(channel, logger, "my-work-queue", "worker-1");
await consumer.StartConsumingAsync(autoAck: false, prefetchCount: 1);

// Process specific number of messages
var messages = await consumer.ConsumeMessagesAsync(5, autoAck: false);
```

## Test Scenarios

### 1. SingleProducerSingleConsumer
- **Purpose**: Verify basic message flow
- **Test**: Send 10 messages, consume all with one consumer
- **Validation**: All messages are received and processed

### 2. SingleProducerMultipleConsumers  
- **Purpose**: Demonstrate round-robin distribution
- **Test**: Send 20 messages, consume with 3 consumers
- **Validation**: Messages are distributed evenly among consumers

### 3. MessageAcknowledgment
- **Purpose**: Test acknowledgment mechanisms
- **Test**: Compare manual vs auto acknowledgment
- **Validation**: Messages are acknowledged correctly

### 4. MessagePersistence
- **Purpose**: Verify persistence and durability
- **Test**: Durable queues with persistent messages
- **Validation**: Messages survive and are processed correctly

### 5. WorkItem Processing
- **Purpose**: Simulate real workload scenarios
- **Test**: Process structured work items with timing
- **Validation**: Processing time and statistics are accurate

## Usage Examples

### Basic Producer-Consumer
```csharp
// Create producer
var producer = new WorkQueueProducer(channel, logger);
await producer.InitializeAsync();

// Create consumer
var consumer = new WorkQueueConsumer(channel, logger);
consumer.OnMessageProcessed += (message, workItem) => {
    Console.WriteLine($"Processed: {message}");
};

// Publish messages
await producer.PublishTestMessagesAsync(5);

// Start consuming
await consumer.StartConsumingAsync(autoAck: true);
```

### Multiple Workers
```csharp
// Create multiple consumers
var consumers = new List<WorkQueueConsumer>();
for (int i = 1; i <= 3; i++)
{
    var consumer = new WorkQueueConsumer(channel, logger, "work-queue", $"worker-{i}");
    consumers.Add(consumer);
    await consumer.StartConsumingAsync(autoAck: false, prefetchCount: 1);
}

// Publish work
await producer.PublishTestWorkItemsAsync(15);

// Work is automatically distributed among consumers
```

### Persistent Messages
```csharp
// Create durable queue
await producer.InitializeAsync(durable: true);

// Publish persistent messages
var workItems = new[] {
    MessageHelpers.CreateWorkItem("Critical Task 1"),
    MessageHelpers.CreateWorkItem("Critical Task 2")
};
await producer.PublishWorkItemsAsync(workItems, persistent: true);

// Messages will survive server restarts
```

## Configuration Options

### Producer Configuration
- **Queue Name**: Custom queue name for the work queue
- **Durability**: Whether the queue survives server restarts
- **Persistence**: Whether messages are saved to disk
- **Batch Size**: Number of messages to publish at once

### Consumer Configuration  
- **Consumer ID**: Unique identifier for the consumer
- **Auto Acknowledgment**: Automatic vs manual message acknowledgment
- **Prefetch Count**: Number of unacknowledged messages per consumer
- **Processing Timeout**: Maximum time to wait for messages

## Best Practices Demonstrated

1. **Resource Management**: Proper disposal of connections and channels
2. **Error Handling**: Graceful handling of connection failures and processing errors
3. **Logging**: Comprehensive logging for debugging and monitoring
4. **Testing**: Thorough test coverage with different scenarios
5. **Performance**: Efficient message processing with QoS controls
6. **Reliability**: Message acknowledgment and persistence for critical scenarios

## Running the Tests

```bash
# Run all Use Case 1 tests
dotnet test --filter "TestFixture=WorkQueueTests"

# Run specific test
dotnet test --filter "TestMethod=SingleProducerSingleConsumer_Should_ProcessAllMessages"
```

## Prerequisites

- RabbitMQ server running on localhost:5672
- Admin user credentials (admin/password)
- .NET 9 runtime
- RabbitMQ.Client NuGet package

## Performance Considerations

- **Connection Pooling**: Reuse connections across multiple operations
- **Channel Management**: Use separate channels for producers and consumers  
- **Batch Processing**: Group multiple messages for better throughput
- **QoS Settings**: Configure prefetch count based on processing capacity
- **Memory Usage**: Monitor queue depths and consumer processing rates

This implementation provides a solid foundation for understanding RabbitMQ work queues and serves as a starting point for more complex messaging patterns.