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
        conn.Reducers.OnCreateGame += (ctx, _, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnCreateGameAndJoin += (ctx, _, _, _) => HandleReducerEvent(ctx);
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
        conn.Reducers.OnKickPlayerFromGame += (ctx, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnMovePlayer += (ctx, _, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnTeleportPlayer += (ctx, _, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnSetLoadout += (ctx, _, _, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnUseAbility += (ctx, _, _, _, _, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnSetTarget += (ctx, _, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnRespawn += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnStartGame += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnEndGame += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnSplitOwnedSquads += (ctx, _) => HandleReducerEvent(ctx);
        conn.Reducers.OnSetTeam += (ctx, _, _) => HandleReducerEvent(ctx);
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

    public ulong GetDefaultMapId()
    {
        var map = Db.MapDef.Iter().FirstOrDefault();
        if (map == null) throw new Exception("No MapDef found — Init may not have run");
        return map.Id;
    }

    /// Creates a game and returns its ID by finding the session owned by this client.
    public ulong CreateGame(uint maxPlayers, uint? respawnTimerSeconds = null)
    {
        var mapId = GetDefaultMapId();
        Call(r => r.CreateGame(maxPlayers, respawnTimerSeconds, mapId));
        var game = Db.GameSession.OwnerIdentity.Filter(Identity).MaxBy(g => g.Id);
        if (game == null) throw new Exception("CreateGame succeeded but game not found in client cache");
        return game.Id;
    }

    /// Creates a game, joins it, and returns the game session ID.
    public ulong CreateGameAndJoin(uint maxPlayers, uint? respawnTimerSeconds = null)
    {
        var mapId = GetDefaultMapId();
        Call(r => r.CreateGameAndJoin(maxPlayers, respawnTimerSeconds, mapId));
        var game = Db.GameSession.OwnerIdentity.Filter(Identity).MaxBy(g => g.Id);
        if (game == null) throw new Exception("CreateGameAndJoin succeeded but game not found");
        return game.Id;
    }

    /// Returns the GamePlayer row for this client's active player in the given game.
    public SpacetimeDB.Types.GamePlayer GetGamePlayer(ulong gameId)
    {
        var player = Db.Player.Iter().FirstOrDefault(p => p.ControllerIdentity == Identity);
        if (player == null) throw new Exception("No active player");
        foreach (var entity in Db.Entity.GameSessionId.Filter(gameId))
        {
            if (entity.Type != EntityType.GamePlayer) continue;
            var gp = Db.GamePlayer.EntityId.Find(entity.EntityId);
            if (gp != null && gp.PlayerId == player.Id && gp.Active)
                return gp;
        }
        throw new Exception("No active GamePlayer found in session");
    }

    // ── Entity helpers (for ECS-based lookups) ────────────────────────────────

    public System.Collections.Generic.IEnumerable<SpacetimeDB.Types.GamePlayer> GamePlayersInSession(ulong gameId)
    {
        foreach (var entity in Db.Entity.GameSessionId.Filter(gameId))
        {
            if (entity.Type != EntityType.GamePlayer) continue;
            var gp = Db.GamePlayer.EntityId.Find(entity.EntityId);
            if (gp != null) yield return gp;
        }
    }

    public System.Collections.Generic.IEnumerable<SpacetimeDB.Types.Soldier> SoldiersInSession(ulong gameId)
    {
        foreach (var entity in Db.Entity.GameSessionId.Filter(gameId))
        {
            if (entity.Type != EntityType.Soldier) continue;
            var s = Db.Soldier.EntityId.Find(entity.EntityId);
            if (s != null) yield return s;
        }
    }

    public System.Collections.Generic.IEnumerable<SpacetimeDB.Types.TerrainFeature> TerrainInSession(ulong gameId)
    {
        foreach (var entity in Db.Entity.GameSessionId.Filter(gameId))
        {
            if (entity.Type != EntityType.Terrain) continue;
            var t = Db.TerrainFeature.EntityId.Find(entity.EntityId);
            if (t != null) yield return t;
        }
    }

    public System.Collections.Generic.IEnumerable<SpacetimeDB.Types.Corpse> CorpsesInSession(ulong gameId)
    {
        foreach (var entity in Db.Entity.GameSessionId.Filter(gameId))
        {
            if (entity.Type != EntityType.Corpse) continue;
            var c = Db.Corpse.EntityId.Find(entity.EntityId);
            if (c != null) yield return c;
        }
    }

    public SpacetimeDB.Types.Targetable GetTargetable(ulong entityId)
    {
        return Db.Targetable.EntityId.Find(entityId)
            ?? throw new Exception($"Targetable not found for entity {entityId}");
    }

    public SpacetimeDB.Types.Entity GetEntity(ulong entityId)
    {
        return Db.Entity.EntityId.Find(entityId)
            ?? throw new Exception($"Entity not found: {entityId}");
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

    /// Pumps the connection briefly to receive pending broadcasts from other clients.
    public void Sync(int ms = 200)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            _conn.FrameTick();
            Thread.Sleep(1);
        }
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
