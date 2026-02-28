using Xunit;
using System;



public class PlayerTests : IDisposable
{
    private readonly SpacetimeTestClient _client;

    public PlayerTests()
    {
        _client = SpacetimeTestClient.Create();
    }

    public void Dispose() {
      _client.ClearData();
    }

    [Fact]
    public void CreatePlayer_InsertsRow()
    {
        _client.CallReducerExpectSuccess("create_player", "Alice");

        var rows = _client.SqlRows("SELECT * FROM player WHERE Name = 'Alice'");
        Assert.Single(rows);
    }

    [Fact]
    public void CreatePlayer_DuplicateIdentityRejected()
    {
        _client.CallReducerExpectSuccess("create_player", "First");
        _client.CallReducerExpectSuccess("create_player", "Second");
    }

    [Fact]
    public void DeletePlayer_RemovesRow()
    {
        _client.CallReducerExpectSuccess("create_player", "ToDelete");

        var rowsBefore = _client.SqlRows("SELECT * FROM player WHERE Name = 'ToDelete'");
        Assert.Single(rowsBefore);

        _client.CallReducerExpectSuccess("delete_player", _client.IdentityArg());

        var rowsAfter = _client.SqlRows("SELECT * FROM player WHERE Name = 'ToDelete'");
        Assert.Empty(rowsAfter);
    }
}

public class GameSessionTests: IDisposable
{
    private readonly SpacetimeTestClient _client;

    public GameSessionTests()
    {
        _client = SpacetimeTestClient.Create();
    }

    public void Dispose() {
        _client.ClearData();
    }

    [Fact]
    public void CreateGame_InsertsSession()
    {
        _client.CallReducerExpectSuccess("create_game", 4);

        var rows = _client.SqlRows("SELECT * FROM game_session WHERE MaxPlayers = 4");
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void DeleteGame_RemovesSession()
    {
        var gameId = _client.CreateGame(2);
        _client.CallReducerExpectSuccess("delete_game", gameId);

        var rowsAfter = _client.SqlRows($"SELECT * FROM game_session WHERE Id = {gameId}");
        Assert.Empty(rowsAfter);
    }
}

public class JoinGameTests: IDisposable
{
    private readonly SpacetimeTestClient _client;

    public JoinGameTests()
    {
        _client = SpacetimeTestClient.Create();
    }

    public void Dispose() {
        _client.ClearData();
    }

    [Fact]
    public void JoinGame_Succeeds()
    {
        _client.CallReducerExpectSuccess("create_player", "Host");
        var gameId = _client.CreateGame(4);

        _client.CallReducerExpectSuccess("join_game", gameId);

        var players = _client.SqlRows($"SELECT * FROM game_player WHERE GameSessionId = {gameId}");
        Assert.Single(players);
    }

    [Fact]
    public void JoinGame_NonexistentGameRejected()
    {
        _client.CallReducerExpectSuccess("create_player", "Lonely");
        _client.CallReducerExpectFailure("join_game", 999999);
    }

    [Fact]
    public void JoinGame_FullGameRejected()
    {
        using var joiner = SpacetimeTestClient.Create();

        _client.CallReducerExpectSuccess("create_player", "Host");
        var gameId = _client.CreateGame(1);

        _client.CallReducerExpectSuccess("join_game", gameId);

        joiner.CallReducerExpectSuccess("create_player", "Joiner");
        joiner.CallReducerExpectFailure("join_game", gameId);

        joiner.ClearData();
    }

    [Fact]
    public void LeaveGame_RemovesGamePlayer()
    {
        _client.CallReducerExpectSuccess("create_player", "Leaver");
        var gameId = _client.CreateGame(4);

        _client.CallReducerExpectSuccess("join_game", gameId);

        var before = _client.SqlRows($"SELECT * FROM game_player WHERE GameSessionId = {gameId}");
        Assert.Single(before);

        _client.CallReducerExpectSuccess("leave_game", gameId);

        var after = _client.SqlRows($"SELECT * FROM game_player WHERE GameSessionId = {gameId}");
        Assert.Empty(after);
    }
}
