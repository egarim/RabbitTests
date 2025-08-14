using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitTests.Infrastructure;
using System.Text;

namespace RabbitTests.UseCase5_RPC;

/// <summary>
/// RPC Server for Request-Reply pattern - processes requests and sends responses
/// </summary>
public class RPCServer : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<RPCServer> _logger;
    private readonly string _requestQueueName;
    private readonly string _serverId;
    private readonly AsyncEventingBasicConsumer _consumer;
    private string _consumerTag = string.Empty;
    private volatile bool _isRunning = false;
    private bool _disposed = false;

    // Statistics
    private int _requestsProcessed = 0;
    private int _requestsFailed = 0;
    private readonly DateTime _startTime = DateTime.UtcNow;

    // Request processing delegate
    public delegate Task<RPCResponse> RequestProcessorDelegate(RPCRequest request);
    private RequestProcessorDelegate? _requestProcessor;

    public RPCServer(IChannel channel, ILogger<RPCServer> logger, string requestQueueName = "rpc-queue", string? serverId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestQueueName = requestQueueName;
        _serverId = serverId ?? $"rpc-server-{Guid.NewGuid():N}";
        
        // Setup consumer for requests
        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += OnRequestReceived;
    }

    /// <summary>
    /// Initializes the RPC server and declares the request queue
    /// </summary>
    public async Task InitializeAsync(bool durable = false)
    {
        // Declare request queue
        await _channel.QueueDeclareAsync(
            queue: _requestQueueName,
            durable: durable,
            exclusive: false,
            autoDelete: !durable,
            arguments: null);

        _logger.LogInformation("RPC Server {ServerId} initialized with request queue {RequestQueue}", _serverId, _requestQueueName);
    }

    /// <summary>
    /// Starts the RPC server with a request processor
    /// </summary>
    public async Task StartAsync(RequestProcessorDelegate requestProcessor, ushort prefetchCount = 1)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("RPC Server is already running");
        }

        _requestProcessor = requestProcessor ?? throw new ArgumentNullException(nameof(requestProcessor));

        // Set QoS to control message distribution
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetchCount, global: false);

        // Start consuming requests
        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _requestQueueName,
            autoAck: false, // Manual acknowledgment for reliability
            consumer: _consumer);

        _isRunning = true;
        _logger.LogInformation("RPC Server {ServerId} started and listening for requests", _serverId);
    }

    /// <summary>
    /// Stops the RPC server
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        if (!string.IsNullOrEmpty(_consumerTag))
        {
            await _channel.BasicCancelAsync(_consumerTag);
            _consumerTag = string.Empty;
        }

        _logger.LogInformation("RPC Server {ServerId} stopped. Processed {ProcessedCount} requests, {FailedCount} failed", 
            _serverId, _requestsProcessed, _requestsFailed);
    }

    /// <summary>
    /// Handles incoming request messages
    /// </summary>
    private async Task OnRequestReceived(object sender, BasicDeliverEventArgs eventArgs)
    {
        var correlationId = eventArgs.BasicProperties.CorrelationId;
        var replyTo = eventArgs.BasicProperties.ReplyTo;
        
        try
        {
            if (string.IsNullOrEmpty(correlationId) || string.IsNullOrEmpty(replyTo))
            {
                _logger.LogWarning("Received request without correlation ID or reply-to address");
                await _channel.BasicRejectAsync(eventArgs.DeliveryTag, false);
                return;
            }

            // Deserialize request
            var request = MessageHelpers.DeserializeMessage<RPCRequest>(eventArgs.Body.ToArray());
            request.ServerId = _serverId;
            request.ReceivedAt = DateTime.UtcNow;

            _logger.LogDebug("Processing RPC request {CorrelationId} on server {ServerId}", correlationId, _serverId);

            RPCResponse response;
            
            try
            {
                // Process the request
                if (_requestProcessor != null)
                {
                    response = await _requestProcessor(request);
                }
                else
                {
                    response = new RPCResponse
                    {
                        Id = Guid.NewGuid().ToString(),
                        CorrelationId = correlationId,
                        Success = false,
                        ErrorMessage = "No request processor configured",
                        ProcessedAt = DateTime.UtcNow,
                        ServerId = _serverId
                    };
                }

                response.CorrelationId = correlationId;
                response.ServerId = _serverId;
                response.ProcessedAt = DateTime.UtcNow;

                Interlocked.Increment(ref _requestsProcessed);
            }
            catch (Exception processingEx)
            {
                _logger.LogError(processingEx, "Error processing RPC request {CorrelationId}", correlationId);
                
                response = new RPCResponse
                {
                    Id = Guid.NewGuid().ToString(),
                    CorrelationId = correlationId,
                    Success = false,
                    ErrorMessage = processingEx.Message,
                    ProcessedAt = DateTime.UtcNow,
                    ServerId = _serverId
                };

                Interlocked.Increment(ref _requestsFailed);
            }

            // Send response
            await SendResponseAsync(response, replyTo, correlationId);

            // Acknowledge the request
            await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);

            _logger.LogDebug("Completed RPC request {CorrelationId} on server {ServerId}", correlationId, _serverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC request {CorrelationId}", correlationId);
            
            try
            {
                // Reject the message (don't requeue to avoid infinite loops)
                await _channel.BasicRejectAsync(eventArgs.DeliveryTag, false);
                Interlocked.Increment(ref _requestsFailed);
            }
            catch (Exception rejectEx)
            {
                _logger.LogError(rejectEx, "Error rejecting failed message");
            }
        }
    }

    /// <summary>
    /// Sends a response back to the client
    /// </summary>
    private async Task SendResponseAsync(RPCResponse response, string replyTo, string correlationId)
    {
        try
        {
            var properties = new BasicProperties
            {
                CorrelationId = correlationId,
                MessageId = response.Id,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["server-id"] = _serverId,
                    ["response-time"] = DateTimeOffset.UtcNow.ToString("O")
                }
            };

            var messageBody = MessageHelpers.SerializeMessage(response);

            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: replyTo,
                mandatory: false,
                basicProperties: properties,
                body: messageBody);

            _logger.LogDebug("Sent RPC response {CorrelationId} to {ReplyTo}", correlationId, replyTo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending RPC response {CorrelationId}", correlationId);
            throw;
        }
    }

    /// <summary>
    /// Gets server statistics
    /// </summary>
    public RPCServerStats GetStats()
    {
        return new RPCServerStats
        {
            ServerId = _serverId,
            RequestsProcessed = _requestsProcessed,
            RequestsFailed = _requestsFailed,
            StartTime = _startTime,
            IsRunning = _isRunning,
            Uptime = DateTime.UtcNow - _startTime
        };
    }

    /// <summary>
    /// Gets the server ID
    /// </summary>
    public string ServerId => _serverId;

    /// <summary>
    /// Gets whether the server is currently running
    /// </summary>
    public bool IsRunning => _isRunning;

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopAsync().Wait(TimeSpan.FromSeconds(5));
                _logger.LogInformation("RPC Server {ServerId} disposed", _serverId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RPC Server {ServerId}", _serverId);
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Statistics for RPC Server
/// </summary>
public class RPCServerStats
{
    public string ServerId { get; set; } = string.Empty;
    public int RequestsProcessed { get; set; }
    public int RequestsFailed { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsRunning { get; set; }
    public TimeSpan Uptime { get; set; }
    public double RequestsPerSecond => Uptime.TotalSeconds > 0 ? RequestsProcessed / Uptime.TotalSeconds : 0;
}