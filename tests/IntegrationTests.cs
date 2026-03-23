using System;
using System.Linq;
using SpacetimeDB.Types;
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

    var player = _client.Db.Player.Name.Find("Alice");
    Assert.NotNull(player);
    Assert.Equal("Alice", player.Name);
    Assert.Equal(_client.Identity, player.OwnerIdentity);
    Assert.True(player.Online);
    Assert.Equal(_client.Identity, player.ControllerIdentity);
  }

  [Fact]
  public void CreatePlayer_DuplicateNameRejected()
  {
    _client.Call(r => r.CreatePlayer("First"));
    _client.CallExpectFailure(r => r.CreatePlayer("First"));
  }

  [Fact]
  public void CreatePlayer_MultiplePlayersAllowed()
  {
    _client.Call(r => r.CreatePlayer("PlayerA"));
    _client.Call(r => r.CreatePlayer("PlayerB"));

    var a = _client.Db.Player.Name.Find("PlayerA");
    var b = _client.Db.Player.Name.Find("PlayerB");
    Assert.NotNull(a);
    Assert.NotNull(b);
    Assert.False(a.Online);
    Assert.True(b.Online);
  }

  [Fact]
  public void DeletePlayer_RemovesRow()
  {
    var playerId = _client.CreatePlayerAndGetId("ToDelete");
    _client.Call(r => r.DeselectPlayer());
    _client.Call(r => r.DeletePlayer(playerId));
    Assert.Null(_client.Db.Player.Name.Find("ToDelete"));
  }

  [Fact]
  public void DeletePlayer_OnlinePlayerRejected()
  {
    var playerId = _client.CreatePlayerAndGetId("Online");
    _client.CallExpectFailure(r => r.DeletePlayer(playerId));
  }
}

public class SelectPlayerTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SelectPlayerTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void SelectPlayer_Succeeds()
  {
    var playerId = _client.CreatePlayerAndGetId("Selectable");
    _client.Call(r => r.DeselectPlayer());

    var player = _client.Db.Player.Id.Find(playerId);
    Assert.NotNull(player);
    Assert.False(player.Online);

    _client.Call(r => r.SelectPlayer(playerId));

    player = _client.Db.Player.Id.Find(playerId);
    Assert.NotNull(player);
    Assert.True(player.Online);
    Assert.Equal(_client.Identity, player.ControllerIdentity);
  }

  [Fact]
  public void SelectPlayer_WrongOwnerRejected()
  {
    var playerId = _client.CreatePlayerAndGetId("OwnedByA");
    _client.Call(r => r.DeselectPlayer());

    using var other = SpacetimeTestClient.Create();
    other.CallExpectFailure(r => r.SelectPlayer(playerId));

    other.ClearData();
  }

  [Fact]
  public void SelectPlayer_OnlinePlayerRejected()
  {
    var playerId = _client.CreatePlayerAndGetId("AlreadyOnline");
    _client.CallExpectFailure(r => r.SelectPlayer(playerId));
  }

  [Fact]
  public void DeselectPlayer_SetsOffline()
  {
    var playerId = _client.CreatePlayerAndGetId("GoOffline");
    _client.Call(r => r.DeselectPlayer());

    var player = _client.Db.Player.Id.Find(playerId);
    Assert.NotNull(player);
    Assert.False(player.Online);
    Assert.Null(player.ControllerIdentity);
  }

  [Fact]
  public void CreatePlayer_AutoDeselectsPrevious()
  {
    var firstId = _client.CreatePlayerAndGetId("First");
    var secondId = _client.CreatePlayerAndGetId("Second");

    var first = _client.Db.Player.Id.Find(firstId);
    var second = _client.Db.Player.Id.Find(secondId);
    Assert.NotNull(first);
    Assert.NotNull(second);
    Assert.False(first.Online);
    Assert.True(second.Online);
    Assert.Equal(_client.Identity, second.ControllerIdentity);
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
    var playerId = _client.CreatePlayerAndGetId("Host");
    var gameId = _client.CreateGame(4);
    _client.Call(r => r.JoinGame(gameId));

    var players = _client.GamePlayersInSession(gameId).ToList();
    Assert.Single(players);
    Assert.Equal(playerId, players[0].PlayerId);
  }

  [Fact]
  public void JoinGame_NonexistentGameRejected()
  {
    _client.CreatePlayerAndGetId("Lonely");
    _client.CallExpectFailure(r => r.JoinGame(999999));
  }

  [Fact]
  public void JoinGame_FullGameRejected()
  {
    using var joiner = SpacetimeTestClient.Create();

    _client.CreatePlayerAndGetId("Host");
    var gameId = _client.CreateGame(1);
    _client.Call(r => r.JoinGame(gameId));

    joiner.CreatePlayerAndGetId("Joiner");
    joiner.CallExpectFailure(r => r.JoinGame(gameId));

    joiner.ClearData();
  }

  [Fact]
  public void LeaveGame_DeactivatesGamePlayer()
  {
    _client.CreatePlayerAndGetId("Leaver");
    var gameId = _client.CreateGame(4);
    _client.Call(r => r.JoinGame(gameId));
    var gps = _client.GamePlayersInSession(gameId).ToList();
    Assert.Single(gps);
    Assert.True(gps[0].Active);

    _client.Call(r => r.LeaveGame(gameId));
    gps = _client.GamePlayersInSession(gameId).ToList();
    Assert.Single(gps);
    Assert.False(gps[0].Active);
  }

  [Fact]
  public void RejoinSameGame_RestoresPosition()
  {
    _client.CreatePlayerAndGetId("Rejoiner");
    var gameId = _client.CreateGame(4);
    _client.Call(r => r.JoinGame(gameId));

    var movedPos = new DbVector3(10, 5, 10);
    _client.Call(r => r.MovePlayer(gameId, movedPos, 0f));

    var gpBeforeLeave = _client.GamePlayersInSession(gameId).First();
    var posBeforeLeave = _client.GetEntity(gpBeforeLeave.EntityId).Position;
    Assert.Equal(movedPos, posBeforeLeave);

    _client.Call(r => r.LeaveGame(gameId));
    var gpAfterLeave = _client.GamePlayersInSession(gameId).First();
    Assert.False(gpAfterLeave.Active);

    _client.Call(r => r.JoinGame(gameId));
    var gpAfterRejoin = _client.GamePlayersInSession(gameId).First(gp => gp.Active);
    var posAfterRejoin = _client.GetEntity(gpAfterRejoin.EntityId).Position;
    Assert.True(gpAfterRejoin.Active);
    Assert.Equal(movedPos, posAfterRejoin);
  }

  [Fact]
  public void JoinMultipleGames()
  {
    var playerId = _client.CreatePlayerAndGetId("MultiJoiner");
    var gameA = _client.CreateGame(4);
    var gameB = _client.CreateGame(4);

    _client.Call(r => r.JoinGame(gameA));
    var gpA = _client.GamePlayersInSession(gameA).ToList();
    Assert.Single(gpA);
    Assert.True(gpA[0].Active);

    _client.Call(r => r.JoinGame(gameB));
    var gpB = _client.GamePlayersInSession(gameB).ToList();
    Assert.Single(gpB);
    Assert.True(gpB[0].Active);

    gpA = _client.GamePlayersInSession(gameA).ToList();
    Assert.Single(gpA);
    Assert.False(gpA[0].Active);
  }

  [Fact]
  public void RejoinAfterKick_RestoresPosition()
  {
    _client.CreatePlayerAndGetId("KickTarget");
    var gameId = _client.CreateGame(4);
    _client.Call(r => r.JoinGame(gameId));

    var movedPos = new DbVector3(7, 3, 7);
    _client.Call(r => r.MovePlayer(gameId, movedPos, 0f));

    var gpBeforeKick = _client.GamePlayersInSession(gameId).First();
    var posBeforeKick = _client.GetEntity(gpBeforeKick.EntityId).Position;
    Assert.Equal(movedPos, posBeforeKick);

    _client.Call(r => r.KickPlayerFromGame(gameId, "KickTarget"));
    var gpAfterKick = _client.GamePlayersInSession(gameId).First();
    Assert.False(gpAfterKick.Active);

    _client.Call(r => r.JoinGame(gameId));
    var gpAfterRejoin = _client.GamePlayersInSession(gameId).First(gp => gp.Active);
    var posAfterRejoin = _client.GetEntity(gpAfterRejoin.EntityId).Position;
    Assert.True(gpAfterRejoin.Active);
    Assert.Equal(movedPos, posAfterRejoin);
  }
}

public class ChatTests: IDisposable
{
  private readonly SpacetimeTestClient _client;

  public ChatTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void CreateChatSessionTest()
  {
    _client.CreatePlayerAndGetId("ChatCreator");
    _client.Call(r => r.CreateChatSession("TestChat"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat");
    Assert.Single(chatLookup);
  }

  [Fact]
  public void RemoveSessionTest()
  {
    _client.CreatePlayerAndGetId("RemoveHost");
    _client.Call(r => r.CreateChatSession("TestChat2"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat2");
    Assert.Single(chatLookup);
    _client.Call(r => r.RemoveChatSessionByName("TestChat2"));
    chatLookup = _client.Db.ChatSession.Name.Filter("TestChat2");
    Assert.False(chatLookup.First().Active);
  }

  [Fact]
  public void RemoveSessionTestById()
  {
    _client.CreatePlayerAndGetId("RemoveByIdHost");
    _client.Call(r => r.CreateChatSession("TestChat2"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat2");
    Assert.Single(chatLookup);
    var chatId = chatLookup.First().Id;
    _client.Call(r => r.RemoveChatSessionById(chatId));
    var chat = _client.Db.ChatSession.Id.Find(chatId);
    Assert.NotNull(chat);
    Assert.False(chat.Active);
  }

  [Fact]
  public void JoinChatSessionTest()
  {
    var playerId = _client.CreatePlayerAndGetId("TestName");
    _client.Call(r => r.CreateChatSession("TestChat4"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat4");
    _client.Call(r => r.JoinChatSession(chatLookup.First().Id));
    var membershipLookup = _client.Db.ChatSessionPlayer.SessionId.Filter(chatLookup.First().Id);
    Assert.Single(membershipLookup);
  }

  [Fact]
  public void ChatReducer_WithoutActivePlayer_Fails()
  {
    using var noPlayer = SpacetimeTestClient.Create();
    noPlayer.CallExpectFailure(r => r.CreateChatSession("TestChat5"));
    noPlayer.Dispose();
  }

  [Fact]
  public void LeaveChatSessionTest()
  {
    _client.CreatePlayerAndGetId("TestName2");
    _client.Call(r => r.CreateChatSession("TestChat6"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat6");
    _client.Call(r => r.JoinChatSession(chatLookup.First().Id));
    var membershipLookup = _client.Db.ChatSessionPlayer.SessionId.Filter(chatLookup.First().Id);
    Assert.Single(membershipLookup);
    _client.Call(r => r.LeaveChatSession(chatLookup.First().Id));
    membershipLookup = _client.Db.ChatSessionPlayer.SessionId.Filter(chatLookup.First().Id);
    Assert.Empty(membershipLookup);
  }

  [Fact]
  public void RemoveChatSession_CleansMemberships()
  {
    _client.CreatePlayerAndGetId("CleanupHost");
    _client.Call(r => r.CreateChatSession("CleanupChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("CleanupChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));
    Assert.Single(_client.Db.ChatSessionPlayer.SessionId.Filter(chatId));

    _client.Call(r => r.RemoveChatSessionById(chatId));
    Assert.Empty(_client.Db.ChatSessionPlayer.SessionId.Filter(chatId));
    Assert.False(_client.Db.ChatSession.Id.Find(chatId)!.Active);
  }

  [Fact]
  public void SendMessage_InsertsRow()
  {
    var playerId = _client.CreatePlayerAndGetId("Sender");
    _client.Call(r => r.CreateChatSession("MsgChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("MsgChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));

    _client.Call(r => r.SendMessage("Hello world", chatId));

    var messages = _client.Db.Message.SessionId.Filter(chatId).ToList();
    var msg = Assert.Single(messages);
    Assert.Equal("Hello world", msg.Body);
    Assert.Equal(playerId, msg.SenderPlayerId);
    Assert.False(msg.Deleted);
  }

  [Fact]
  public void SendMessage_MultipleMessages()
  {
    _client.CreatePlayerAndGetId("Chatter");
    _client.Call(r => r.CreateChatSession("MultiMsgChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("MultiMsgChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));

    _client.Call(r => r.SendMessage("First", chatId));
    _client.Call(r => r.SendMessage("Second", chatId));
    _client.Call(r => r.SendMessage("Third", chatId));

    var messages = _client.Db.Message.SessionId.Filter(chatId).ToList();
    Assert.Equal(3, messages.Count);
  }

  [Fact]
  public void SoftDeleteMessage_SetsDeletedFlag()
  {
    _client.CreatePlayerAndGetId("Deleter");
    _client.Call(r => r.CreateChatSession("SoftDelChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("SoftDelChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));
    _client.Call(r => r.SendMessage("Delete me softly", chatId));

    var msgId = _client.Db.Message.SessionId.Filter(chatId).First().Id;
    _client.Call(r => r.SoftDeleteMessage(msgId));

    var msg = _client.Db.Message.Id.Find(msgId);
    Assert.NotNull(msg);
    Assert.True(msg.Deleted);
  }

  [Fact]
  public void SoftDeleteMessage_NonexistentFails()
  {
    _client.CallExpectFailure(r => r.SoftDeleteMessage(999999));
  }

  [Fact]
  public void HardDeleteMessage_RemovesRow()
  {
    _client.CreatePlayerAndGetId("HardDeleter");
    _client.Call(r => r.CreateChatSession("HardDelChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("HardDelChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));
    _client.Call(r => r.SendMessage("Delete me forever", chatId));

    var msgId = _client.Db.Message.SessionId.Filter(chatId).First().Id;
    _client.Call(r => r.HardDeleteMessage(msgId));

    Assert.Null(_client.Db.Message.Id.Find(msgId));
  }

  [Fact]
  public void RemoveChatSessionByName_NonexistentNoOp()
  {
    _client.CreatePlayerAndGetId("NoOpHost");
    _client.Call(r => r.RemoveChatSessionByName("DoesNotExist"));
  }

  [Fact]
  public void RemoveChatSessionById_NonexistentFails()
  {
    _client.CallExpectFailure(r => r.RemoveChatSessionById(999999));
  }

  [Fact]
  public void MultiClient_JoinSameSession()
  {
    using var other = SpacetimeTestClient.Create();

    _client.CreatePlayerAndGetId("HostMulti");
    other.CreatePlayerAndGetId("GuestMulti");

    _client.Call(r => r.CreateChatSession("SharedChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("SharedChat").First().Id;

    _client.Call(r => r.JoinChatSession(chatId));
    other.Call(r => r.JoinChatSession(chatId));

    var members = other.Db.ChatSessionPlayer.SessionId.Filter(chatId).ToList();
    Assert.Equal(2, members.Count);

    other.ClearData();
  }
}
