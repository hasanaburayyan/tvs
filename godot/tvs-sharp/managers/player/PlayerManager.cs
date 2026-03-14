using Godot;
using System;
using System.Linq;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class PlayerManager : Node
{
  private static readonly PackedScene PlayerScene = GD.Load<PackedScene>("uid://c0px8gi5tvei1");
  private static readonly PackedScene DamageNumberScene = GD.Load<PackedScene>("res://scenes/hud/hud elements/damage_number.tscn");

  public ulong GameId{get; set;} = 0;

  [Export]
  public Node PlayerSpawnPath;

  [Export]
  public Node CorpseContainer;

  [Signal]
  public delegate void LocalPlayerDiedEventHandler(ulong gameSessionId, long diedAtMicros, uint respawnTimerSeconds);

  [Signal]
  public delegate void LocalPlayerRevivedEventHandler();

  [Signal]
  public delegate void PlayerKilledEventHandler(string killerName, string victimName, byte killerTeam, byte victimTeam);

  public void LoadLobby() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	foreach (var gamePlayer in conn.Db.GamePlayer.GameSessionId.Filter(GameId)) {
	  if (gamePlayer.Active) SpawnPlayer(gamePlayer);
	}

	foreach (var corpse in conn.Db.Corpse.GameSessionId.Filter(GameId))
	  SpawnCorpse(corpse);

	conn.Db.GamePlayer.OnInsert += OnGamePlayerInsert;
	conn.Db.GamePlayer.OnDelete += OnGamePlayerDelete;
	conn.Db.GamePlayer.OnUpdate += OnGamePlayerUpdate;
	conn.Db.PositionOverride.OnInsert += OnPositionOverrideInsert;
	conn.Db.BattleLog.OnInsert += OnBattleLogInsert;
	conn.Db.Corpse.OnInsert += OnCorpseInsert;
	conn.Db.Corpse.OnDelete += OnCorpseDelete;
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

	player.SetTeamColor(gamePlayer.TeamSlot);

	if (gamePlayer.Dead)
	  player.PlayDeath();
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
		if (!oldGamePlayer.Dead && newGamePlayer.Dead)
		{
		  player.PlayDeath();

		  var conn = SpacetimeNetworkManager.Instance.Conn;
		  string victimName = conn.Db.Player.Id.Find(newGamePlayer.PlayerId)?.Name ?? "Unknown";
		  string killerName = "Unknown";
		  byte killerTeam = 0;

		  foreach (var log in conn.Db.BattleLog.Iter())
		  {
			if (log.GameSessionId != GameId) continue;
			if (!log.TargetGamePlayerIds.Contains(newGamePlayer.Id)) continue;
			var ability = conn.Db.AbilityDef.Id.Find(log.AbilityId);
			if (ability?.Type != AbilityType.Damage) continue;

			var actorGp = conn.Db.GamePlayer.Id.Find(log.ActorGamePlayerId);
			if (actorGp != null)
			{
			  killerName = conn.Db.Player.Id.Find(actorGp.PlayerId)?.Name ?? "Unknown";
			  killerTeam = actorGp.TeamSlot;
			}
		  }

		  EmitSignal(SignalName.PlayerKilled, killerName, victimName, killerTeam, newGamePlayer.TeamSlot);

		  if (newGamePlayer.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId)
		  {
			var session = conn.Db.GameSession.Id.Find(GameId);
			uint timer = session?.RespawnTimerSeconds ?? 15;
			long diedMicros = newGamePlayer.DiedAt?.MicrosecondsSinceUnixEpoch ?? 0;
			EmitSignal(SignalName.LocalPlayerDied, GameId, diedMicros, timer);
		  }
		}
		else if (oldGamePlayer.Dead && !newGamePlayer.Dead)
		{
		  player.Revive();
		  player.ApplyPositionOverride(
			new Vector3(newGamePlayer.Position.X, newGamePlayer.Position.Y, newGamePlayer.Position.Z)
		  );

		  if (newGamePlayer.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId)
			EmitSignal(SignalName.LocalPlayerRevived);
		}
		else
		{
		  player.OnStateUpdated(
			new Vector3(newGamePlayer.Position.X, newGamePlayer.Position.Y, newGamePlayer.Position.Z),
			newGamePlayer.RotationY
		  );
		}

		if (oldGamePlayer.TeamSlot != newGamePlayer.TeamSlot)
		  player.SetTeamColor(newGamePlayer.TeamSlot);
	  }

	  if (newGamePlayer.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId
		&& oldGamePlayer.TargetGamePlayerId != null
		&& newGamePlayer.TargetGamePlayerId == null)
	  {
		Targeting.Instance?.ClearTarget();
	  }
	}
  }

  private void SpawnCorpse(SpacetimeDB.Types.Corpse corpse)
  {
	var container = CorpseContainer ?? this;
	var node = new Corpse();
	node.Name = $"Corpse_{corpse.Id}";
	node.CorpseId = corpse.Id;
	node.GamePlayerId = corpse.GamePlayerId;
	node.PlayerId = corpse.PlayerId;

	var owner = SpacetimeNetworkManager.Instance.Conn.Db.Player.Id.Find(corpse.PlayerId);
	node.PlayerName = owner?.Name ?? "";

	node.Position = new Vector3(corpse.Position.X, corpse.Position.Y, corpse.Position.Z);
	node.Rotation = new Vector3(0, corpse.RotationY, 0);
	container.AddChild(node);
  }

  private void OnCorpseInsert(EventContext ctx, SpacetimeDB.Types.Corpse corpse)
  {
	if (corpse.GameSessionId != GameId) return;
	SpawnCorpse(corpse);
  }

  private void OnCorpseDelete(EventContext ctx, SpacetimeDB.Types.Corpse corpse)
  {
	if (corpse.GameSessionId != GameId) return;
	var container = CorpseContainer ?? this;
	var node = container.GetNodeOrNull<Corpse>($"Corpse_{corpse.Id}");
	node?.QueueFree();
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

  public void OnBattleLogInsert(EventContext ctx, BattleLogEntry entry) {
	if (entry.GameSessionId != GameId) return;
	if (entry.ResolvedPower == 0) return;

	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	var ability = conn.Db.AbilityDef.Id.Find(entry.AbilityId);
	bool isHeal = ability?.Type == AbilityType.Heal;

	foreach (var targetId in entry.TargetGamePlayerIds)
	{
	  var targetGp = conn.Db.GamePlayer.Id.Find(targetId);
	  if (targetGp == null) continue;

	  var playerNode = PlayerSpawnPath.GetNodeOrNull<Player>(targetGp.PlayerId.ToString());
	  if (playerNode == null) continue;

	  var dmgNum = DamageNumberScene.Instantiate<DamageNumber>();
	  dmgNum.Setup(entry.ResolvedPower, isHeal);
	  GetTree().Root.AddChild(dmgNum);
	  dmgNum.GlobalPosition = playerNode.GlobalPosition + new Vector3(0, 2.2f, 0);
	}
  }

  public void DestroyLobby() {
	foreach (var child in PlayerSpawnPath.GetChildren()) {
	  if (child is Player) {
		child.QueueFree();
	  }
	}

	var container = CorpseContainer ?? this;
	foreach (var child in container.GetChildren()) {
	  if (child is Corpse) {
		child.QueueFree();
	  }
	}
  }
}
