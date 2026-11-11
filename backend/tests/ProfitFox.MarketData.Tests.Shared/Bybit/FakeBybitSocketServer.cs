using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProfitFox.MarketData.Tests.Shared.Bybit;

/// <summary>
///     The public streams of Bybit v5 on a local port: <c>/v5/public/spot</c> and <c>/v5/public/linear</c>. It answers
///     <c>subscribe</c>, <c>unsubscribe</c> and <c>ping</c> as Bybit does, pushes the messages a test gives it and counts the
///     connections and subscriptions clients make.
/// </summary>
public sealed class FakeBybitSocketServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<WebSocket, Connection> _connections = new ConcurrentDictionary<WebSocket, Connection>();

    private FakeBybitSocketServer(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Like <c>ws://127.0.0.1:5297</c>.</summary>
    public string Address { get; private set; } = "";

    /// <summary>Topics a subscription to is refused, as Bybit refuses one it does not serve.</summary>
    public ConcurrentDictionary<string, bool> RefusedTopics { get; } = new ConcurrentDictionary<string, bool>();

    /// <summary>How many topics each subscribe request carried, in order, on any path.</summary>
    public ConcurrentQueue<int> SubscribeSizes { get; } = new ConcurrentQueue<int>();

    /// <summary>How many topics each connection open now to <paramref name="path" /> carries.</summary>
    public IReadOnlyList<int> TopicsPerConnection(string path)
    {
        return [.. _connections.Values.Where(connection => connection.Path == path).Select(connection => connection.Topics.Count)];
    }

    /// <summary>The connections open now to <paramref name="path" />, such as <c>/v5/public/linear</c>.</summary>
    public int Connections(string path)
    {
        return _connections.Values.Count(connection => connection.Path == path);
    }

    /// <summary>Whether a client is subscribed to <paramref name="topic" /> now.</summary>
    public bool Subscribed(string topic)
    {
        return _connections.Values.Any(connection => connection.Topics.ContainsKey(topic));
    }

    public static async Task<FakeBybitSocketServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        var server = new FakeBybitSocketServer(app);
        app.UseWebSockets();
        app.Map("/v5/{*path}", server.AcceptAsync);
        await app.StartAsync();
        string http = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.Address = http.Replace("http://", "ws://", StringComparison.Ordinal);

        return server;
    }

    /// <summary>Pushes the data of a topic to every client subscribed to it.</summary>
    public async Task PushAsync(string topic, JsonArray data)
    {
        string message = new JsonObject
        {
            ["topic"] = topic,
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = "snapshot",
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["creationTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["data"] = data
        }.ToJsonString();

        foreach (Connection connection in _connections.Values.Where(connection => connection.Topics.ContainsKey(topic)))
        {
            await connection.SendAsync(message);
        }
    }

    /// <summary>Breaks every connection, as a network failure does; clients reconnect and subscribe again.</summary>
    public Task DropAsync()
    {
        foreach (WebSocket socket in _connections.Keys)
        {
            socket.Abort();
            _connections.TryRemove(socket, out _);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>An update of <c>kline.1</c>: <paramref name="confirm" /> tells a closed candle from one still forming.</summary>
    public static JsonObject Kline(DateTimeOffset openTime, decimal close, bool confirm, decimal volume = 1m, decimal? turnover = null)
    {
        return new JsonObject
        {
            ["start"] = openTime.ToUnixTimeMilliseconds(),
            ["end"] = openTime.AddMinutes(1).ToUnixTimeMilliseconds() - 1,
            ["interval"] = "1",
            ["open"] = Text(close),
            ["close"] = Text(close),
            ["high"] = Text(close),
            ["low"] = Text(close),
            ["volume"] = Text(volume),
            ["turnover"] = Text(turnover ?? close),
            ["confirm"] = confirm,
            ["timestamp"] = openTime.AddMinutes(1).ToUnixTimeMilliseconds()
        };
    }

    /// <summary>An event of <c>allLiquidation</c>: the side is that of the liquidated position, <c>Buy</c> for a long.</summary>
    public static JsonObject Liquidation(string symbol, string side, decimal price, decimal quantity, DateTimeOffset at)
    {
        return new JsonObject { ["T"] = at.ToUnixTimeMilliseconds(), ["s"] = symbol, ["S"] = side, ["v"] = Text(quantity), ["p"] = Text(price) };
    }

    /// <summary>A trade of <c>publicTrade</c>: the side is that of the taker; a block trade and a trade of the RPI orders come the same way.</summary>
    public static JsonObject Trade(string symbol, string side, decimal quantity, decimal price, DateTimeOffset at, bool block = false, bool rpi = false)
    {
        return new JsonObject
        {
            ["T"] = at.ToUnixTimeMilliseconds(),
            ["s"] = symbol,
            ["S"] = side,
            ["v"] = Text(quantity),
            ["p"] = Text(price),
            ["L"] = "PlusTick",
            ["i"] = Guid.NewGuid().ToString(),
            ["BT"] = block,
            ["RPI"] = rpi,
            ["seq"] = 1
        };
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private async Task AcceptAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new Connection(socket, context.Request.Path.Value ?? "");
        _connections[socket] = connection;
        var buffer = new byte[64 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, context.RequestAborted);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                await AnswerAsync(connection, JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))!.AsObject());
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // The connection broke: a drop of the test or the client went away.
        }
        finally
        {
            _connections.TryRemove(socket, out _);
        }
    }

    private async Task AnswerAsync(Connection connection, JsonObject request)
    {
        string op = request["op"]?.GetValue<string>() ?? "";
        string? requestId = request["req_id"]?.GetValue<string>();

        switch (op)
        {
            case "subscribe":
                string[] topics = [.. request["args"]!.AsArray().Select(topic => topic!.GetValue<string>())];

                if (topics.FirstOrDefault(RefusedTopics.ContainsKey) is { } refused)
                {
                    await connection.SendAsync(Answer("subscribe", false, $"error:handler not found,topic:{refused}", requestId));

                    break;
                }

                SubscribeSizes.Enqueue(topics.Length);

                foreach (string topic in topics)
                {
                    connection.Topics[topic] = true;
                }

                await connection.SendAsync(Answer("subscribe", true, "", requestId));

                break;
            case "unsubscribe":
                foreach (JsonNode? topic in request["args"]!.AsArray())
                {
                    connection.Topics.TryRemove(topic!.GetValue<string>(), out _);
                }

                await connection.SendAsync(Answer("unsubscribe", true, "", requestId));

                break;
            case "ping":
                await connection.SendAsync(Answer("ping", true, "pong", requestId));

                break;
        }
    }

    private static string Answer(string op, bool success, string message, string? requestId)
    {
        return new JsonObject { ["success"] = success, ["ret_msg"] = message, ["op"] = op, ["conn_id"] = "fake", ["req_id"] = requestId }.ToJsonString();
    }

    private sealed class Connection(WebSocket socket, string path)
    {
        private readonly SemaphoreSlim _sending = new SemaphoreSlim(1, 1);

        public string Path { get; } = path;

        public ConcurrentDictionary<string, bool> Topics { get; } = new ConcurrentDictionary<string, bool>();

        public async Task SendAsync(string message)
        {
            await _sending.WaitAsync();

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            finally
            {
                _sending.Release();
            }
        }
    }
}
