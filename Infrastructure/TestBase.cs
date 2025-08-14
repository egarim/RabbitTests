using Microsoft.Extensions.Logging;

namespace RabbitTests.Infrastructure;

public abstract class TestBase : IDisposable
{
    protected RabbitMQConnection Connection { get; private set; }
    protected ILogger Logger { get; private set; }

    protected TestBase()
    {
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        Logger = loggerFactory.CreateLogger(GetType());
        Connection = new RabbitMQConnection(loggerFactory.CreateLogger<RabbitMQConnection>());
    }

    protected async Task SetupAsync()
    {
        // Ensure connection is established
        await Connection.GetChannelAsync();
    }

    protected async Task TeardownAsync()
    {
        await Connection.CleanupAsync();
    }

    public void Dispose()
    {
        TeardownAsync().GetAwaiter().GetResult();
        Connection?.Dispose();
    }

    protected async Task CleanupQueueAsync(string queueName)
    {
        try
        {
            var channel = await Connection.GetChannelAsync();
            await channel.QueueDeleteAsync(queueName, false, false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to cleanup queue {QueueName}", queueName);
        }
    }

    protected async Task CleanupExchangeAsync(string exchangeName)
    {
        try
        {
            var channel = await Connection.GetChannelAsync();
            await channel.ExchangeDeleteAsync(exchangeName, false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to cleanup exchange {ExchangeName}", exchangeName);
        }
    }
}