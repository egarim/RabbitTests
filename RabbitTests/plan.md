# RabbitMQ Test Cases Plan - Top 5 Common Use Cases

## Overview
This document outlines the top 5 most common RabbitMQ use cases that will be implemented as test cases in the RabbitTests project. Each use case demonstrates different messaging patterns and scenarios that developers commonly encounter when working with RabbitMQ.

**Connection Details:**
- Management UI: http://localhost:15672
- AMQP Connection: localhost:5672
- Username: admin
- Password: password

---

## Use Case 1: Simple Producer-Consumer Pattern (Work Queue)
**Description:** The most basic RabbitMQ pattern where messages are sent to a queue and consumed by workers.

### Scenario:
- A producer sends tasks/messages to a named queue
- One or more consumers process messages from the queue
- Messages are distributed round-robin among consumers
- Demonstrates basic message acknowledgment

### Test Cases to Implement:
1. **SingleProducerSingleConsumer**: Send 10 messages, consume all with one consumer
2. **SingleProducerMultipleConsumers**: Send 20 messages, consume with 3 consumers (round-robin distribution)
3. **MessageAcknowledgment**: Test manual acknowledgment vs auto-acknowledgment
4. **MessagePersistence**: Test durable queues and persistent messages

### Key Learning Points:
- Basic queue creation and binding
- Message publishing and consuming
- Worker queue distribution
- Message acknowledgment patterns
- Queue and message durability

---

## Use Case 2: Publish-Subscribe Pattern (Fanout Exchange)
**Description:** Broadcasting messages to multiple consumers simultaneously using fanout exchanges.

### Scenario:
- A publisher sends messages to a fanout exchange
- Multiple subscribers receive copies of every message
- Each subscriber has its own queue bound to the exchange
- Useful for notifications, logging, and real-time updates

### Test Cases to Implement:
1. **BasicFanout**: One publisher, three subscribers, each receives all messages
2. **DynamicSubscribers**: Add/remove subscribers during message publishing
3. **TemporaryQueues**: Use exclusive, auto-delete queues for subscribers
4. **BroadcastNotifications**: Simulate system-wide notifications

### Key Learning Points:
- Fanout exchange configuration
- Queue binding to exchanges
- Multiple queue consumption
- Temporary and exclusive queues
- Broadcasting vs point-to-point messaging

---

## Use Case 3: Routing Pattern (Direct Exchange)
**Description:** Selective message routing based on routing keys using direct exchanges.

### Scenario:
- Messages are published with specific routing keys
- Consumers bind their queues with matching routing keys
- Only messages with matching routing keys are delivered
- Enables selective message processing based on criteria

### Test Cases to Implement:
1. **BasicRouting**: Route messages by severity levels (info, warning, error)
2. **MultipleBindings**: One queue bound to multiple routing keys
3. **SelectiveConsumption**: Consumers that only process specific message types
4. **DynamicRouting**: Change routing keys and bindings during runtime

### Key Learning Points:
- Direct exchange configuration
- Routing key usage
- Selective message filtering
- Multiple bindings per queue
- Dynamic routing scenarios

---

## Use Case 4: Topic-Based Routing (Topic Exchange)
**Description:** Advanced routing using pattern matching with topic exchanges and wildcard routing keys.

### Scenario:
- Publishers use hierarchical routing keys (e.g., "logs.error.database")
- Consumers use wildcard patterns (* for one word, # for multiple words)
- Enables complex routing scenarios
- Common in logging systems and event-driven architectures

### Test Cases to Implement:
1. **WildcardRouting**: Use * and # wildcards for pattern matching
2. **HierarchicalRouting**: Implement log routing by system.level.component
3. **ComplexPatterns**: Multiple overlapping patterns and routing rules
4. **EventDrivenArchitecture**: Simulate microservice event routing

### Key Learning Points:
- Topic exchange configuration
- Wildcard pattern matching
- Hierarchical routing keys
- Complex routing scenarios
- Event-driven messaging patterns

---

## Use Case 5: Request-Reply Pattern (RPC)
**Description:** Synchronous communication pattern using RabbitMQ for Remote Procedure Call (RPC) scenarios.

### Scenario:
- Client sends request messages and waits for responses
- Server processes requests and sends replies
- Uses correlation IDs to match requests with responses
- Implements timeout handling for reliability

### Test Cases to Implement:
1. **BasicRPC**: Simple request-response with correlation ID
2. **MultipleClients**: Handle concurrent RPC calls from multiple clients
3. **TimeoutHandling**: Implement client-side timeouts for unresponsive servers
4. **ErrorHandling**: Handle server errors and invalid requests
5. **LoadBalancing**: Multiple RPC servers processing requests

### Key Learning Points:
- Correlation ID usage
- Reply-to queue patterns
- Temporary exclusive queues
- Timeout and error handling
- Synchronous messaging over async infrastructure

---

## Implementation Structure

### Project Organization:
```
RabbitTests/
??? Infrastructure/
?   ??? RabbitMQConnection.cs      # Connection management
?   ??? TestBase.cs                # Base test class with setup/teardown
?   ??? MessageHelpers.cs          # Utility classes for testing
??? UseCase1_WorkQueue/
?   ??? WorkQueueProducer.cs
?   ??? WorkQueueConsumer.cs
?   ??? WorkQueueTests.cs
??? UseCase2_PublishSubscribe/
?   ??? PublisherService.cs
?   ??? SubscriberService.cs
?   ??? PublishSubscribeTests.cs
??? UseCase3_Routing/
?   ??? RoutingProducer.cs
?   ??? RoutingConsumer.cs
?   ??? RoutingTests.cs
??? UseCase4_Topics/
?   ??? TopicPublisher.cs
?   ??? TopicSubscriber.cs
?   ??? TopicTests.cs
??? UseCase5_RPC/
    ??? RPCClient.cs
    ??? RPCServer.cs
    ??? RPCTests.cs
```

### Dependencies Required:
- **RabbitMQ.Client**: Official .NET client library
- **Microsoft.Extensions.Logging**: For logging in tests
- **Microsoft.Extensions.DependencyInjection**: For dependency injection
- **System.Text.Json**: For message serialization

### Test Infrastructure:
- **Base Test Class**: Common setup/teardown, connection management
- **Test Helpers**: Message serialization, queue cleanup, assertion helpers
- **Configuration**: Connection strings, test timeouts, retry policies
- **Logging**: Comprehensive logging for debugging and monitoring

### Performance Considerations:
- Connection pooling for multiple test scenarios
- Proper resource cleanup after each test
- Queue and exchange cleanup between tests
- Message size limitations and testing
- Concurrent test execution safety

---

## Expected Outcomes

After implementing these test cases, developers will understand:

1. **Basic messaging patterns** and when to use each
2. **Exchange types** and their appropriate use cases
3. **Message routing strategies** from simple to complex
4. **Reliability patterns** including acknowledgments and persistence
5. **Performance considerations** for different messaging scenarios
6. **Error handling** and failure recovery patterns
7. **Testing strategies** for message-based systems

Each use case builds upon the previous ones, creating a comprehensive learning path for RabbitMQ development in .NET environments.