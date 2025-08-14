using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using RabbitMQ.Client;
using RabbitTests.Infrastructure;

namespace RabbitTests.Infrastructure;

/// <summary>
/// Base test class that provides common setup and teardown for RabbitMQ tests
/// </summary>
public abstract class TestBase : IDisposable
{
    protected RabbitMQConnection Connection { get; private set; } = null!;
    protected ILogger Logger { get; private set; } = null!;
    protected IChannel Channel { get; private set; } = null!;
    private bool _disposed = false;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        Logger = loggerFactory.CreateLogger(GetType());

        // Create connection
        Connection = new RabbitMQConnection(loggerFactory.CreateLogger<RabbitMQConnection>());
        
        // Test connection
        var isConnected = await Connection.TestConnectionAsync();
        if (!isConnected)
        {
            throw new InvalidOperationException("Cannot connect to RabbitMQ. Please ensure RabbitMQ is running on localhost:5672 with admin/password credentials.");
        }

        // Create channel
        Channel = Connection.CreateChannel();
        
        Logger.LogInformation("Test setup completed successfully");
    }

    [SetUp]
    public virtual async Task SetUp()
    {
        // Clean up any existing test queues and exchanges before each test
        await CleanupTestResources();
    }

    [TearDown]
    public virtual async Task TearDown()
    {
        // Clean up test resources after each test
        await CleanupTestResources();
    }

    [OneTimeTearDown]
    public virtual void OneTimeTearDown()
    {
        Dispose();
    }

    /// <summary>
    /// Cleans up test-related queues and exchanges
    /// </summary>
    protected virtual async Task CleanupTestResources()
    {
        try
        {
            if (Channel != null && Channel.IsOpen)
            {
                // Delete test queues (if they exist)
                await SafeDeleteQueue("test-work-queue");
                await SafeDeleteQueue("test-work-queue-durable");
                
                // Delete test exchanges (if they exist)
                await SafeDeleteExchange("test-exchange");
                await SafeDeleteExchange("test-fanout-exchange");
                await SafeDeleteExchange("test-durable-fanout-exchange");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Error during test resource cleanup");
        }
    }

    /// <summary>
    /// Safely deletes a queue, ignoring errors if it doesn't exist
    /// </summary>
    protected async Task SafeDeleteQueue(string queueName)
    {
        try
        {
            await Channel.QueueDeleteAsync(queueName, false, false);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "Could not delete queue {QueueName}", queueName);
        }
    }

    /// <summary>
    /// Safely deletes an exchange, ignoring errors if it doesn't exist
    /// </summary>
    protected async Task SafeDeleteExchange(string exchangeName)
    {
        try
        {
            await Channel.ExchangeDeleteAsync(exchangeName, false);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "Could not delete exchange {ExchangeName}", exchangeName);
        }
    }

    /// <summary>
    /// Declares a test queue with specified parameters
    /// </summary>
    protected async Task<string> DeclareTestQueue(string queueName, bool durable = false, bool exclusive = false, bool autoDelete = true)
    {
        var result = await Channel.QueueDeclareAsync(queueName, durable, exclusive, autoDelete);
        Logger.LogDebug("Declared queue {QueueName} (durable: {Durable}, exclusive: {Exclusive}, autoDelete: {AutoDelete})", 
            queueName, durable, exclusive, autoDelete);
        return result.QueueName;
    }

    /// <summary>
    /// Waits for messages to be processed (useful for async operations in tests)
    /// </summary>
    protected async Task WaitForMessages(int delayMs = 1000)
    {
        await Task.Delay(delayMs);
    }

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                Channel?.Dispose();
                Connection?.Dispose();
                Logger?.LogInformation("Test resources disposed");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error disposing test resources");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}