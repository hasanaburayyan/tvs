using Godot;
using SpacetimeDB.Types;
using System;

public partial class InGameMenu : PopulableMenu
{
  [Export]
  public Hud hud;

  private Button _stuckButton;
  private Button _leaveGameButton;
  private Control _player_list_container;

  private static readonly PackedScene PlayerRowScene = GD.Load<PackedScene>("uid://v441cmkknglk");

  public override void _Ready()
  {
	_stuckButton = GetNode<Button>("%StuckButton");
	_leaveGameButton = GetNode<Button>("%LeaveGameButton");
	_player_list_container = GetNode<Control>("%PlayerListContainer");

	_stuckButton.Pressed += OnStuckButtonPressed;
	_leaveGameButton.Pressed += OnLeaveGamePressed;

	SpacetimeNetworkManager.Instance.Conn.Db.GamePlayer.OnInsert += OnGamePlayerInsert;
	SpacetimeNetworkManager.Instance.Conn.Db.GamePlayer.OnUpdate += OnGamePlayerUpdate;
	SpacetimeNetworkManager.Instance.Conn.Db.GamePlayer.OnDelete += OnGamePlayerDelete;
  }

  public override void Populate()
  {
	PopulatePlayerList();
  }

  public void PopulatePlayerList()
  {
	foreach (var child in _player_list_container.GetChildren())
	{
	  _player_list_container.RemoveChild(child);
	}

	var conn = SpacetimeNetworkManager.Instance.Conn;
	var gamePlayers = conn.Db.GamePlayer.GameSessionId.Filter(hud.sessionID);
	foreach (var gamePlayer in gamePlayers)
	{
	  var player = conn.Db.Player.Id.Find(gamePlayer.PlayerId);
	  if (player is null) continue;
	  var playerRow = PlayerRowScene.Instantiate<PlayerRow>();
	  playerRow.hud = hud;
	  _player_list_container.AddChild(playerRow);
	  playerRow.Name = player.Name;
	  playerRow.Populate(player);
	}
  }

  public void OnStuckButtonPressed()
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var activePlayerId = SpacetimeNetworkManager.Instance.ActivePlayerId
	  ?? throw new Exception("No active player selected");
	var player = conn.Db.Player.Id.Find(activePlayerId)
	  ?? throw new Exception("Active player not found");
	conn.Reducers.TeleportPlayer(hud.sessionID, player.Name, new DbVector3(2, 5, 2));
  }

  public void OnLeaveGamePressed()
  {
	SpacetimeNetworkManager.Instance.Conn.Reducers.LeaveGame(hud.sessionID);
	hud.EmitSignal(Hud.SignalName.LeaveLobby, hud.sessionID);
  }

  public void OnGamePlayerInsert(EventContext ctx, GamePlayer gamePlayer)
  {
	if (gamePlayer.GameSessionId != hud.sessionID)
	{
	  return;
	}
	PopulatePlayerList();
  }

  public void OnGamePlayerDelete(EventContext ctx, GamePlayer gamePlayer)
  {
	if (gamePlayer.GameSessionId != hud.sessionID)
	{
	  return;
	}
	PopulatePlayerList();
  }

  public void OnGamePlayerUpdate(EventContext ctx, GamePlayer oldGamePlayer, GamePlayer newGamePlayer)
  {
	if (oldGamePlayer.GameSessionId != hud.sessionID && newGamePlayer.GameSessionId != hud.sessionID)
	{
	  return;
	}

	if (oldGamePlayer.GameSessionId == hud.sessionID && newGamePlayer.GameSessionId != hud.sessionID)
	{
	  PopulatePlayerList();
	}

	if (oldGamePlayer.GameSessionId != hud.sessionID && newGamePlayer.GameSessionId == hud.sessionID)
	{
	  PopulatePlayerList();
	}
  }
}
