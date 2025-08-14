using RabbitMQ.Client;
using Microsoft.Extensions.Logging;

namespace RabbitTests.Infrastructure;

/// <summary>
/// Manages RabbitMQ connections and provides connection factory functionality
/// </summary>
public class RabbitMQConnection : IDisposable
{
    private readonly ILogger<RabbitMQConnection> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection? _connection;
    private bool _disposed = false;

    public RabbitMQConnection(ILogger<RabbitMQConnection> logger)
    {
        _logger = logger;
        _connectionFactory = new ConnectionFactory
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
    }

    /// <summary>
    /// Gets or creates a connection to RabbitMQ
    /// </summary>
    public IConnection GetConnection()
    {
        if (_connection == null || !_connection.IsOpen)
        {
            try
            {
                _connection = _connectionFactory.CreateConnectionAsync().Result;
                _logger.LogInformation("Successfully connected to RabbitMQ at {HostName}:{Port}", 
                    _connectionFactory.HostName, _connectionFactory.Port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to RabbitMQ");
                throw;
            }
        }
        return _connection;
    }

    /// <summary>
    /// Creates a new channel for message operations
    /// </summary>
    public IChannel CreateChannel()
    {
        return GetConnection().CreateChannelAsync().Result;
    }

    /// <summary>
    /// Tests if connection to RabbitMQ is possible
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();
            _logger.LogInformation("RabbitMQ connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ connection test failed");
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _connection?.CloseAsync().Wait();
                _connection?.Dispose();
                _logger.LogInformation("RabbitMQ connection disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RabbitMQ connection");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}