using Godot;
using System;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class SpacetimeNetworkManager : Node
{
  public static SpacetimeNetworkManager Instance { get; private set; }
  public DbConnection Conn { get; private set; }
  public override void _Ready()
  {
	Instance = this;
	Conn = DbConnection.Builder()
	  .WithUri("https://maincloud.spacetimedb.com")
	  //.WithUri("http://127.0.0.1:3000")
	  .WithDatabaseName("tvs")
	  .OnConnect(OnConnected)
	  .OnConnectError(OnConnectError)
	  .OnDisconnect(OnDisconnect)
	  .Build();
  }

  void OnConnected(DbConnection conn, Identity identity, string token)
  {
	GD.Print($"Connected as {identity}");
	Conn.SubscriptionBuilder()
	.OnApplied((ctx) =>
	{
	  GD.Print("Subscription applied");
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
  }

  public override void _Process(double delta)
  {
	Conn.FrameTick();
  }
}
