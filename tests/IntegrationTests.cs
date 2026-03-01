using System;
using System.Linq;
using Xunit;

public class PlayerTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public PlayerTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void CreatePlayer_InsertsRow()
  {
    _client.Call(r => r.CreatePlayer("Alice"));

    var player = _client.Db.Player.Identity.Find(_client.Identity);
    Assert.NotNull(player);
    Assert.Equal("Alice", player.Name);
  }

  [Fact]
  public void CreatePlayer_DuplicateIdentityRejected()
  {
    _client.Call(r => r.CreatePlayer("First"));
    _client.CallExpectFailure(r => r.CreatePlayer("Second"));
  }

  [Fact]
  public void DeletePlayer_RemovesRow()
  {
    _client.Call(r => r.CreatePlayer("ToDelete"));
    Assert.NotNull(_client.Db.Player.Identity.Find(_client.Identity));

    _client.Call(r => r.DeletePlayer(_client.Identity));
    Assert.Null(_client.Db.Player.Identity.Find(_client.Identity));
  }
}

public class GameSessionTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public GameSessionTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void CreateGame_InsertsSession()
  {
    var gameId = _client.CreateGame(4);

    var game = _client.Db.GameSession.Id.Find(gameId);
    Assert.NotNull(game);
    Assert.Equal(4u, game.MaxPlayers);
  }

  [Fact]
  public void DeleteGame_RemovesSession()
  {
    var gameId = _client.CreateGame(2);

    _client.Call(r => r.DeleteGame(gameId));
    Assert.Null(_client.Db.GameSession.Id.Find(gameId));
  }
}

public class JoinGameTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public JoinGameTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void JoinGame_Succeeds()
  {
    _client.Call(r => r.CreatePlayer("Host"));
    var gameId = _client.CreateGame(4);
    _client.Call(r => r.JoinGame(gameId));

    var players = _client.Db.GamePlayer.GameSessionId.Filter(gameId).ToList();
    Assert.Single(players);
  }

  [Fact]
  public void JoinGame_NonexistentGameRejected()
  {
    _client.Call(r => r.CreatePlayer("Lonely"));
    _client.CallExpectFailure(r => r.JoinGame(999999));
  }

  [Fact]
  public void JoinGame_FullGameRejected()
  {
    using var joiner = SpacetimeTestClient.Create();

    _client.Call(r => r.CreatePlayer("Host"));
    var gameId = _client.CreateGame(1);
    _client.Call(r => r.JoinGame(gameId));

    joiner.Call(r => r.CreatePlayer("Joiner"));
    joiner.CallExpectFailure(r => r.JoinGame(gameId));

    joiner.ClearData();
  }

  [Fact]
  public void LeaveGame_RemovesGamePlayer()
  {
    _client.Call(r => r.CreatePlayer("Leaver"));
    var gameId = _client.CreateGame(4);
    _client.Call(r => r.JoinGame(gameId));
    Assert.Single(_client.Db.GamePlayer.GameSessionId.Filter(gameId).ToList());

    _client.Call(r => r.LeaveGame(gameId));
    Assert.Empty(_client.Db.GamePlayer.GameSessionId.Filter(gameId).ToList());
  }
}
