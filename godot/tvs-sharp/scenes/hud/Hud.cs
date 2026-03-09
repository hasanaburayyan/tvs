using Godot;
using System;
using System.Collections.Generic;

public enum Menus
{
  UNKNOWN = 0,
  PLAYER_CREATION = 1,
  SERVER_SELECT = 2,
}

public partial class Hud : CanvasLayer
{
  [Signal]
  public delegate void StartLobbyEventHandler(int id);

  private PopulableMenu _playerCreationMenu;
  private PopulableMenu _serverSelectMenu;

  private Dictionary<Menus, PopulableMenu> _menus = new Dictionary<Menus, PopulableMenu>();

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
	_playerCreationMenu = GetNode<PopulableMenu>("%PlayerCreation");
	_serverSelectMenu = GetNode<PopulableMenu>("%ServerSelect");

	_menus = new Dictionary<Menus, PopulableMenu>
  {
	{ Menus.PLAYER_CREATION, _playerCreationMenu },
	{ Menus.SERVER_SELECT, _serverSelectMenu },
  };
  }


  public void CloseMenus()
  {
	foreach (var menu in _menus.Values)
	{
	  menu.Visible = false;
	}
  }

  public void SwitchToMenu(Menus menu)
  {
	CloseMenus();
	_menus[menu].Visible = true;
	_menus[menu].Populate();
  }
}
