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

	foreach (var entity in conn.Db.Entity.GameSessionId.Filter(GameId))
	{
	  if (entity.Type == EntityType.GamePlayer)
	  {
		var gp = conn.Db.GamePlayer.EntityId.Find(entity.EntityId);
		if (gp != null && gp.Active) SpawnPlayer(entity, gp);
	  }
	  else if (entity.Type == EntityType.Corpse)
	  {
		var corpse = conn.Db.Corpse.EntityId.Find(entity.EntityId);
		if (corpse != null) SpawnCorpse(entity, corpse);
	  }
	}

	conn.Db.GamePlayer.OnInsert += OnGamePlayerInsert;
	conn.Db.GamePlayer.OnDelete += OnGamePlayerDelete;
	conn.Db.GamePlayer.OnUpdate += OnGamePlayerUpdate;
	conn.Db.Entity.OnUpdate += OnEntityUpdate;
	conn.Db.Targetable.OnUpdate += OnTargetableUpdate;
	conn.Db.PositionOverride.OnInsert += OnPositionOverrideInsert;
	conn.Db.BattleLog.OnInsert += OnBattleLogInsert;
	conn.Db.Corpse.OnInsert += OnCorpseInsert;
	conn.Db.Corpse.OnDelete += OnCorpseDelete;
  }

  public void SpawnPlayer(Entity entity, GamePlayer gamePlayer) {
	var owner = SpacetimeNetworkManager.Instance.Conn.Db.Player.Id.Find(gamePlayer.PlayerId);
	if (owner is null)
	{
	  GD.PrintErr($"SpawnPlayer: Player record not found for PlayerId {gamePlayer.PlayerId}, skipping spawn");
	  return;
	}

	var targetable = SpacetimeNetworkManager.Instance.Conn.Db.Targetable.EntityId.Find(entity.EntityId);

	var player = PlayerScene.Instantiate<Player>();
	player.Name = gamePlayer.PlayerId.ToString();
	player.PlayerId = gamePlayer.PlayerId;
	player.EntityId = entity.EntityId;
	player.GameId = GameId;
	player.Position = new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
	player.Rotation = new Vector3(0, entity.RotationY, 0);
	player.username = owner.Name;
	PlayerSpawnPath.AddChild(player);

	player.SetTeamColor(entity.TeamSlot);

	if (targetable != null && targetable.Dead)
	  player.PlayDeath();
  }

  public void OnGamePlayerInsert(EventContext ctx, GamePlayer gamePlayer) {
	if (!gamePlayer.Active) return;

	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(gamePlayer.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;

	SpawnPlayer(entity, gamePlayer);
  }

  public void OnGamePlayerDelete(EventContext ctx, GamePlayer gamePlayer) {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(gamePlayer.EntityId);
	if (entity != null && entity.GameSessionId != GameId) return;
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
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(newGamePlayer.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;

	if (oldGamePlayer.Active && !newGamePlayer.Active) {
	  RemovePlayer(oldGamePlayer);
	  return;
	}

	if (!oldGamePlayer.Active && newGamePlayer.Active) {
	  SpawnPlayer(entity, newGamePlayer);
	  return;
	}

	if (newGamePlayer.Active) {
	  if (newGamePlayer.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId
		&& oldGamePlayer.TargetEntityId != null
		&& newGamePlayer.TargetEntityId == null)
	  {
		Targeting.Instance?.ClearTarget();
	  }
	}
  }

  private void OnEntityUpdate(EventContext ctx, Entity oldEntity, Entity newEntity) {
	if (newEntity.GameSessionId != GameId) return;
	if (newEntity.Type != EntityType.GamePlayer) return;

	var gp = SpacetimeNetworkManager.Instance.Conn.Db.GamePlayer.EntityId.Find(newEntity.EntityId);
	if (gp == null || !gp.Active) return;

	var player = PlayerSpawnPath.GetNodeOrNull<Player>(gp.PlayerId.ToString());
	if (player == null) return;

	if (!player.IsLocal)
	{
	  player.OnStateUpdated(
		new Vector3(newEntity.Position.X, newEntity.Position.Y, newEntity.Position.Z),
		newEntity.RotationY
	  );
	}

	if (oldEntity.TeamSlot != newEntity.TeamSlot)
	  player.SetTeamColor(newEntity.TeamSlot);
  }

  private void OnTargetableUpdate(EventContext ctx, SpacetimeDB.Types.Targetable oldT, SpacetimeDB.Types.Targetable newT) {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var entity = conn.Db.Entity.EntityId.Find(newT.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	if (entity.Type != EntityType.GamePlayer) return;

	var gp = conn.Db.GamePlayer.EntityId.Find(newT.EntityId);
	if (gp == null || !gp.Active) return;

	var player = PlayerSpawnPath.GetNodeOrNull<Player>(gp.PlayerId.ToString());
	if (player == null) return;

	if (!oldT.Dead && newT.Dead)
	{
	  player.PlayDeath();

	  string victimName = conn.Db.Player.Id.Find(gp.PlayerId)?.Name ?? "Unknown";
	  string killerName = "Unknown";
	  byte killerTeam = 0;

	  var killLog = conn.Db.BattleLog.GameSessionId.Filter(GameId)
		.FirstOrDefault(log => log.EventType == BattleLogEventType.Kill
		  && log.TargetEntityIds.Contains(newT.EntityId));

	  if (killLog != null)
	  {
		var actorEntity = conn.Db.Entity.EntityId.Find(killLog.ActorEntityId);
		var actorGp = conn.Db.GamePlayer.EntityId.Find(killLog.ActorEntityId);
		if (actorGp != null)
		{
		  killerName = conn.Db.Player.Id.Find(actorGp.PlayerId)?.Name ?? "Unknown";
		  killerTeam = actorEntity?.TeamSlot ?? 0;
		}
	  }

	  EmitSignal(SignalName.PlayerKilled, killerName, victimName, killerTeam, entity.TeamSlot);

	  if (gp.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId)
	  {
		var session = conn.Db.GameSession.Id.Find(GameId);
		uint timer = session?.RespawnTimerSeconds ?? 15;
		long diedMicros = newT.DiedAt?.MicrosecondsSinceUnixEpoch ?? 0;
		EmitSignal(SignalName.LocalPlayerDied, GameId, diedMicros, timer);
	  }
	}
	else if (oldT.Dead && !newT.Dead)
	{
	  player.Revive();
	  player.ApplyPositionOverride(
		new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z)
	  );

	  if (gp.PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId)
		EmitSignal(SignalName.LocalPlayerRevived);
	}
  }

  private void SpawnCorpse(Entity entity, SpacetimeDB.Types.Corpse corpse)
  {
	var container = CorpseContainer ?? this;
	var node = new Corpse();
	node.Name = $"Corpse_{entity.EntityId}";
	node.EntityId = entity.EntityId;
	node.SourceEntityId = corpse.SourceEntityId;
	node.PlayerId = corpse.PlayerId;

	var sourceEntity = corpse.SourceEntityId.HasValue
	  ? SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(corpse.SourceEntityId.Value)
	  : null;
	if (sourceEntity?.Type != EntityType.Soldier)
	{
	  var owner = SpacetimeNetworkManager.Instance.Conn.Db.Player.Id.Find(corpse.PlayerId);
	  node.PlayerName = owner?.Name ?? "";
	}

	node.Position = new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
	node.Rotation = new Vector3(0, entity.RotationY, 0);
	container.AddChild(node);
  }

  private void OnCorpseInsert(EventContext ctx, SpacetimeDB.Types.Corpse corpse)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(corpse.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	SpawnCorpse(entity, corpse);
  }

  private void OnCorpseDelete(EventContext ctx, SpacetimeDB.Types.Corpse corpse)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(corpse.EntityId);
	if (entity != null && entity.GameSessionId != GameId) return;
	var container = CorpseContainer ?? this;
	var node = container.GetNodeOrNull<Corpse>($"Corpse_{corpse.EntityId}");
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
	if (entry.EventType != BattleLogEventType.Attack && entry.EventType != BattleLogEventType.Heal) return;
	if (entry.ResolvedPower == 0) return;

	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	bool isHeal = entry.EventType == BattleLogEventType.Heal;

	foreach (var targetEntityId in entry.TargetEntityIds)
	{
	  var targetEntity = conn.Db.Entity.EntityId.Find(targetEntityId);
	  if (targetEntity == null) continue;

	  Node3D targetNode = null;
	  if (targetEntity.Type == EntityType.GamePlayer)
	  {
		var targetGp = conn.Db.GamePlayer.EntityId.Find(targetEntityId);
		if (targetGp != null)
		  targetNode = PlayerSpawnPath.GetNodeOrNull<Player>(targetGp.PlayerId.ToString());
	  }

	  if (targetNode == null) continue;

	  var dmgNum = DamageNumberScene.Instantiate<DamageNumber>();
	  dmgNum.Setup(entry.ResolvedPower, isHeal);
	  GetTree().Root.AddChild(dmgNum);
	  dmgNum.GlobalPosition = targetNode.GlobalPosition + new Vector3(0, 2.2f, 0);
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
