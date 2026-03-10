using Godot;
using System;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class PlayerManager : Node
{
  private static readonly PackedScene PlayerScene = GD.Load<PackedScene>("uid://c0px8gi5tvei1");

  public ulong GameId{get; set;} = 0;

  [Export]
  public Node PlayerSpawnPath;

  public void LoadLobby() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	foreach (var gamePlayer in conn.Db.GamePlayer.GameSessionId.Filter(GameId)) {
	  SpawnPlayer(gamePlayer);
	}

	conn.Db.GamePlayer.OnInsert += OnGamePlayerInsert;
	conn.Db.GamePlayer.OnDelete += OnGamePlayerDelete;
	conn.Db.GamePlayer.OnUpdate += OnGamePlayerUpdate;
	conn.Db.PositionOverride.OnInsert += OnPositionOverrideInsert;
  }

  public void SpawnPlayer(GamePlayer gamePlayer) {
	var owner = SpacetimeNetworkManager.Instance.Conn.Db.Player.Identity.Find(gamePlayer.PlayerIdentity);
	if (owner is null)
	{
	  GD.PrintErr($"SpawnPlayer: Player record not found for identity {gamePlayer.PlayerIdentity}, skipping spawn");
	  return;
	}

	var player = PlayerScene.Instantiate<Player>();
	player.Name = gamePlayer.PlayerIdentity.ToString();
	player.OwnerIdentity = gamePlayer.PlayerIdentity;
	player.GameId = GameId;
	player.Position = new Vector3(gamePlayer.Position.X, gamePlayer.Position.Y, gamePlayer.Position.Z);
	player.username = owner.Name;
	PlayerSpawnPath.AddChild(player);
  }

  public void OnGamePlayerInsert(EventContext ctx, GamePlayer gamePlayer) {
	if (gamePlayer.GameSessionId != GameId) {
	  return;
	}
	SpawnPlayer(gamePlayer);
  }

  public void OnGamePlayerDelete(EventContext ctx, GamePlayer gamePlayer) {
	if (gamePlayer.GameSessionId != GameId) {
	  return;
	}
	RemovePlayer(gamePlayer);
  }

  private void RemovePlayer(GamePlayer gamePlayer) {
	var player = PlayerSpawnPath.GetNode<Player>(gamePlayer.PlayerIdentity.ToString());
	if (player != null) {
	  player.QueueFree();
	if (player.OwnerIdentity == SpacetimeNetworkManager.Instance.Conn.Identity) {
		DestroyLobby();
	  }
	}
  }

  public void OnGamePlayerUpdate(EventContext ctx, GamePlayer oldGamePlayer, GamePlayer newGamePlayer) {
	if (oldGamePlayer.GameSessionId != GameId && newGamePlayer.GameSessionId != GameId) {
	  return;
	}

	// if left
	if (oldGamePlayer.GameSessionId == GameId && newGamePlayer.GameSessionId != GameId) {
	  RemovePlayer(oldGamePlayer);
	}

	// if joined
	if (oldGamePlayer.GameSessionId != GameId && newGamePlayer.GameSessionId == GameId) {
	  SpawnPlayer(newGamePlayer);
	}

	// if moved
	if (oldGamePlayer.GameSessionId == GameId && newGamePlayer.GameSessionId == GameId) {
	  var player = PlayerSpawnPath.GetNode<Player>(oldGamePlayer.PlayerIdentity.ToString());
	  if (player != null) {
		player.OnPositionUpdated(new Vector3(newGamePlayer.Position.X, newGamePlayer.Position.Y, newGamePlayer.Position.Z));
	  }
	}
  }

  public void OnPositionOverrideInsert(EventContext ctx, PositionOverride posOverride) {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	if (posOverride.PlayerIdentity != conn.Identity) {
	  return;
	}
	if (posOverride.GameSessionId != GameId) {
	  return;
	}

	var player = PlayerSpawnPath.GetNode<Player>(posOverride.PlayerIdentity.ToString());
	if (player != null) {
	  player.ApplyPositionOverride(new Vector3(posOverride.Position.X, posOverride.Position.Y, posOverride.Position.Z));
	}

	conn.Reducers.AckPositionOverride(posOverride.Id);
  }

  public void DestroyLobby() {
	foreach (var player in PlayerSpawnPath.GetChildren()) {
	  if (player is Player) {
		PlayerSpawnPath.RemoveChild(player);
	  }
	}
  }
}
