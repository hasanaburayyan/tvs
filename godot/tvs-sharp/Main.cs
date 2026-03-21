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
  public SquadManager SquadManager;

  [Export]
  public Hud Hud;

  [Export]
  public MapManager MapManager;

	public override void _Ready()
	{
	Hud.StartLobby += (id) => {
	  PlayerManager.GameId = (ulong)id;
	  PlayerManager.LoadLobby();
	  SquadManager.GameId = (ulong)id;
	  SquadManager.LoadSquads();
	  MapManager.GameId = (ulong)id;
	  MapManager.LoadMap();
	  Hud.ActivateFreelook();
	};

	Hud.LeaveLobby += (id) =>
	{
	  Hud.DeactivateFreelook();
	  if (PlayerManager.GameId == (ulong)id)
	  {
		PlayerManager.GameId = 0;
	  }
	  SquadManager.GameId = 0;
	  SquadManager.DestroyAll();
	  MapManager.GameId = 0;
	  MapManager.DestroyMap();
	  PlayerManager.DestroyLobby();
	};

	PlayerManager.LocalPlayerDied += (ulong gameSessionId, long diedAtMicros, uint respawnTimerSeconds) =>
	{
	  Hud.ShowDeathOverlay(gameSessionId, diedAtMicros, respawnTimerSeconds);
	};

	PlayerManager.LocalPlayerRevived += () =>
	{
	  Hud.HideDeathOverlay();
	};

	PlayerManager.PlayerKilled += (string killerName, string victimName, byte killerTeam, byte victimTeam) =>
	{
	  Hud.AddKillFeedEntry(killerName, victimName, killerTeam, victimTeam);
	};

	MapManager.CapturePointUpdated += (long pointId, float posX, float posZ, float radius, int inf1, int inf2, int max, int owner) =>
	{
	  Hud.UpdateCapturePoint((ulong)pointId, posX, posZ, radius, inf1, inf2, max, (byte)owner);
	};

	MapManager.CapturePointRemoved += (long pointId) =>
	{
	  Hud.RemoveCapturePoint((ulong)pointId);
	};
  }

	public override void _Process(double delta)
	{
	}
}
