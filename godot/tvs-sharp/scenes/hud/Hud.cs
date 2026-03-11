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

  private Dictionary<Menus, PopulableMenu> _menus = new Dictionary<Menus, PopulableMenu>();

  private bool inGame = false;
  public ulong sessionID;


  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
	_playerCreationMenu = GetNode<PopulableMenu>("%PlayerCreation");
	_serverSelectMenu = GetNode<PopulableMenu>("%ServerSelect");
	_ingameMenu = GetNode<PopulableMenu>("%InGameMenu");

	_menus = new Dictionary<Menus, PopulableMenu>
  {
  { Menus.PLAYER_CREATION, _playerCreationMenu },
  { Menus.SERVER_SELECT, _serverSelectMenu },
  { Menus.IN_GAME_MENU, _ingameMenu }
  };

	SpacetimeNetworkManager.Instance.SubscriptionApplied += () =>
	{
	  SwitchToMenu(Menus.PLAYER_CREATION);
	};

	StartLobby += (int id) =>
	{
	  inGame = true;
	  sessionID = (ulong)id;
	};

	LeaveLobby += (int id) =>
	{
	  inGame = false;
	  if (sessionID == (ulong)id)
	  {
		sessionID = 0;
	  }

	  SwitchToMenu(Menus.SERVER_SELECT);
	};

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
		_ingameMenu.Visible = !_ingameMenu.Visible;
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
}
