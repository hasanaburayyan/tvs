using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using SpacetimeDB.Types;

public class SpacetimeTestClient : IDisposable
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("STDB_TEST_URL") ?? "http://localhost:3001";
    private static readonly string DefaultDb =
        Environment.GetEnvironmentVariable("STDB_TEST_DB") ?? "tvs";

    private readonly DbConnection _conn;
    private readonly HttpClient _http;
    private readonly string _dbName;

    private bool _reducerComplete;
    private Status? _lastReducerStatus;

    /// The client cache — read tables directly: Db.Player.Iter(), Db.GameSession.Id.Find(id), etc.
    public RemoteTables Db => _conn.Db;

    /// Call reducers directly: Reducers.CreatePlayer("Alice"), Reducers.JoinGame(id), etc.
    public RemoteReducers Reducers => _conn.Reducers;

    /// The identity assigned to this connection (each Create() gets a unique one).
    public Identity Identity => _conn.Identity!.Value;

    private SpacetimeTestClient(DbConnection conn, HttpClient http, string dbName)
    {
        _conn = conn;
        _http = http;
        _dbName = dbName;

        conn.Reducers.OnCreatePlayer += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnCreateGame += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnDeletePlayer += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnDeleteGame += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnJoinGame += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnLeaveGame += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnClearData += (ctx) => HandleReducerEvent(ctx);
        conn.Reducers.OnSelectPlayer += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnDeselectPlayer += (ctx) => HandleReducerEvent(ctx);
        conn.Reducers.OnCreateChatSession += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnRemoveChatSessionByName += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnRemoveChatSessionById += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnJoinChatSession += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnLeaveChatSession += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnSendMessage += (ctx, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnSoftDeleteMessage += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnHardDeleteMessage += (ctx, _) => HandleReducerEvent(ctx);
    }

    private void HandleReducerEvent(ReducerEventContext ctx)
    {
        if (ctx.Event.CallerIdentity == _conn.Identity)
        {
            _lastReducerStatus = ctx.Event.Status;
            _reducerComplete = true;
        }
    }

    /// Creates a test client with a fresh anonymous identity.
    /// Connects, subscribes to all tables, and blocks until ready.
    public static SpacetimeTestClient Create(string? dbName = null)
    {
        dbName ??= DefaultDb;
        bool subscribed = false;
        string? token = null;

        var conn = DbConnection.Builder()
            .WithUri(BaseUrl)
            .WithDatabaseName(dbName)
            .OnConnect((c, identity, tok) =>
            {
                token = tok;
                c.SubscriptionBuilder()
                    .OnApplied(_ => { subscribed = true; })
                    .SubscribeToAllTables();
            })
            .OnConnectError(err => throw new Exception($"SpacetimeDB connect failed: {err}"))
            .OnDisconnect((c, err) => { })
            .Build();

        PumpUntil(conn, () => subscribed);

        var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        if (token != null)
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return new SpacetimeTestClient(conn, http, dbName);
    }

    // ── Call & CallExpectFailure ─────────────────────────────────────────────

    /// Call a reducer and wait for it to commit. Throws if the reducer fails.
    public void Call(Action<RemoteReducers> action)
    {
        _lastReducerStatus = null;
        _reducerComplete = false;
        action(Reducers);
        PumpUntil(_conn, () => _reducerComplete);
        var status = _lastReducerStatus!;
        if (status is Status.Failed(var reason))
            throw new Exception($"Reducer failed: {reason}");
        if (status is not Status.Committed)
            throw new Exception($"Reducer unexpected status: {status}");
    }

    /// Call a reducer and wait for it to fail. Throws if the reducer succeeds.
    /// Returns the failure reason string.
    public string CallExpectFailure(Action<RemoteReducers> action)
    {
        _lastReducerStatus = null;
        _reducerComplete = false;
        action(Reducers);
        PumpUntil(_conn, () => _reducerComplete);
        var status = _lastReducerStatus!;
        if (status is Status.Failed(var reason)) return reason;
        throw new Exception($"Expected reducer to fail, but got: {status}");
    }

    // ── Convenience helpers ─────────────────────────────────────────────────

    /// Creates a player and returns its ulong Id.
    public ulong CreatePlayerAndGetId(string name)
    {
        Call(r => r.CreatePlayer(name));
        var player = Db.Player.Name.Find(name)
            ?? throw new Exception($"CreatePlayer succeeded but player '{name}' not found in client cache");
        return player.Id;
    }

    /// Creates a game and returns its ID by finding the session owned by this client.
    public ulong CreateGame(uint maxPlayers)
    {
        Call(r => r.CreateGame(maxPlayers));
        var game = Db.GameSession.OwnerIdentity.Filter(Identity).MaxBy(g => g.Id);
        if (game == null) throw new Exception("CreateGame succeeded but game not found in client cache");
        return game.Id;
    }

    /// Calls ClearData and waits for it to commit.
    public void ClearData()
    {
        Call(r => r.ClearData());
    }

    // ── SQL (escape hatch for assertions the SDK can't express) ─────────────

    public string Sql(string query)
    {
        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = _http.PostAsync($"/v1/database/{_dbName}/sql", content).Result;
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().Result;
    }

    public JsonElement[] SqlRows(string query)
    {
        var json = JsonDocument.Parse(Sql(query));
        var results = json.RootElement;
        if (results.GetArrayLength() == 0) return Array.Empty<JsonElement>();
        var firstStatement = results[0];
        if (!firstStatement.TryGetProperty("rows", out var rows)) return Array.Empty<JsonElement>();
        var list = new JsonElement[rows.GetArrayLength()];
        for (int i = 0; i < list.Length; i++) list[i] = rows[i];
        return list;
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private static void PumpUntil(DbConnection conn, Func<bool> condition, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            conn.FrameTick();
            Thread.Sleep(1);
        }
        if (!condition())
            throw new TimeoutException($"SpacetimeDB operation timed out after {timeoutMs}ms");
    }

    public void Dispose()
    {
        if (_conn.IsActive) _conn.Disconnect();
        _http.Dispose();
    }
}
