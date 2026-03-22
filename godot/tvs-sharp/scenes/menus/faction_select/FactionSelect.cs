using Godot;
using SpacetimeDB.Types;

public partial class FactionSelect : PopulableMenu
{
  [Export]
  public Hud hud;

  private Button _ententeButton;
  private Button _centralButton;

  public override void _Ready()
  {
	_ententeButton = GetNode<Button>("%EntenteButton");
	_centralButton = GetNode<Button>("%CentralButton");

	_ententeButton.Pressed += () => SelectFaction(1);
	_centralButton.Pressed += () => SelectFaction(2);
  }

  public override void Populate()
  {
	_ententeButton.Disabled = false;
	_centralButton.Disabled = false;
  }

  private void SelectFaction(byte teamSlot)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	_ententeButton.Disabled = true;
	_centralButton.Disabled = true;

	conn.Reducers.OnSetTeam += OnSetTeamResult;
	conn.Reducers.SetTeam(hud.sessionID, teamSlot);
  }

  private void OnSetTeamResult(ReducerEventContext ctx, ulong gameId, byte teamSlot)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;
	if (ctx.Event.CallerIdentity != conn.Identity) return;

	conn.Reducers.OnSetTeam -= OnSetTeamResult;

	if (ctx.Event.Status is not SpacetimeDB.Status.Committed)
	{
	  _ententeButton.Disabled = false;
	  _centralButton.Disabled = false;
	  GD.PrintErr("SetTeam failed");
	  return;
	}

	hud.EnterGameOrLoadoutSelect(hud.sessionID);
  }
}
