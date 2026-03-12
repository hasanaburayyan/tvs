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
	  if (gamePlayer.Active) SpawnPlayer(gamePlayer);
	}

	conn.Db.GamePlayer.OnInsert += OnGamePlayerInsert;
	conn.Db.GamePlayer.OnDelete += OnGamePlayerDelete;
	conn.Db.GamePlayer.OnUpdate += OnGamePlayerUpdate;
	conn.Db.PositionOverride.OnInsert += OnPositionOverrideInsert;
  }

  public void SpawnPlayer(GamePlayer gamePlayer) {
	var owner = SpacetimeNetworkManager.Instance.Conn.Db.Player.Id.Find(gamePlayer.PlayerId);
	if (owner is null)
	{
	  GD.PrintErr($"SpawnPlayer: Player record not found for PlayerId {gamePlayer.PlayerId}, skipping spawn");
	  return;
	}

	var player = PlayerScene.Instantiate<Player>();
	player.Name = gamePlayer.PlayerId.ToString();
	player.PlayerId = gamePlayer.PlayerId;
	player.GamePlayerId = gamePlayer.Id;
	player.GameId = GameId;
	player.Position = new Vector3(gamePlayer.Position.X, gamePlayer.Position.Y, gamePlayer.Position.Z);
	player.Rotation = new Vector3(0, gamePlayer.RotationY, 0);
	player.username = owner.Name;
	PlayerSpawnPath.AddChild(player);
  }

  public void OnGamePlayerInsert(EventContext ctx, GamePlayer gamePlayer) {
	if (gamePlayer.GameSessionId != GameId || !gamePlayer.Active) {
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
	var player = PlayerSpawnPath.GetNodeOrNull<Player>(gamePlayer.PlayerId.ToString());
	if (player != null) {
	  player.QueueFree();
	  if (player.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId) {
		DestroyLobby();
	  }
	}
  }

  public void OnGamePlayerUpdate(EventContext ctx, GamePlayer oldGamePlayer, GamePlayer newGamePlayer) {
	if (newGamePlayer.GameSessionId != GameId) {
	  return;
	}

	if (oldGamePlayer.Active && !newGamePlayer.Active) {
	  RemovePlayer(oldGamePlayer);
	  return;
	}

	if (!oldGamePlayer.Active && newGamePlayer.Active) {
	  SpawnPlayer(newGamePlayer);
	  return;
	}

	if (newGamePlayer.Active) {
	  var player = PlayerSpawnPath.GetNodeOrNull<Player>(newGamePlayer.PlayerId.ToString());
	  if (player != null) {
		player.OnStateUpdated(
		  new Vector3(newGamePlayer.Position.X, newGamePlayer.Position.Y, newGamePlayer.Position.Z),
		  newGamePlayer.RotationY
		);
	  }
	}
  }

  public void OnPositionOverrideInsert(EventContext ctx, PositionOverride posOverride) {
	var mgr = SpacetimeNetworkManager.Instance;
	if (posOverride.PlayerId != mgr.ActivePlayerId) {
	  return;
	}
	if (posOverride.GameSessionId != GameId) {
	  return;
	}

	var player = PlayerSpawnPath.GetNodeOrNull<Player>(posOverride.PlayerId.ToString());
	if (player != null) {
	  player.ApplyPositionOverride(new Vector3(posOverride.Position.X, posOverride.Position.Y, posOverride.Position.Z));
	}

	mgr.Conn.Reducers.AckPositionOverride(posOverride.Id);
  }

  public void DestroyLobby() {
	foreach (var player in PlayerSpawnPath.GetChildren()) {
	  if (player is Player) {
		PlayerSpawnPath.RemoveChild(player);
	  }
	}
  }
}
