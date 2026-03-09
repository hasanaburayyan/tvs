using Godot;
using System;

public partial class PlayerCreation : PopulableMenu
{
  [Export]
  public Hud hud;

  private LineEdit _usernameInput;
  private Button _createPlayerButton;


  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
	_usernameInput = GetNode<LineEdit>("%UsernameInput");
	_createPlayerButton = GetNode<Button>("%CreatePlayerButton");

	_createPlayerButton.Pressed += OnCreatePlayerButtonPressed;
  }

  void OnCreatePlayerButtonPressed()
  {
	SpacetimeNetworkManager.Instance.Conn.Reducers.CreatePlayer(_usernameInput.Text);
	hud.SwitchToMenu(Menus.SERVER_SELECT);
  }
}
