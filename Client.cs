using System;
using System.Threading;
using SpacetimeDB;
using SpacetimeDB.Types;

if (args.Length == 0)
{
  Console.WriteLine("Usage: dotnet run -- <command> [args]");
  Console.WriteLine("Commands:");
  Console.WriteLine("  list                  List all players and game sessions");
  Console.WriteLine("  seed                  Create test players and a game session");
  Console.WriteLine("  delete-players        Delete all players");
  return;
}

var command = args[0];
var done = false;

var conn = DbConnection.Builder()
    .WithUri("http://localhost:3000")
    .WithDatabaseName("tvs")
    .OnConnect(OnConnected)
    .OnConnectError(OnConnectError)
    .OnDisconnect(OnDisconnected)
    .Build();

while (!done)
{
  conn.FrameTick();
  Thread.Sleep(100);
}

conn.Disconnect();

void OnConnected(DbConnection conn, Identity identity, string token)
{
  Log.Info($"Connected as {identity}");
  conn.SubscriptionBuilder()
      .OnApplied((ctx) => RunCommand(conn, command))
      .SubscribeToAllTables();
}

void OnConnectError(Exception err)
{
  Log.Error($"Connection error: {err}");
  done = true;
}

void OnDisconnected(DbConnection c, Exception? err)
{
  done = true;
}

void RunCommand(DbConnection conn, string cmd)
{
  switch (cmd)
  {
    case "list":
      ListAll(conn);
      done = true;
      break;
    case "seed":
      Seed(conn);
      break;
    case "delete-all":
      DeleteAll(conn);
      break;
    default:
      Console.WriteLine($"Unknown command: {cmd}");
      done = true;
      break;
  }
}

void ListAll(DbConnection conn)
{
  Console.WriteLine("=== Players ===");
  foreach (var player in conn.Db.Player.Iter())
    Console.WriteLine($"  {player.Name} (online={player.Online})");

  Console.WriteLine("\n=== Game Sessions ===");
  foreach (var session in conn.Db.GameSession.Iter())
    Console.WriteLine($"  #{session.Id} state={session.State} max={session.MaxPlayers}");

  Console.WriteLine("\n=== Game Players ===");
  foreach (var gp in conn.Db.GamePlayer.Iter())
    Console.WriteLine($"  session={gp.GameSessionId} player={gp.PlayerIdentity} slot={gp.TeamSlot}");
}

void Seed(DbConnection conn)
{
  Log.Info("Seeding: creating player and game session...");
  conn.Reducers.CreatePlayer("TestPlayer");
  conn.Reducers.CreateGame(4);
  conn.Reducers.JoinGame(1);
  done = true;
}

void DeleteAll(DbConnection conn)
{
  foreach (var player in conn.Db.Player.Iter())
  {
    conn.Reducers.DeletePlayer(player.Identity);
  }

  foreach (var game in conn.Db.GameSession.Iter())
  {
    conn.Reducers.DeleteGame(game.Id);
  }

  foreach (var gp in conn.Db.GamePlayer.Iter()) 
  {
    conn.Reducers.LeaveGame(gp.GameSessionId);
  }

  done = true;
}
