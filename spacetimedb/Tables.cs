using SpacetimeDB;

public static partial class Module {
  [SpacetimeDB.Table(Accessor = "player", Public = true)]
  public partial struct Player {

    [SpacetimeDB.PrimaryKey]
    public Identity Identity;
    [SpacetimeDB.Index.BTree]
    public string Name;
    public bool Online;

  }

  [SpacetimeDB.Type]
  public enum SessionState : byte {
    Lobby,
    InProgress,
    Ended,
  }

  [SpacetimeDB.Table(Accessor = "game_session", Public = true)]
  public partial struct GameSession {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public Identity OwnerIdentity;
    public SessionState State;
    public uint MaxPlayers;
    public Timestamp CreatedAt;

  }

  [SpacetimeDB.Table(Accessor = "game_player", Public = true)]
  public partial struct GamePlayer {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    [SpacetimeDB.Index.BTree]
    public Identity PlayerIdentity;
    public byte TeamSlot;

  }
}