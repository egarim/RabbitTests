using RabbitMQ.Client;
using Microsoft.Extensions.Logging;

namespace RabbitTests.Infrastructure;

public class RabbitMQConnection : IDisposable
{
    private readonly ILogger<RabbitMQConnection> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMQConnection(ILogger<RabbitMQConnection>? logger = null)
    {
        _logger = logger ?? new NoOpLogger();
    }

    public async Task<IChannel> GetChannelAsync()
    {
        if (_channel == null || _channel.IsClosed)
        {
            await ConnectAsync();
        }
        return _channel!;
    }

    private async Task ConnectAsync()
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "admin",
                Password = "password",
                VirtualHost = "/",
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                AutomaticRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();
            
            _logger.LogInformation("Connected to RabbitMQ successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw;
        }
    }

    public async Task CleanupAsync()
    {
        try
        {
            if (_channel != null && !_channel.IsClosed)
            {
                await _channel.CloseAsync();
            }
            
            if (_connection != null && _connection.IsOpen)
            {
                await _connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupAsync().GetAwaiter().GetResult();
            _disposed = true;
        }
    }

    private class NoOpLogger : ILogger<RabbitMQConnection>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}