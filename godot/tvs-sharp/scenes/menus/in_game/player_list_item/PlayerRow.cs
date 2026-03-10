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

  // Called when the node enters the scene tree for the first time.
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
	var teleportToPlayer = conn.Db.GamePlayer.PlayerIdentity.Find(player.Identity);
	var myPlayer = conn.Db.Player.Identity.Find(conn.Identity ?? throw new Exception("Client has no identity"));

	conn.Reducers.TeleportPlayer(hud.sessionID, myPlayer.Name, teleportToPlayer.Position);
  }

  public void OnTeleportButtonPressed() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var myPlayer = conn.Db.GamePlayer.PlayerIdentity.Find(conn.Identity ?? throw new Exception("Client has no identity"));

	conn.Reducers.TeleportPlayer(hud.sessionID, player.Name, myPlayer.Position);
  }

  public void OnKickButtonPressed() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.KickPlayerFromGame(hud.sessionID, player.Name);
  }


  public void Populate(SpacetimeDB.Types.Player player) {
	this.player = player;
	isSelf = player.Identity == SpacetimeNetworkManager.Instance.Conn.Identity;

	_nameLabel.Text = player.Name;
	if (isSelf) {
	  _kickButton.Visible = false;
	  _travelButton.Visible = false;
	  _teleportButton.Visible = false;
	}
  }
}
