using Godot;
using System;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class SpacetimeNetworkManager : Node
{
  private const string TokenPath = "user://auth_token.txt";

  [Signal]
  public delegate void SubscriptionAppliedEventHandler();

  public static SpacetimeNetworkManager Instance { get; private set; }
  public DbConnection Conn { get; private set; }
  public ulong? ActivePlayerId { get; set; }

  public override void _Ready()
  {
	Instance = this;
	Conn = DbConnection.Builder()
	  .WithUri("https://maincloud.spacetimedb.com")
	  //.WithUri("http://127.0.0.1:3000")
	  .WithDatabaseName("tvs")
	  .WithToken(LoadToken())
	  .OnConnect(OnConnected)
	  .OnConnectError(OnConnectError)
	  .OnDisconnect(OnDisconnect)
	  .Build();
  }

  void OnConnected(DbConnection conn, Identity identity, string token)
  {
	GD.Print($"Connected as {identity}");
	SaveToken(token);
	Conn.SubscriptionBuilder()
	  .OnApplied((ctx) =>
	  {
		GD.Print("Subscription applied");
		EmitSignal(SignalName.SubscriptionApplied);
	  })
	  .SubscribeToAllTables();
  }

  void OnConnectError(Exception err)
  {
	GD.Print($"Connection error: {err}");
  }

  void OnDisconnect(DbConnection conn, Exception? err)
  {
	GD.Print("Disconnected");
	ActivePlayerId = null;
  }

  static void SaveToken(string token)
  {
	using var file = FileAccess.Open(TokenPath, FileAccess.ModeFlags.Write);
	file?.StoreString(token);
  }

  static string? LoadToken()
  {
	if (!FileAccess.FileExists(TokenPath))
	  return null;
	using var file = FileAccess.Open(TokenPath, FileAccess.ModeFlags.Read);
	return file?.GetAsText();
  }

  public override void _Process(double delta)
  {
	Conn.FrameTick();
  }
}
