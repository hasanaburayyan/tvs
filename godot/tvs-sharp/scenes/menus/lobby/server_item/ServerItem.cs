using Godot;
using SpacetimeDB.Types;
using System;

public partial class ServerItem : HBoxContainer
{

  public Hud hud;

  private Label _creatorLabel;
  private Label _playerCountLabel;
  private Label _sessionIDLabel;
  private Label _stateLabel;
  private Button _enlistButton;

  private ulong _sessionID;
  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
	_creatorLabel = GetNode<Label>("%Creator");
	_playerCountLabel = GetNode<Label>("%PlayerCount");
	_sessionIDLabel = GetNode<Label>("%SessionID");
	_stateLabel = GetNode<Label>("%State");
	_enlistButton = GetNode<Button>("%Enlist");


	_creatorLabel.Text = "Simple example!";

	_enlistButton.Pressed += OnEnlistButtonPressed;
  }

  void OnEnlistButtonPressed() {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.OnJoinGame += OnJoinGameResult;
	conn.Reducers.JoinGame(_sessionID);
	_enlistButton.Disabled = true;
  }

  void OnJoinGameResult(ReducerEventContext ctx, ulong gameId) {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	if (ctx.Event.CallerIdentity != conn.Identity) return;
	conn.Reducers.OnJoinGame -= OnJoinGameResult;

	if (ctx.Event.Status is not SpacetimeDB.Status.Committed)
	{
	  _enlistButton.Disabled = false;
	  return;
	}

	hud.EnterGameOrFactionSelect(_sessionID);
  }

  public void Populate(string creator, int playerCount, ulong maxPlayers, ulong sessionID, string state) {
	GD.Print($"Populating server item with creator: {creator}, playerCount: {playerCount}, maxPlayers: {maxPlayers}, sessionID: {sessionID}, state: {state}");
	_creatorLabel.Text = $"Created by: {creator}";
	_playerCountLabel.Text = $"Player count: {playerCount}/{maxPlayers}";
	_sessionIDLabel.Text = $"ID: {sessionID}";
	_stateLabel.Text = $"State: {state}";
	_enlistButton.Disabled = playerCount >= (int)maxPlayers;
  this._sessionID = sessionID;
  }
}
