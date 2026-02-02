//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace NoZ.Platform;

public class DotNetNetworkDriver : INetworkDriver
{
    private ClientWebSocket? _client;
    private CancellationTokenSource? _clientCts;
    private readonly ConcurrentQueue<NetworkMessage> _clientReceiveQueue = new();
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    private HttpListener? _httpListener;
    private CancellationTokenSource? _serverCts;
    private readonly ConcurrentDictionary<int, WebSocket> _serverConnections = new();
    private readonly ConcurrentQueue<(int, NetworkMessage)> _serverReceiveQueue = new();
    private readonly ConcurrentQueue<int> _pendingConnects = new();
    private readonly ConcurrentQueue<int> _pendingDisconnects = new();
    private int _nextConnectionId;
    private bool _isServerRunning;

    public bool SupportsServer => true;
    public bool IsServerRunning => _isServerRunning;

    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<int>? OnClientConnected;
    public event Action<int>? OnClientDisconnected;

    public void Init() { }

    public void Shutdown()
    {
        Disconnect();
        StopServer();
    }

    public ConnectionState GetConnectionState() => _connectionState;

    public void Connect(string url)
    {
        if (_connectionState != ConnectionState.Disconnected)
            return;

        _connectionState = ConnectionState.Connecting;
        _ = ConnectAsync(url);
    }

    private async Task ConnectAsync(string url)
    {
        try
        {
            _client = new ClientWebSocket();
            _clientCts = new CancellationTokenSource();

            await _client.ConnectAsync(new Uri(url), _clientCts.Token);

            _connectionState = ConnectionState.Connected;
            OnConnected?.Invoke();

            _ = ClientReceiveLoopAsync();
        }
        catch
        {
            _connectionState = ConnectionState.Failed;
            _client?.Dispose();
            _client = null;
        }
    }

    private async Task ClientReceiveLoopAsync()
    {
        var buffer = new byte[4096];

        try
        {
            while (_client?.State == WebSocketState.Open && _clientCts is { IsCancellationRequested: false })
            {
                var result = await _client.ReceiveAsync(buffer, _clientCts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                {
                    var data = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, data, 0, result.Count);
                    _clientReceiveQueue.Enqueue(new NetworkMessage(data, result.Count));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _connectionState = ConnectionState.Disconnected;
            OnDisconnected?.Invoke();
        }
    }

    public void Disconnect()
    {
        _clientCts?.Cancel();

        if (_client != null)
        {
            try
            {
                if (_client.State == WebSocketState.Open)
                    _client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(1000);
            }
            catch { }

            _client.Dispose();
            _client = null;
        }

        _clientCts?.Dispose();
        _clientCts = null;
        _connectionState = ConnectionState.Disconnected;

        while (_clientReceiveQueue.TryDequeue(out _)) { }
    }

    public void Send(ReadOnlySpan<byte> data)
    {
        if (_client?.State != WebSocketState.Open)
            return;

        var copy = data.ToArray();
        _ = _client.SendAsync(copy, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    public bool TryReceive(out NetworkMessage message)
    {
        return _clientReceiveQueue.TryDequeue(out message);
    }

    public void StartServer(int port, int maxConnections)
    {
        if (_isServerRunning)
            return;

        _serverCts = new CancellationTokenSource();
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://+:{port}/");

        try
        {
            _httpListener.Start();
            _isServerRunning = true;
            _ = ServerAcceptLoopAsync();
        }
        catch
        {
            _httpListener.Close();
            _httpListener = null;
            _isServerRunning = false;
        }
    }

    private async Task ServerAcceptLoopAsync()
    {
        while (_isServerRunning && _serverCts is { IsCancellationRequested: false })
        {
            try
            {
                var context = await _httpListener!.GetContextAsync();

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                var wsContext = await context.AcceptWebSocketAsync(null);
                var connectionId = Interlocked.Increment(ref _nextConnectionId);

                _serverConnections[connectionId] = wsContext.WebSocket;
                _pendingConnects.Enqueue(connectionId);

                _ = ServerConnectionReceiveLoopAsync(connectionId, wsContext.WebSocket);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task ServerConnectionReceiveLoopAsync(int connectionId, WebSocket socket)
    {
        var buffer = new byte[4096];

        try
        {
            while (socket.State == WebSocketState.Open && _serverCts is { IsCancellationRequested: false })
            {
                var result = await socket.ReceiveAsync(buffer, _serverCts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                {
                    var data = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, data, 0, result.Count);
                    _serverReceiveQueue.Enqueue((connectionId, new NetworkMessage(data, result.Count)));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _serverConnections.TryRemove(connectionId, out _);
            _pendingDisconnects.Enqueue(connectionId);

            try { socket.Dispose(); } catch { }
        }
    }

    public void StopServer()
    {
        _serverCts?.Cancel();

        foreach (var kvp in _serverConnections)
        {
            try
            {
                kvp.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(500);
                kvp.Value.Dispose();
            }
            catch { }
        }
        _serverConnections.Clear();

        try { _httpListener?.Stop(); } catch { }
        _httpListener = null;

        _serverCts?.Dispose();
        _serverCts = null;
        _isServerRunning = false;

        while (_serverReceiveQueue.TryDequeue(out _)) { }
        while (_pendingConnects.TryDequeue(out _)) { }
        while (_pendingDisconnects.TryDequeue(out _)) { }
    }

    public void Broadcast(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();

        foreach (var kvp in _serverConnections)
        {
            if (kvp.Value.State == WebSocketState.Open)
                _ = kvp.Value.SendAsync(copy, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
    }

    public void SendTo(int connectionId, ReadOnlySpan<byte> data)
    {
        if (!_serverConnections.TryGetValue(connectionId, out var socket))
            return;

        if (socket.State != WebSocketState.Open)
            return;

        var copy = data.ToArray();
        _ = socket.SendAsync(copy, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    public bool TryReceiveServer(out int connectionId, out NetworkMessage message)
    {
        while (_pendingConnects.TryDequeue(out var connId))
            OnClientConnected?.Invoke(connId);

        while (_pendingDisconnects.TryDequeue(out var discId))
            OnClientDisconnected?.Invoke(discId);

        if (_serverReceiveQueue.TryDequeue(out var tuple))
        {
            connectionId = tuple.Item1;
            message = tuple.Item2;
            return true;
        }

        connectionId = 0;
        message = default;
        return false;
    }
}
