using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using SnakeClub.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.IncludeFields = true);
bool production = string.Equals(Environment.GetEnvironmentVariable("SNAKE_ENV"), "production", StringComparison.OrdinalIgnoreCase);
string? rateValue = Environment.GetEnvironmentVariable("SNAKE_RATE_LIMIT_PER_MIN");
if (!string.IsNullOrEmpty(rateValue) && (!int.TryParse(rateValue, out var configuredLimit) || configuredLimit < 1 || configuredLimit > 10000))
    throw new InvalidOperationException("SNAKE_RATE_LIMIT_PER_MIN must be between 1 and 10000.");
int rateLimit = string.IsNullOrEmpty(rateValue) ? (production ? 120 : 0) : int.Parse(rateValue);
if (rateLimit > 0)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellation) =>
            await context.HttpContext.Response.WriteAsJsonAsync(new { ok = false, error = "请求过于频繁，请稍后再试" }, cancellation);
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.Request.Path == "/health" ? RateLimitPartition.GetNoLimiter("health") :
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = rateLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    });
}
var allowedOrigins = (Environment.GetEnvironmentVariable("SNAKE_ALLOWED_ORIGINS") ?? "http://127.0.0.1:8082,http://localhost:8082").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("game", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
string host = Environment.GetEnvironmentVariable("SNAKE_HOST") ?? "127.0.0.1";
string port = Environment.GetEnvironmentVariable("SNAKE_PORT") ?? "2567";
builder.WebHost.UseUrls($"http://{host}:{port}");
var app = builder.Build();
var rooms = new RoomManager();
app.UseCors("game");
if (rateLimit > 0) app.UseRateLimiter();
GameApi.Map(app, builder.Environment.ContentRootPath);
app.MapGet("/health", () => Results.Json(new { ok = true, service = "snake-club", version = "0.0.3.1" }));
// Realtime rooms are a development capability, not part of the first
// production release. In particular the room service does not yet have
// production authentication or bounded connection/room capacity.
if (!production)
{
    app.UseWebSockets();
    app.Map("/ws", async context =>
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await rooms.Handle(socket, context.RequestAborted);
    });
}
using var stop = new CancellationTokenSource();
var loop = rooms.Run(stop.Token);
var web = app.RunAsync();
try { await await Task.WhenAny(web, loop); }
finally
{
    stop.Cancel();
    await app.StopAsync();
    try { await loop; } catch (OperationCanceledException) { }
}

sealed class ClientMessage
{
    public string? type { get; set; }
    public string? room { get; set; }
    public string? mode { get; set; }
    public string? name { get; set; }
    public float x { get; set; }
    public float y { get; set; }
    public bool boost { get; set; }
}

sealed class ServerMessage
{
    public string type { get; set; } = "";
    public string? error { get; set; }
    public string? room { get; set; }
    public string? mode { get; set; }
    public string? playerId { get; set; }
    public bool fruitsIncluded { get; set; }
    public ArenaSnapshot? state { get; set; }
}

sealed class ArenaSnapshot
{
    public float halfSize;
    public float remaining;
    public string phase;
    public string winner;
    public int tick;
    public bool starVisible;
    public Point starPosition;
    public string starHolder;
    public float starProgress;
    public List<SnakeState> snakes;
    public List<FruitState>? fruits;

    public ArenaSnapshot(ArenaState source, bool includeFruits)
    {
        halfSize = source.halfSize;
        remaining = source.remaining;
        phase = source.phase;
        winner = source.winner;
        tick = source.tick;
        starVisible = source.starVisible;
        starPosition = source.starPosition;
        starHolder = source.starHolder;
        starProgress = source.starProgress;
        snakes = source.snakes;
        fruits = includeFruits ? source.fruits : null;
    }
}

sealed class Peer
{
    public readonly string Id = Guid.NewGuid().ToString("N")[..12];
    public readonly WebSocket Socket;
    public string Name = "Player";
    public Room? Room;
    public long LastInput;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private int statePending;

    public Peer(WebSocket socket) { Socket = socket; }

    public async Task Send(string data, CancellationToken token)
    {
        await sendLock.WaitAsync(token);
        try
        {
            if (Socket.State == WebSocketState.Open)
                await Socket.SendAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text, true, token);
        }
        catch (WebSocketException) { }
        catch (ObjectDisposedException) { }
        finally { sendLock.Release(); }
    }

    public void SendState(string data, CancellationToken token)
    {
        if (Interlocked.Exchange(ref statePending, 1) != 0) return;
        _ = SendStateAsync(data, token);
    }

    private async Task SendStateAsync(string data, CancellationToken token)
    {
        try { await Send(data, token); }
        catch (OperationCanceledException) { }
        finally { Interlocked.Exchange(ref statePending, 0); }
    }
}

sealed class Room
{
    public readonly object Gate = new();
    public readonly string Code;
    public readonly string Mode;
    public readonly List<Peer> Peers = new();
    public Arena Arena;
    public int LastFruitRevision = -1;
    private int generation;

    public Room(string code, string mode) { Code = code; Mode = mode; Arena = NewArena(); }

    private Arena NewArena() => new(Random.Shared.Next());

    public void Add(Peer peer)
    {
        Peers.Add(peer);
        peer.Room = this;
        Arena.AddSnake(peer.Id, peer.Name, false);
        if (Mode == "mixed" && Peers.Count == 1 && Arena.State.phase == "waiting") AddBots();
        Arena.Start();
    }

    private void AddBots()
    {
        for (int i = 0; i < 7; i++) Arena.AddSnake($"bot-{generation}-{i}", $"AI {i + 1}", true);
    }

    public void Restart()
    {
        if (Arena.State.phase != "ended") return;
        generation++;
        Arena = NewArena();
        LastFruitRevision = -1;
        foreach (var peer in Peers) Arena.AddSnake(peer.Id, peer.Name, false);
        if (Mode == "mixed") AddBots();
        Arena.Start();
    }
}

sealed class RoomManager
{
    private const int MaxPlayers = 8;
    private readonly ConcurrentDictionary<string, Room> rooms = new();
    // Match Unity JsonUtility's field-only arena representation. In particular,
    // Point.Normalized is a computed property and must never be serialized.
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, PropertyNameCaseInsensitive = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task Handle(WebSocket socket, CancellationToken token)
    {
        var peer = new Peer(socket);
        byte[] buffer = new byte[4096];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 8192)
                {
                    await peer.Send(Encode(new ServerMessage { type = "error", error = "消息格式或大小无效" }), token);
                    break;
                }
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                ClientMessage? request;
                try { request = JsonSerializer.Deserialize<ClientMessage>(message.ToArray(), JsonOptions); }
                catch (JsonException) { request = null; }
                message.SetLength(0);
                if (request == null) { await peer.Send(Encode(new ServerMessage { type = "error", error = "无法解析消息" }), token); continue; }
                await Dispatch(peer, request, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            var room = peer.Room;
            if (room != null)
            {
                lock (room.Gate)
                {
                    room.Peers.Remove(peer);
                    room.Arena.RemoveSnake(peer.Id);
                    peer.Room = null;
                    if (room.Peers.Count == 0) rooms.TryRemove(new KeyValuePair<string, Room>(room.Code, room));
                }
            }
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closeTimeout.Token); }
                catch (WebSocketException) { }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task Dispatch(Peer peer, ClientMessage request, CancellationToken token)
    {
        if (request.type == "create" || request.type == "join")
        {
            if (peer.Room != null) { await Error(peer, "已经在房间中", token); return; }
            string mode = request.mode == "mixed" ? "mixed" : request.mode == "pvp" ? "pvp" : "";
            if (mode == "") { await Error(peer, "请选择 PvP 或混合模式", token); return; }
            peer.Name = CleanName(request.name);
            Room? room;
            if (request.type == "create")
            {
                do { room = new Room(NewCode(), mode); } while (!rooms.TryAdd(room.Code, room));
            }
            else
            {
                string code = (request.room ?? "").Trim().ToUpperInvariant();
                if (!rooms.TryGetValue(code, out room)) { await Error(peer, "房间不存在", token); return; }
                if (room.Mode != mode) { await Error(peer, "房间模式不匹配", token); return; }
            }
            string? error = null;
            lock (room.Gate)
            {
                if (!rooms.TryGetValue(room.Code, out var current) || !ReferenceEquals(room, current)) error = "房间已关闭";
                else if (room.Peers.Count >= MaxPlayers) error = "房间已满";
                else room.Add(peer);
            }
            if (error != null) { await Error(peer, error, token); return; }
            await peer.Send(Encode(new ServerMessage { type = "joined", playerId = peer.Id, room = room.Code, mode = room.Mode }), token);
            return;
        }
        var joined = peer.Room;
        if (joined == null) { await Error(peer, "请先创建或加入房间", token); return; }
        if (request.type == "input")
        {
            long now = Environment.TickCount64;
            if (now - peer.LastInput < 25) return;
            peer.LastInput = now;
            lock (joined.Gate) joined.Arena.SetInput(peer.Id, new Point(request.x, request.y), request.boost);
        }
        else if (request.type == "restart") { lock (joined.Gate) joined.Restart(); }
        else await Error(peer, "未知消息类型", token);
    }

    public async Task Run(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        int tick = 0;
        while (await timer.WaitForNextTickAsync(token))
        {
            tick++;
            foreach (var room in rooms.Values)
            {
                string? state = null;
                Peer[] peers;
                lock (room.Gate)
                {
                    room.Arena.Tick(0.05f);
                    peers = room.Peers.ToArray();
                    if (tick % 2 == 0)
                    {
                        // Fruit usually does not change between ticks. Send it when
                        // collected and at least once per second for new joiners.
                        bool includeFruits = tick % 20 == 0 || room.LastFruitRevision != room.Arena.FruitRevision;
                        if (includeFruits) room.LastFruitRevision = room.Arena.FruitRevision;
                        state = Encode(new ServerMessage { type = "state", room = room.Code, mode = room.Mode,
                            fruitsIncluded = includeFruits, state = new ArenaSnapshot(room.Arena.State, includeFruits) });
                    }
                }
                if (state != null) foreach (var peer in peers) peer.SendState(state, token);
            }
        }
    }

    private static string Encode(ServerMessage message) => JsonSerializer.Serialize(message, JsonOptions);
    private static Task Error(Peer peer, string message, CancellationToken token) => peer.Send(Encode(new ServerMessage { type = "error", error = message }), token);
    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[5];
        for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
    }
    private static string CleanName(string? value)
    {
        string name = new string((value ?? "").Trim().Where(c => !char.IsControl(c)).Take(16).ToArray());
        return name.Length == 0 ? "Player" : name;
    }
}
