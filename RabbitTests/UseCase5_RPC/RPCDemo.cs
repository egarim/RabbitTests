using Microsoft.Extensions.Logging;
using RabbitTests.Infrastructure;
using RabbitTests.UseCase5_RPC;

namespace RabbitTests.UseCase5_RPC;

/// <summary>
/// Interactive demonstration of RPC (Request-Reply) pattern
/// Shows practical examples of synchronous communication over RabbitMQ
/// </summary>
public class RPCDemo
{
    private readonly ILogger<RPCDemo> _logger;
    private readonly RabbitMQConnection _connection;

    public RPCDemo(ILogger<RPCDemo> logger, RabbitMQConnection connection)
    {
        _logger = logger;
        _connection = connection;
    }

    /// <summary>
    /// Demonstrates basic RPC communication
    /// </summary>
    public async Task BasicRPCDemo()
    {
        _logger.LogInformation("=== Basic RPC Demo ===");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        // Create server and client
        using var server = new RPCServer(channel, loggerFactory.CreateLogger<RPCServer>(), "demo-rpc-queue", "demo-server");
        using var client = new RPCClient(channel, loggerFactory.CreateLogger<RPCClient>(), "demo-rpc-queue", "demo-client");

        // Initialize
        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();

        // Start server with default processor
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        _logger.LogInformation("Server started. Sending echo request...");

        // Send echo request
        var echoRequest = RPCRequestFactory.CreateEchoRequest("Hello from RPC Demo!");
        var echoResponse = await client.SendRequestAsync(echoRequest, TimeSpan.FromSeconds(5));

        _logger.LogInformation("Echo Response: {Response}", echoResponse.Result);

        // Send calculation request
        var calcRequest = RPCRequestFactory.CreateCalculationRequest("multiply", 15, 3);
        var calcResponse = await client.SendRequestAsync(calcRequest, TimeSpan.FromSeconds(5));

        _logger.LogInformation("Calculation (15 * 3): {Result}", calcResponse.Result);

        _logger.LogInformation("Basic RPC demo completed successfully!");
    }

    /// <summary>
    /// Demonstrates concurrent RPC requests
    /// </summary>
    public async Task ConcurrentRPCDemo()
    {
        _logger.LogInformation("=== Concurrent RPC Demo ===");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        using var server = new RPCServer(channel, loggerFactory.CreateLogger<RPCServer>(), "concurrent-rpc-queue", "concurrent-server");
        using var client = new RPCClient(channel, loggerFactory.CreateLogger<RPCClient>(), "concurrent-rpc-queue", "concurrent-client");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 5);

        _logger.LogInformation("Sending 5 concurrent calculation requests...");

        // Create multiple requests
        var requests = new List<RPCRequest>
        {
            RPCRequestFactory.CreateCalculationRequest("add", 10, 5),
            RPCRequestFactory.CreateCalculationRequest("subtract", 20, 8),
            RPCRequestFactory.CreateCalculationRequest("multiply", 6, 7),
            RPCRequestFactory.CreateCalculationRequest("divide", 84, 12),
            RPCRequestFactory.CreateDataProcessingRequest(new List<int> { 1, 2, 3, 4, 5 }, "sum")
        };

        var startTime = DateTime.UtcNow;
        var responses = await client.SendConcurrentRequestsAsync(requests, TimeSpan.FromSeconds(10));
        var endTime = DateTime.UtcNow;

        _logger.LogInformation("Received {Count} responses in {Duration}ms:", 
            responses.Count, (endTime - startTime).TotalMilliseconds);

        for (int i = 0; i < responses.Count; i++)
        {
            var response = responses[i];
            var request = requests[i];
            _logger.LogInformation("  Request {Index}: {Method} -> {Result}", 
                i + 1, request.Method, response.Result);
        }

        var stats = server.GetStats();
        _logger.LogInformation("Server processed {Count} requests", stats.RequestsProcessed);
    }

    /// <summary>
    /// Demonstrates load balancing with multiple servers
    /// </summary>
    public async Task LoadBalancingDemo()
    {
        _logger.LogInformation("=== Load Balancing Demo ===");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        var servers = new List<RPCServer>();
        
        try
        {
            // Create 3 servers
            for (int i = 1; i <= 3; i++)
            {
                var server = new RPCServer(channel, loggerFactory.CreateLogger<RPCServer>(), 
                    "loadbalance-rpc-queue", $"lb-server-{i}");
                await server.InitializeAsync(durable: false);
                await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 2);
                servers.Add(server);
                _logger.LogInformation("Started server {ServerId}", server.ServerId);
            }

            using var client = new RPCClient(channel, loggerFactory.CreateLogger<RPCClient>(), 
                "loadbalance-rpc-queue", "lb-client");
            await client.InitializeAsync();

            _logger.LogInformation("Sending 12 requests to test load balancing...");

            // Send multiple requests
            var tasks = new List<Task<RPCResponse>>();
            for (int i = 1; i <= 12; i++)
            {
                var request = RPCRequestFactory.CreateEchoRequest($"Message {i}", "lb-client");
                tasks.Add(client.SendRequestAsync(request, TimeSpan.FromSeconds(10)));
            }

            var responses = await Task.WhenAll(tasks);

            // Analyze distribution
            var serverDistribution = responses
                .GroupBy(r => r.ServerId)
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogInformation("Request distribution among servers:");
            foreach (var kvp in serverDistribution)
            {
                _logger.LogInformation("  {ServerId}: {Count} requests", kvp.Key, kvp.Value);
            }

            // Show server statistics
            foreach (var server in servers)
            {
                var stats = server.GetStats();
                _logger.LogInformation("Server {ServerId}: {Processed} processed, {Failed} failed", 
                    stats.ServerId, stats.RequestsProcessed, stats.RequestsFailed);
            }
        }
        finally
        {
            foreach (var server in servers)
            {
                server.Dispose();
            }
        }
    }

    /// <summary>
    /// Demonstrates error handling and timeout scenarios
    /// </summary>
    public async Task ErrorHandlingDemo()
    {
        _logger.LogInformation("=== Error Handling Demo ===");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        using var server = new RPCServer(channel, loggerFactory.CreateLogger<RPCServer>(), "error-rpc-queue", "error-server");
        using var client = new RPCClient(channel, loggerFactory.CreateLogger<RPCClient>(), "error-rpc-queue", "error-client");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();
        await server.StartAsync(RPCRequestProcessors.DefaultProcessor, prefetchCount: 1);

        // Test 1: Error response
        _logger.LogInformation("Testing error response...");
        var errorRequest = RPCRequestFactory.CreateErrorRequest("Simulated error for demo");
        var errorResponse = await client.SendRequestAsync(errorRequest, TimeSpan.FromSeconds(5));
        
        _logger.LogInformation("Error response - Success: {Success}, Error: {Error}", 
            errorResponse.Success, errorResponse.ErrorMessage);

        // Test 2: Division by zero
        _logger.LogInformation("Testing division by zero...");
        var divisionRequest = RPCRequestFactory.CreateCalculationRequest("divide", 10, 0);
        var divisionResponse = await client.SendRequestAsync(divisionRequest, TimeSpan.FromSeconds(5));
        
        _logger.LogInformation("Division by zero - Success: {Success}, Error: {Error}", 
            divisionResponse.Success, divisionResponse.ErrorMessage);

        // Test 3: Timeout (this will throw an exception)
        _logger.LogInformation("Testing timeout scenario...");
        try
        {
            var timeoutRequest = RPCRequestFactory.CreateDelayRequest(3000); // 3 second delay
            await client.SendRequestAsync(timeoutRequest, TimeSpan.FromSeconds(1)); // 1 second timeout
        }
        catch (TimeoutException ex)
        {
            _logger.LogInformation("Timeout caught as expected: {Message}", ex.Message);
        }

        _logger.LogInformation("Error handling demo completed!");
    }

    /// <summary>
    /// Demonstrates custom request processing
    /// </summary>
    public async Task CustomProcessorDemo()
    {
        _logger.LogInformation("=== Custom Processor Demo ===");

        using var channel = _connection.CreateChannel();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        using var server = new RPCServer(channel, loggerFactory.CreateLogger<RPCServer>(), "custom-rpc-queue", "custom-server");
        using var client = new RPCClient(channel, loggerFactory.CreateLogger<RPCClient>(), "custom-rpc-queue", "custom-client");

        await server.InitializeAsync(durable: false);
        await client.InitializeAsync();

        // Define custom processor
        async Task<RPCResponse> CustomProcessor(RPCRequest request)
        {
            _logger.LogInformation("Processing custom request: {Method}", request.Method);
            
            return request.Method switch
            {
                "greet" => ProcessGreeting(request),
                "reverse" => ProcessReverse(request),
                "timestamp" => ProcessTimestamp(request),
                _ => await RPCRequestProcessors.DefaultProcessor(request) // Fallback to default
            };
        }

        await server.StartAsync(CustomProcessor, prefetchCount: 1);

        // Test custom methods
        var greetRequest = new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "greet",
            Parameters = new Dictionary<string, object> { ["name"] = "Alice" }
        };
        var greetResponse = await client.SendRequestAsync(greetRequest, TimeSpan.FromSeconds(5));
        _logger.LogInformation("Greeting: {Result}", greetResponse.Result);

        var reverseRequest = new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "reverse",
            Parameters = new Dictionary<string, object> { ["text"] = "Hello World" }
        };
        var reverseResponse = await client.SendRequestAsync(reverseRequest, TimeSpan.FromSeconds(5));
        _logger.LogInformation("Reversed: {Result}", reverseResponse.Result);

        var timestampRequest = new RPCRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = "timestamp",
            Parameters = new Dictionary<string, object>()
        };
        var timestampResponse = await client.SendRequestAsync(timestampRequest, TimeSpan.FromSeconds(5));
        _logger.LogInformation("Timestamp: {Result}", timestampResponse.Result);

        _logger.LogInformation("Custom processor demo completed!");
    }

    private static RPCResponse ProcessGreeting(RPCRequest request)
    {
        var name = request.Parameters.GetValueOrDefault("name", "World").ToString();
        return new RPCResponse
        {
            Id = Guid.NewGuid().ToString(),
            Success = true,
            Result = $"Hello, {name}! Welcome to RPC."
        };
    }

    private static RPCResponse ProcessReverse(RPCRequest request)
    {
        var text = request.Parameters.GetValueOrDefault("text", "").ToString();
        var reversed = new string(text!.Reverse().ToArray());
        return new RPCResponse
        {
            Id = Guid.NewGuid().ToString(),
            Success = true,
            Result = reversed
        };
    }

    private static RPCResponse ProcessTimestamp(RPCRequest request)
    {
        return new RPCResponse
        {
            Id = Guid.NewGuid().ToString(),
            Success = true,
            Result = $"Server time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
        };
    }

    /// <summary>
    /// Runs all demos in sequence
    /// </summary>
    public async Task RunAllDemos()
    {
        try
        {
            await BasicRPCDemo();
            await Task.Delay(1000);
            
            await ConcurrentRPCDemo();
            await Task.Delay(1000);
            
            await LoadBalancingDemo();
            await Task.Delay(1000);
            
            await ErrorHandlingDemo();
            await Task.Delay(1000);
            
            await CustomProcessorDemo();
            
            _logger.LogInformation("=== All RPC Demos Completed Successfully! ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo failed: {Message}", ex.Message);
        }
    }
}