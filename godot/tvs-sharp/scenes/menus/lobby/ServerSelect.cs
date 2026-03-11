using Godot;
using System;
using System.Linq;

public partial class ServerSelect : PopulableMenu
{
  [Export]
  public Hud hud;

  private static readonly PackedScene ServerItemScene = GD.Load<PackedScene>("uid://b3nr84vs1xjdy");

  private Button _refreshButton;
  private VBoxContainer _serverListContainer;
  

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	_refreshButton = GetNode<Button>("%RefreshButton");
	_serverListContainer = GetNode<VBoxContainer>("%ServerListContainer");

	_refreshButton.Pressed += OnRefreshButtonPressed;
	}

  public override void Populate() {
	PopulateServerList();
  }

  void ClearServerList() {
	foreach (var child in _serverListContainer.GetChildren()) {
	  _serverListContainer.RemoveChild(child);
	}
  }

  void PopulateServerList() {
	ClearServerList();
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var servers = conn.Db.GameSession.Iter();
	foreach (var server in servers) {
	  var serverItem = ServerItemScene.Instantiate<ServerItem>();
	  string ownerName = "Unknown";
	  foreach (var p in conn.Db.Player.OwnerIdentity.Filter(server.OwnerIdentity))
	  {
		ownerName = p.Name;
		break;
	  }
	  serverItem.hud = hud;
	  var currentPlayerCount = conn.Db.GamePlayer.GameSessionId.Filter(server.Id).Count();
	  _serverListContainer.AddChild(serverItem);
	  serverItem.Populate(ownerName, currentPlayerCount, server.MaxPlayers, server.Id, server.State.ToString());
	}
  }

  void OnRefreshButtonPressed() {
	PopulateServerList();
  }
}
