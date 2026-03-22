using Godot;
using SpacetimeDB.Types;
using System;

public partial class PlayerRow : HBoxContainer
{
  public Hud hud;

  private Label _nameLabel;
  private Button _travelButton;
  private Button _teleportButton;
  private Button _kickButton;

  private SpacetimeDB.Types.Player player;

  private bool isSelf = false;

  private GamePlayer? FindInSession(ulong playerId) {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	foreach (var gp in conn.Db.GamePlayer.PlayerId.Filter(playerId))
	{
	  if (!gp.Active) continue;
	  var entity = conn.Db.Entity.EntityId.Find(gp.EntityId);
	  if (entity != null && entity.GameSessionId == hud.sessionID) return gp;
	}
	return null;
  }

  public override void _Ready()
  {
	_nameLabel = GetNode<Label>("%NameLabel");
	_travelButton = GetNode<Button>("%TravelButton");
	_teleportButton = GetNode<Button>("%TeleportButton");
	_kickButton = GetNode<Button>("%KickButton");

	_travelButton.Pressed += OnTravelButtonPressed;
	_teleportButton.Pressed += OnTeleportButtonPressed;
	_kickButton.Pressed += OnKickButtonPressed;
  }

  public void OnTravelButtonPressed() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var teleportToGamePlayer = FindInSession(player.Id);
	if (teleportToGamePlayer is null) return;

	var targetEntity = conn.Db.Entity.EntityId.Find(teleportToGamePlayer.EntityId);
	if (targetEntity is null) return;

	var activePlayerId = SpacetimeNetworkManager.Instance.ActivePlayerId
	  ?? throw new Exception("No active player selected");
	var myPlayer = conn.Db.Player.Id.Find(activePlayerId)
	  ?? throw new Exception("Active player not found");

	conn.Reducers.TeleportPlayer(hud.sessionID, myPlayer.Name, targetEntity.Position);
  }

  public void OnTeleportButtonPressed() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var activePlayerId = SpacetimeNetworkManager.Instance.ActivePlayerId
	  ?? throw new Exception("No active player selected");
	var myGamePlayer = FindInSession(activePlayerId);
	if (myGamePlayer is null) return;

	var myEntity = conn.Db.Entity.EntityId.Find(myGamePlayer.EntityId);
	if (myEntity is null) return;

	conn.Reducers.TeleportPlayer(hud.sessionID, player.Name, myEntity.Position);
  }

  public void OnKickButtonPressed() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.KickPlayerFromGame(hud.sessionID, player.Name);
  }

  public void Populate(SpacetimeDB.Types.Player player) {
	this.player = player;
	isSelf = player.Id == SpacetimeNetworkManager.Instance.ActivePlayerId;

	_nameLabel.Text = player.Name;
	if (isSelf) {
	  _kickButton.Visible = false;
	  _travelButton.Visible = false;
	  _teleportButton.Visible = false;
	}
  }
}
