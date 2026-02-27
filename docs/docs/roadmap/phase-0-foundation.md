---
sidebar_position: 2
title: "Phase 0: Foundation"
---

# Phase 0 -- Foundation (Week 1)

**Goal:** Replace the boilerplate with a real schema, get a client connecting, and prove the full round-trip works.

**Dependencies:** None -- this is the starting point.

---

## Backend Tasks (0A)

Replace the example `Person` table in `spacetimedb/Lib.cs` with the foundational game tables.

### Tables

**Player**
- `Identity` primary key (the connected user's identity)
- `string` display name
- Faction enum (`Entente` / `Central`)
- `bool` online status

**GameSession**
- `ulong` auto-increment primary key
- State enum (`Lobby` / `InProgress` / `Ended`)
- `uint` max players
- `Timestamp` created at

**GamePlayer**
- `ulong` auto-increment primary key
- `ulong` game session ID (indexed)
- `Identity` player identity (indexed)
- `byte` team slot

### Reducers

| Reducer | Parameters | Behavior |
|---------|-----------|----------|
| `RegisterPlayer` | `string name, byte faction` | Insert or update a `Player` row for `ctx.Sender` |
| `CreateGame` | `uint maxPlayers` | Insert a `GameSession` in `Lobby` state, add creator as first `GamePlayer` |
| `JoinGame` | `ulong gameId` | Validate game is in `Lobby` and not full, insert `GamePlayer` |
| `LeaveGame` | `ulong gameId` | Delete the caller's `GamePlayer` row; if empty, delete the session |
| `StartGame` | `ulong gameId` | Validate caller is in the game and enough players joined; set state to `InProgress` |

### Schema sketch

```csharp
[SpacetimeDB.Type]
public enum Faction : byte { Entente, Central }

[SpacetimeDB.Type]
public enum GameState : byte { Lobby, InProgress, Ended }

[SpacetimeDB.Table(Accessor = "Player", Public = true)]
public partial struct Player
{
    [SpacetimeDB.PrimaryKey]
    public Identity Identity;

    public string Name;
    public Faction Faction;
    public bool Online;
}

[SpacetimeDB.Table(Accessor = "GameSession", Public = true)]
public partial struct GameSession
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public GameState State;
    public uint MaxPlayers;
    public Timestamp CreatedAt;
}

[SpacetimeDB.Table(Accessor = "GamePlayer", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "by_game", Columns = new[] { "GameId" })]
[SpacetimeDB.Index.BTree(Accessor = "by_player", Columns = new[] { "PlayerIdentity" })]
public partial struct GamePlayer
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public ulong GameId;
    public Identity PlayerIdentity;
    public byte TeamSlot;
}
```

---

## Client Tasks (0B)

### Unity project setup

1. Create a Unity project in `client-unity/`
2. Add the SpacetimeDB Unity SDK
3. Generate C# bindings:
   ```bash
   spacetime generate --lang csharp \
     --out-dir client-unity/Assets/SpacetimeDB \
     --module-path ./spacetimedb
   ```

### Connection

- Wire up `DbConnection.Builder()` with the module URI and name
- Subscribe to `Player`, `GameSession`, and `GamePlayer` tables inside `OnConnect`
- Call `FrameTick()` every frame in `Update()`

### Lobby UI

- Text field for player name + faction dropdown + "Register" button
- List of open `GameSession` rows in `Lobby` state
- "Create Game" and "Join" buttons
- Player list for the current game session (from `GamePlayer` joined with `Player`)
- "Start Game" button (enabled when enough players have joined)

---

## Deliverable

Two players can register, create a game, join it, and see each other in the lobby. The `StartGame` reducer transitions the session to `InProgress`.
