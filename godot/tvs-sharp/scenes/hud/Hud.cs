using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

public enum Menus
{
  UNKNOWN = 0,
  PLAYER_CREATION = 1,
  SERVER_SELECT = 2,
  IN_GAME_MENU = 3,
  PROFILE_SELECT = 4,
  LOADOUT_SELECT = 5,
}

public partial class Hud : CanvasLayer
{
  [Signal]
  public delegate void StartLobbyEventHandler(int id);

  [Signal]
  public delegate void LeaveLobbyEventHandler(int id);

  private PopulableMenu _playerCreationMenu;
  private PopulableMenu _serverSelectMenu;
  private PopulableMenu _ingameMenu;
  private PopulableMenu _profileSelectMenu;
  private PopulableMenu _loadoutSelectMenu;
  private PlayerHud _playerHud;

  private Dictionary<Menus, PopulableMenu> _menus = new Dictionary<Menus, PopulableMenu>();

  private bool inGame = false;
  public ulong sessionID;

  public override void _Ready()
  {
	_playerCreationMenu = GetNode<PopulableMenu>("%PlayerCreation");
	_serverSelectMenu = GetNode<PopulableMenu>("%ServerSelect");
	_ingameMenu = GetNode<PopulableMenu>("%InGameMenu");
	_profileSelectMenu = GetNode<PopulableMenu>("%ProfileSelect");
	_loadoutSelectMenu = GetNode<PopulableMenu>("%LoadoutSelect");
	_playerHud = GetNode<PlayerHud>("PlayerHud");

	_menus = new Dictionary<Menus, PopulableMenu>
	{
	  { Menus.PLAYER_CREATION, _playerCreationMenu },
	  { Menus.SERVER_SELECT, _serverSelectMenu },
	  { Menus.IN_GAME_MENU, _ingameMenu },
	  { Menus.PROFILE_SELECT, _profileSelectMenu },
	  { Menus.LOADOUT_SELECT, _loadoutSelectMenu },
	};

	SpacetimeNetworkManager.Instance.SubscriptionApplied += () =>
	{
	  SwitchToMenu(Menus.PLAYER_CREATION);
	};

	StartLobby += (int id) =>
	{
	  inGame = true;
	  sessionID = (ulong)id;
	  _playerHud.Visible = true;
	  _playerHud.Initialize(sessionID);
	};

	LeaveLobby += (int id) =>
	{
	  inGame = false;
	  _playerHud.Teardown();
	  _playerHud.Visible = false;
	  if (sessionID == (ulong)id)
	  {
		sessionID = 0;
	  }

	  SwitchToMenu(Menus.SERVER_SELECT);
	};

	SwitchToMenu(Menus.PROFILE_SELECT);
  }

  private FreelookCamera FindFreelookCamera()
  {
	var cam = GetViewport().GetCamera3D();
	return cam as FreelookCamera;
  }

  private void SetFreelookInGame(bool entering)
  {
	var fc = FindFreelookCamera();
	if (fc == null) return;

	fc.SetInGame(entering);
	if (entering)
	  fc.SetCameraLocked(true);
  }

  public void CloseMenus()
  {
	foreach (var menu in _menus.Values)
	{
	  menu.Visible = false;
	}
  }

  public override void _UnhandledInput(InputEvent @event)
  {
	if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
	{
	  if (inGame)
	  {
		bool opening = !_ingameMenu.Visible;
		_ingameMenu.Visible = opening;

		var fc = FindFreelookCamera();
		if (fc != null)
		{
		  fc.MenuOpen = opening;
		  if (opening)
			Input.MouseMode = Input.MouseModeEnum.Visible;
		  else if (fc.CameraLocked)
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	  }
	  GetViewport().SetInputAsHandled();
	}
  }

  public void SwitchToMenu(Menus menu)
  {
	CloseMenus();
	_menus[menu].Visible = true;
	_menus[menu].Populate();
  }

  public bool HasLoadoutForSession(ulong gameSessionId)
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return false;

	foreach (var lo in mgr.Conn.Db.Loadout.GameSessionId.Filter(gameSessionId))
	{
	  if (lo.PlayerId == mgr.ActivePlayerId)
		return true;
	}
	return false;
  }

  public void EnterGameOrLoadoutSelect(ulong gameSessionId)
  {
	sessionID = gameSessionId;
	if (HasLoadoutForSession(gameSessionId))
	{
	  EmitSignal(SignalName.StartLobby, (long)gameSessionId);
	  CloseMenus();
	}
	else
	{
	  SwitchToMenu(Menus.LOADOUT_SELECT);
	}
  }

  public void ActivateFreelook()
  {
	SetFreelookInGame(true);
  }

  public void DeactivateFreelook()
  {
	SetFreelookInGame(false);
  }

  public void ShowDeathOverlay(ulong gameSessionId, long diedAtMicros, uint respawnTimerSeconds)
  {
	_playerHud.ShowDeathOverlay(gameSessionId, diedAtMicros, respawnTimerSeconds);
  }

  public void HideDeathOverlay()
  {
	_playerHud.HideDeathOverlay();
  }
}
