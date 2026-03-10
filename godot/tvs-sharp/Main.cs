using Godot;
using System;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Threading;

public partial class Main : Node3D
{
  [Export]
  public PlayerManager PlayerManager;

  [Export]
  public Hud Hud;


	public override void _Ready()
	{
	Hud.StartLobby += (id) => {
	  PlayerManager.GameId = (ulong)id;
	  PlayerManager.LoadLobby();
	};

	Hud.LeaveLobby += (id) =>
	{
	  if (PlayerManager.GameId == (ulong)id)
	  {
		PlayerManager.GameId = 0;
	  }

	  PlayerManager.DestroyLobby();
	};
  }

	public override void _Process(double delta)
	{
	}
}
