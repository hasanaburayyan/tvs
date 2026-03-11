using Godot;
using System;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class SpacetimeNetworkManager : Node
{
  private const string TokenPrefix = "auth_token_";
  private const string TokenExt = ".txt";

  [Signal]
  public delegate void SubscriptionAppliedEventHandler();

  public static SpacetimeNetworkManager Instance { get; private set; }
  public DbConnection Conn { get; private set; }
  public ulong? ActivePlayerId { get; set; }
  public string ActiveProfile { get; private set; }

  private string TokenPath => $"user://{TokenPrefix}{ActiveProfile}{TokenExt}";

  public override void _Ready()
  {
    Instance = this;
  }

  public void Connect(string profile)
  {
    ActiveProfile = profile;
    GD.Print($"Connecting with profile '{profile}' (token: {TokenPath})");
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

  public static List<string> GetSavedProfiles()
  {
    var profiles = new List<string>();
    using var dir = DirAccess.Open("user://");
    if (dir == null) return profiles;

    dir.ListDirBegin();
    var fileName = dir.GetNext();
    while (fileName != "")
    {
      if (!dir.CurrentIsDir() && fileName.StartsWith(TokenPrefix) && fileName.EndsWith(TokenExt))
      {
        var name = fileName[TokenPrefix.Length..^TokenExt.Length];
        if (name.Length > 0)
          profiles.Add(name);
      }
      fileName = dir.GetNext();
    }
    return profiles;
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

  void SaveToken(string token)
  {
	using var file = FileAccess.Open(TokenPath, FileAccess.ModeFlags.Write);
	file?.StoreString(token);
  }

  string? LoadToken()
  {
	if (!FileAccess.FileExists(TokenPath))
	  return null;
	using var file = FileAccess.Open(TokenPath, FileAccess.ModeFlags.Read);
	return file?.GetAsText();
  }

  public override void _Process(double delta)
  {
    Conn?.FrameTick();
  }
}
