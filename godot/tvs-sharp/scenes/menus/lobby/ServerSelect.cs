using Godot;
using SpacetimeDB.Types;
using System;
using System.Linq;

public partial class ServerSelect : PopulableMenu
{
  [Export]
  public Hud hud;

  private static readonly PackedScene ServerItemScene = GD.Load<PackedScene>("uid://b3nr84vs1xjdy");

  private Button _refreshButton;
  private VBoxContainer _serverListContainer;
  private Button _createServer;
  private SpinBox _serverSizeRange;
  private OptionButton _mapSelect;

  public override void _Ready()
  {
	_refreshButton = GetNode<Button>("%RefreshButton");
	_serverListContainer = GetNode<VBoxContainer>("%ServerListContainer");
	_createServer = GetNode<Button>("%CreateServerButton");
	_serverSizeRange = GetNode<SpinBox>("%ServerSizeRange");
	_mapSelect = GetNodeOrNull<OptionButton>("%MapSelect");

	_refreshButton.Pressed += OnRefreshButtonPressed;
	_createServer.Pressed += OnServerCreateButtonPressed;
  }

  public override void Populate()
  {
	PopulateServerList();
	PopulateMapSelect();
  }

  void PopulateMapSelect()
  {
	if (_mapSelect == null) return;
	_mapSelect.Clear();
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;
	foreach (var map in conn.Db.MapDef.Iter())
	{
	  _mapSelect.AddItem(map.Name, (int)map.Id);
	}
	if (_mapSelect.ItemCount > 0) _mapSelect.Selected = 0;
  }

  ulong GetSelectedMapDefId()
  {
	if (_mapSelect != null && _mapSelect.ItemCount > 0)
	  return (ulong)_mapSelect.GetItemId(_mapSelect.Selected);
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return 0;
	foreach (var map in conn.Db.MapDef.Iter())
	  return map.Id;
	return 0;
  }

  void ClearServerList()
  {
	foreach (var child in _serverListContainer.GetChildren())
	{
	  _serverListContainer.RemoveChild(child);
	}
  }

  void PopulateServerList()
  {
	ClearServerList();
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var servers = conn.Db.GameSession.Iter();
	foreach (var server in servers)
	{
	  var serverItem = ServerItemScene.Instantiate<ServerItem>();
	  string ownerName = "Unknown";
	  foreach (var p in conn.Db.Player.OwnerIdentity.Filter(server.OwnerIdentity))
	  {
		ownerName = p.Name;
		break;
	  }
	  serverItem.hud = hud;
	  var currentPlayerCount = 0;
	  foreach (var entity in conn.Db.Entity.GameSessionId.Filter(server.Id))
	  {
		if (entity.Type != EntityType.GamePlayer) continue;
		var gp = conn.Db.GamePlayer.EntityId.Find(entity.EntityId);
		if (gp != null && gp.Active) currentPlayerCount++;
	  }
	  _serverListContainer.AddChild(serverItem);
	  serverItem.Populate(ownerName, currentPlayerCount, server.MaxPlayers, server.Id, server.State.ToString());
	}
  }

  void OnRefreshButtonPressed()
  {
	PopulateServerList();
  }

  void OnServerCreateButtonPressed()
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var mapId = GetSelectedMapDefId();
	conn.Reducers.OnCreateGameAndJoin += OnCreateGameAndJoinResult;
	conn.Reducers.CreateGameAndJoin((uint)_serverSizeRange.Value, null, mapId);
  }

  void OnCreateGameAndJoinResult(ReducerEventContext ctx, uint maxPlayers, uint? respawnTimerSeconds, ulong mapDefId)
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;

	if (ctx.Event.CallerIdentity != conn.Identity) return;
	
	conn.Reducers.OnCreateGameAndJoin -= OnCreateGameAndJoinResult;
	if (ctx.Event.Status is not SpacetimeDB.Status.Committed) return;

	var activePlayerId = SpacetimeNetworkManager.Instance.ActivePlayerId;
	if (activePlayerId == null) return;

	foreach (var gp in conn.Db.GamePlayer.PlayerId.Filter(activePlayerId.Value))
	{
	  if (!gp.Active) continue;
	  var entity = conn.Db.Entity.EntityId.Find(gp.EntityId);
	  if (entity != null)
	  {
		hud.EnterGameOrFactionSelect(entity.GameSessionId);
		return;
	  }
	}
  }
}
