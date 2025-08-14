using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitTests.Infrastructure;
using System.Collections.Concurrent;
using System.Text;

namespace RabbitTests.UseCase5_RPC;

/// <summary>
/// RPC Client for Request-Reply pattern - sends requests and waits for responses
/// </summary>
public class RPCClient : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<RPCClient> _logger;
    private readonly string _requestQueueName;
    private readonly string _replyQueueName;
    private readonly string _clientId;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RPCResponse>> _pendingRequests;
    private readonly AsyncEventingBasicConsumer _consumer;
    private string _consumerTag = string.Empty;
    private bool _disposed = false;

    public RPCClient(IChannel channel, ILogger<RPCClient> logger, string requestQueueName = "rpc-queue", string? clientId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestQueueName = requestQueueName;
        _clientId = clientId ?? $"rpc-client-{Guid.NewGuid():N}";
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<RPCResponse>>();
        
        // Create exclusive reply queue for this client
        _replyQueueName = $"reply-{_clientId}";
        
        // Setup consumer for replies
        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += OnReplyReceived;
    }

    /// <summary>
    /// Initializes the RPC client and starts listening for replies
    /// </summary>
    public async Task InitializeAsync()
    {
        // Declare exclusive reply queue for this client
        await _channel.QueueDeclareAsync(
            queue: _replyQueueName,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null);

        // Start consuming replies
        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _replyQueueName,
            autoAck: true,
            consumer: _consumer);

        _logger.LogInformation("RPC Client {ClientId} initialized with reply queue {ReplyQueue}", _clientId, _replyQueueName);
    }

    /// <summary>
    /// Sends an RPC request and waits for response
    /// </summary>
    public async Task<RPCResponse> SendRequestAsync(RPCRequest request, TimeSpan? timeout = null)
    {
        var correlationId = Guid.NewGuid().ToString();
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
        
        var tcs = new TaskCompletionSource<RPCResponse>();
        _pendingRequests[correlationId] = tcs;

        try
        {
            // Prepare message properties
            var properties = new BasicProperties
            {
                CorrelationId = correlationId,
                ReplyTo = _replyQueueName,
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["client-id"] = _clientId,
                    ["request-time"] = DateTimeOffset.UtcNow.ToString("O")
                }
            };

            // Serialize request
            var messageBody = MessageHelpers.SerializeMessage(request);

            // Send request
            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _requestQueueName,
                mandatory: false,
                basicProperties: properties,
                body: messageBody);

            _logger.LogDebug("Sent RPC request {CorrelationId} from client {ClientId}", correlationId, _clientId);

            // Wait for response with timeout
            using var cts = new CancellationTokenSource(actualTimeout);
            var cancellationTask = Task.Delay(actualTimeout, cts.Token);
            var responseTask = tcs.Task;

            var completedTask = await Task.WhenAny(responseTask, cancellationTask);

            if (completedTask == cancellationTask)
            {
                _logger.LogWarning("RPC request {CorrelationId} timed out after {Timeout}", correlationId, actualTimeout);
                _pendingRequests.TryRemove(correlationId, out _);
                throw new TimeoutException($"RPC request timed out after {actualTimeout}");
            }

            cts.Cancel(); // Cancel the timeout task
            return await responseTask;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(correlationId, out _);
            _logger.LogError(ex, "Error sending RPC request {CorrelationId}", correlationId);
            throw;
        }
    }

    /// <summary>
    /// Sends multiple concurrent RPC requests
    /// </summary>
    public async Task<List<RPCResponse>> SendConcurrentRequestsAsync(IEnumerable<RPCRequest> requests, TimeSpan? timeout = null)
    {
        var requestTasks = requests.Select(request => SendRequestAsync(request, timeout)).ToArray();
        var responses = await Task.WhenAll(requestTasks);
        return responses.ToList();
    }

    /// <summary>
    /// Handles incoming reply messages
    /// </summary>
    private async Task OnReplyReceived(object sender, BasicDeliverEventArgs eventArgs)
    {
        try
        {
            var correlationId = eventArgs.BasicProperties.CorrelationId;
            
            if (string.IsNullOrEmpty(correlationId))
            {
                _logger.LogWarning("Received reply without correlation ID");
                return;
            }

            if (_pendingRequests.TryRemove(correlationId, out var tcs))
            {
                try
                {
                    var response = MessageHelpers.DeserializeMessage<RPCResponse>(eventArgs.Body.ToArray());
                    response.ReceivedAt = DateTime.UtcNow;
                    
                    _logger.LogDebug("Received RPC response {CorrelationId} for client {ClientId}", correlationId, _clientId);
                    tcs.SetResult(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deserializing RPC response {CorrelationId}", correlationId);
                    tcs.SetException(ex);
                }
            }
            else
            {
                _logger.LogWarning("Received reply for unknown correlation ID {CorrelationId}", correlationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RPC reply");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the number of pending requests
    /// </summary>
    public int PendingRequestCount => _pendingRequests.Count;

    /// <summary>
    /// Gets the client ID
    /// </summary>
    public string ClientId => _clientId;

    /// <summary>
    /// Cancels all pending requests
    /// </summary>
    public void CancelAllPendingRequests()
    {
        var pendingRequests = _pendingRequests.ToArray();
        _pendingRequests.Clear();

        foreach (var kvp in pendingRequests)
        {
            kvp.Value.SetCanceled();
        }

        _logger.LogInformation("Cancelled {Count} pending requests for client {ClientId}", pendingRequests.Length, _clientId);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                // Cancel all pending requests
                CancelAllPendingRequests();

                // Stop consuming
                if (!string.IsNullOrEmpty(_consumerTag))
                {
                    _channel.BasicCancelAsync(_consumerTag).Wait(TimeSpan.FromSeconds(5));
                }

                _logger.LogInformation("RPC Client {ClientId} disposed", _clientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RPC Client {ClientId}", _clientId);
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}