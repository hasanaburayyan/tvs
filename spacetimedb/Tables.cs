using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "player", Public = true)]
  public partial struct Player
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public Identity OwnerIdentity;

    [SpacetimeDB.Unique]
    public string Name;
    public bool Online;

    public Identity? ControllerIdentity;
  }

  [SpacetimeDB.Type]
  public enum SessionState : byte
  {
    Lobby,
    InProgress,
    Ended,
  }

  [SpacetimeDB.Type]
  public enum TerrainType : byte
  {
    Trench,
    Tree,
    Wall,
    Building,
    CommandCenter,
    Outpost,
    Trap,
    Fortification,
    MountedWeapon,
  }

  [SpacetimeDB.Table(Accessor = "game_session", Public = true)]
  public partial struct GameSession
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public Identity OwnerIdentity;
    public SessionState State;
    public uint MaxPlayers;
    public Timestamp CreatedAt;
    public uint MapSeed;

    [SpacetimeDB.Default(15u)]
    public uint RespawnTimerSeconds;
  }


  [SpacetimeDB.Table(Accessor = "game_player", Public = true)]
  public partial struct GamePlayer
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    [SpacetimeDB.Index.BTree]
    public ulong PlayerId;
    public byte TeamSlot;
    public bool Active;

    public int Health;
    public int MaxHealth;
    public int Armor;

    public DbVector3 Position;
    public float RotationY;

    public ulong? TargetGamePlayerId;

    [SpacetimeDB.Default(false)]
    public bool Dead;
    public Timestamp? DiedAt;
  }

  [Table(Name = "chat_session", Public = true)]
  public partial struct ChatSession
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public Timestamp TimeCreated;

    [SpacetimeDB.Index.BTree]
    public ulong OwnerPlayerId;
    [SpacetimeDB.Index.BTree]
    public string Name;
    public bool Active;
  }

  [Table(Name = "message", Public = true)]
  public partial struct Message
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public string Body;
    [SpacetimeDB.Index.BTree]
    public ulong SessionId;
    [SpacetimeDB.Index.BTree]
    public ulong SenderPlayerId;
    public bool Deleted;
    public Timestamp TimeSent;
  }

  [SpacetimeDB.Table(Accessor = "corpse", Public = true)]
  public partial struct Corpse
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;

    public ulong? GamePlayerId;
    public ulong? SoldierId;

    public ulong PlayerId;
    public DbVector3 Position;
    public float RotationY;
  }

  [Table(Name = "chat_session_player", Public = true)]
  public partial struct ChatSessionPlayer
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong PlayerId;
    [SpacetimeDB.Index.BTree]
    public ulong SessionId;
  }
}