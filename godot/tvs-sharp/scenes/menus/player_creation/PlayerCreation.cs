using Godot;
using System;
using System.Linq;
using SpacetimeDB.Types;

public partial class PlayerCreation : PopulableMenu
{
  [Export]
  public Hud hud;

  private LineEdit _usernameInput;
  private Button _createPlayerButton;
  private VBoxContainer _playerListContainer;

  public override void _Ready()
  {
	_usernameInput = GetNode<LineEdit>("%UsernameInput");
	_createPlayerButton = GetNode<Button>("%CreatePlayerButton");
	_playerListContainer = GetNode<VBoxContainer>("%PlayerListContainer");

	_createPlayerButton.Pressed += OnCreatePlayerButtonPressed;
  }

  void OnCreatePlayerButtonPressed()
  {
	var name = _usernameInput.Text.Trim();
	if (string.IsNullOrEmpty(name)) return;

	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.OnCreatePlayer += OnCreatePlayerResult;
	conn.Reducers.CreatePlayer(name);
  }

  void OnCreatePlayerResult(ReducerEventContext ctx, string name)
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.OnCreatePlayer -= OnCreatePlayerResult;

	if (ctx.Event.Status is not SpacetimeDB.Status.Committed) return;

	var player = conn.Db.Player.Name.Find(name);
	if (player != null)
	{
	  SpacetimeNetworkManager.Instance.ActivePlayerId = player.Id;
	}
	hud.SwitchToMenu(Menus.SERVER_SELECT);
  }

  public override void Populate()
  {
	PopulatePlayerList();
  }

  void ClearPlayerList()
  {
	foreach (var child in _playerListContainer.GetChildren())
	{
	  _playerListContainer.RemoveChild(child);
	  child.QueueFree();
	}
  }

  void PopulatePlayerList()
  {
	ClearPlayerList();
	var conn = SpacetimeNetworkManager.Instance.Conn;
	if (conn.Identity is not SpacetimeDB.Identity myIdentity) return;

	var myPlayers = conn.Db.Player.OwnerIdentity.Filter(myIdentity);
	foreach (var player in myPlayers)
	{
	  var hbox = new HBoxContainer();
	  var label = new Label();
	  label.Text = player.Name;
	  label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
	  hbox.AddChild(label);

	  if (player.Online)
	  {
		var onlineLabel = new Label();
		onlineLabel.Text = "(Online)";
		hbox.AddChild(onlineLabel);
	  }
	  else
	  {
		var selectButton = new Button();
		selectButton.Text = "Select";
		var playerId = player.Id;
		selectButton.Pressed += () => OnSelectPlayer(playerId);
		hbox.AddChild(selectButton);
	  }

	  _playerListContainer.AddChild(hbox);
	}
  }

  void OnSelectPlayer(ulong playerId)
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.OnSelectPlayer += OnSelectPlayerResult;
	conn.Reducers.SelectPlayer(playerId);
  }

  void OnSelectPlayerResult(ReducerEventContext ctx, ulong playerId)
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Reducers.OnSelectPlayer -= OnSelectPlayerResult;

	if (ctx.Event.Status is not SpacetimeDB.Status.Committed) return;

	SpacetimeNetworkManager.Instance.ActivePlayerId = playerId;
	hud.SwitchToMenu(Menus.SERVER_SELECT);
  }
}
