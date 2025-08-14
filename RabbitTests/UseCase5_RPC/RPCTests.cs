using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;
using RabbitTests.UseCase5_RPC;

namespace RabbitTests.UseCase5_RPC;

/// <summary>
/// Comprehensive test suite for RPC (Request-Reply) pattern
/// Tests synchronous communication using RabbitMQ with correlation IDs
/// </summary>
[TestFixture]
public class RPCTests : TestBase
{
    private const string TestQueueName = "test-rpc-queue";

    [Test]
    public async Task BasicRPC_Should_SendRequestAndReceiveResponse()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "test-server-1");
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "test-client-1");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();

        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        // Act
        var request = RPCRequestFactory.CreateEchoRequest("Hello, RPC!", "test-client-1");
        var response = await client.SendRequestAsync(request, TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Result?.ToString(), Is.EqualTo("Echo: Hello, RPC!"));
        Assert.That(response.ServerId, Is.EqualTo("test-server-1"));
        Assert.That(response.ProcessingTime, Is.GreaterThan(TimeSpan.Zero));

        Logger.LogInformation("Basic RPC test completed successfully");
    }

    [Test]
    public async Task MultipleClients_Should_HandleConcurrentRequests()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "test-server-multi");
        await server.InitializeAsync(durable: false);
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 5);

        var clients = new List<RPCClient>();
        var clientTasks = new List<Task<RPCResponse>>();

        try
        {
            // Create multiple clients
            for (int i = 1; i <= 3; i++)
            {
                var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, $"client-{i}");
                await client.InitializeAsync();
                clients.Add(client);
            }

            // Act - Send concurrent requests from multiple clients
            for (int i = 0; i < clients.Count; i++)
            {
                var clientIndex = i;
                var client = clients[clientIndex];
                
                for (int j = 1; j <= 2; j++)
                {
                    var request = RPCRequestFactory.CreateCalculationRequest("add", 10 + clientIndex, j, $"client-{clientIndex + 1}");
                    clientTasks.Add(client.SendRequestAsync(request, TimeSpan.FromSeconds(10)));
                }
            }

            var responses = await Task.WhenAll(clientTasks);

            // Assert
            Assert.That(responses.Length, Is.EqualTo(6)); // 3 clients * 2 requests each
            Assert.That(responses.All(r => r.Success), Is.True);
            Assert.That(responses.All(r => r.ServerId == "test-server-multi"), Is.True);

            // Verify calculations are correct
            var calculationResponses = responses.Where(r => r.Result != null).ToList();
            Assert.That(calculationResponses.Count, Is.EqualTo(6));

            Logger.LogInformation("Multiple clients test completed with {ResponseCount} responses", responses.Length);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Test]
    public async Task TimeoutHandling_Should_ThrowTimeoutException()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "test-server-timeout");
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "test-client-timeout");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        // Act & Assert
        var request = RPCRequestFactory.CreateDelayRequest(3000, "test-client-timeout"); // 3 second delay
        var timeout = TimeSpan.FromSeconds(1); // 1 second timeout

        var ex = Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.SendRequestAsync(request, timeout));

        Assert.That(ex?.Message, Does.Contain("timed out"));
        Assert.That(client.PendingRequestCount, Is.EqualTo(0)); // Should be cleaned up

        Logger.LogInformation("Timeout handling test completed successfully");
    }

    [Test]
    public async Task ErrorHandling_Should_ReturnErrorResponse()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "test-server-error");
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "test-client-error");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        // Act
        var request = RPCRequestFactory.CreateErrorRequest("Test error message", "test-client-error");
        var response = await client.SendRequestAsync(request, TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Is.EqualTo("Test error message"));
        Assert.That(response.ServerId, Is.EqualTo("test-server-error"));

        Logger.LogInformation("Error handling test completed successfully");
    }

    [Test]
    public async Task LoadBalancing_Should_DistributeRequestsAmongServers()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var servers = new List<RPCServer>();
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "test-client-lb");

        try
        {
            // Create multiple servers
            for (int i = 1; i <= 3; i++)
            {
                var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, $"server-{i}");
                await server.InitializeAsync(durable: false);
                await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);
                servers.Add(server);
            }

            await client.InitializeAsync();

            // Act - Send multiple requests
            var requestTasks = new List<Task<RPCResponse>>();
            for (int i = 1; i <= 9; i++)
            {
                var request = RPCRequestFactory.CreateEchoRequest($"Message {i}", "test-client-lb");
                requestTasks.Add(client.SendRequestAsync(request, TimeSpan.FromSeconds(10)));
            }

            var responses = await Task.WhenAll(requestTasks);

            // Assert
            Assert.That(responses.Length, Is.EqualTo(9));
            Assert.That(responses.All(r => r.Success), Is.True);

            // Verify load balancing - each server should have processed some requests
            var serverIds = responses.Select(r => r.ServerId).Distinct().ToList();
            Assert.That(serverIds.Count, Is.EqualTo(3));
            Assert.That(serverIds, Does.Contain("server-1"));
            Assert.That(serverIds, Does.Contain("server-2"));
            Assert.That(serverIds, Does.Contain("server-3"));

            // Check server statistics
            foreach (var server in servers)
            {
                var stats = server.GetStats();
                Logger.LogInformation("Server {ServerId}: Processed {ProcessedCount} requests", 
                    stats.ServerId, stats.RequestsProcessed);
                Assert.That(stats.RequestsProcessed, Is.GreaterThan(0));
            }

            Logger.LogInformation("Load balancing test completed successfully");
        }
        finally
        {
            foreach (var server in servers)
            {
                server.Dispose();
            }
        }
    }

    [Test]
    public async Task ComplexCalculations_Should_ProcessCorrectly()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "calc-server");
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "calc-client");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        // Act & Assert - Test various calculations
        var testCases = new[]
        {
            (operation: "add", a: 10.5, b: 5.3, expected: 15.8),
            (operation: "subtract", a: 20.0, b: 8.5, expected: 11.5),
            (operation: "multiply", a: 4.0, b: 2.5, expected: 10.0),
            (operation: "divide", a: 15.0, b: 3.0, expected: 5.0)
        };

        foreach (var testCase in testCases)
        {
            var request = RPCRequestFactory.CreateCalculationRequest(testCase.operation, testCase.a, testCase.b, "calc-client");
            var response = await client.SendRequestAsync(request, TimeSpan.FromSeconds(5));

            Assert.That(response.Success, Is.True, $"Operation {testCase.operation} failed");
            Assert.That(Convert.ToDouble(response.Result), Is.EqualTo(testCase.expected).Within(0.01), 
                $"Operation {testCase.operation} returned incorrect result");
        }

        // Test division by zero
        var divideByZeroRequest = RPCRequestFactory.CreateCalculationRequest("divide", 10, 0, "calc-client");
        var divideByZeroResponse = await client.SendRequestAsync(divideByZeroRequest, TimeSpan.FromSeconds(5));
        
        Assert.That(divideByZeroResponse.Success, Is.False);
        Assert.That(divideByZeroResponse.ErrorMessage, Does.Contain("divide by zero"));

        Logger.LogInformation("Complex calculations test completed successfully");
    }

    [Test]
    public async Task DataProcessing_Should_HandleListOperations()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "data-server");
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "data-client");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        var testData = new List<int> { 1, 2, 3, 4, 5, 10, 15, 20 };

        // Act & Assert - Test various data operations
        var operations = new[]
        {
            (operation: "sum", expected: 60.0),
            (operation: "average", expected: 7.5),
            (operation: "max", expected: 20.0),
            (operation: "min", expected: 1.0),
            (operation: "count", expected: 8.0)
        };

        foreach (var op in operations)
        {
            var request = RPCRequestFactory.CreateDataProcessingRequest(testData, op.operation, "data-client");
            var response = await client.SendRequestAsync(request, TimeSpan.FromSeconds(5));

            Assert.That(response.Success, Is.True, $"Operation {op.operation} failed: {response.ErrorMessage}");
            Assert.That(Convert.ToDouble(response.Result), Is.EqualTo(op.expected).Within(0.01), 
                $"Operation {op.operation} returned incorrect result");
        }

        Logger.LogInformation("Data processing test completed successfully");
    }

    [Test]
    public async Task ConcurrentRequests_Should_HandleHighLoad()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "load-server");
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "load-client");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 10);

        // Act - Send many concurrent requests
        var requestCount = 50;
        var requests = new List<RPCRequest>();
        
        for (int i = 1; i <= requestCount; i++)
        {
            requests.Add(RPCRequestFactory.CreateCalculationRequest("add", i, i * 2, "load-client"));
        }

        var startTime = DateTime.UtcNow;
        var responses = await client.SendConcurrentRequestsAsync(requests, TimeSpan.FromSeconds(30));
        var endTime = DateTime.UtcNow;

        // Assert
        Assert.That(responses.Count, Is.EqualTo(requestCount));
        Assert.That(responses.All(r => r.Success), Is.True);

        var duration = endTime - startTime;
        var requestsPerSecond = requestCount / duration.TotalSeconds;

        Logger.LogInformation("High load test: {RequestCount} requests in {Duration:F2}s ({RequestsPerSecond:F2} req/s)", 
            requestCount, duration.TotalSeconds, requestsPerSecond);

        // Verify server statistics
        var stats = server.GetStats();
        Assert.That(stats.RequestsProcessed, Is.EqualTo(requestCount));
        Assert.That(stats.RequestsFailed, Is.EqualTo(0));

        Logger.LogInformation("High load test completed successfully");
    }

    [Test]
    public async Task ServerRecovery_Should_HandleServerRestart()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var client = new RPCClient(Channel, loggerFactory.CreateLogger<RPCClient>(), TestQueueName, "recovery-client");
        await client.InitializeAsync();

        // Act & Assert - Test with server starting after client
        using var server = new RPCServer(Channel, loggerFactory.CreateLogger<RPCServer>(), TestQueueName, "recovery-server");
        await server.InitializeAsync(durable: false);
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        // Send request after server is running
        var request = RPCRequestFactory.CreateEchoRequest("Recovery test", "recovery-client");
        var response = await client.SendRequestAsync(request, TimeSpan.FromSeconds(5));

        Assert.That(response.Success, Is.True);
        Assert.That(response.Result?.ToString(), Is.EqualTo("Echo: Recovery test"));

        Logger.LogInformation("Server recovery test completed successfully");
    }
}