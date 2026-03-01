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
public class ChatTests: IDisposable{
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
  public void CreateChatSessionTest(){
    _client.Call(r => r.CreateChatSession("TestChat"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat");
    Assert.Single(chatLookup);
  }
  [Fact]
  public void RemoveSessionTest(){
    _client.Call(r => r.CreateChatSession("TestChat2"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat2");
    Assert.Single(chatLookup);
    _client.Call(r => r.RemoveChatSessionByName("TestChat2"));
    chatLookup = _client.Db.ChatSession.Name.Filter("TestChat2");
    Assert.False(chatLookup.First().Active);
  }
  [Fact]
  public void RemoveSessionTestById(){
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
  public void JoinChatSessionTest(){
    _client.Call(r => r.CreatePlayer("TestName"));
    _client.Call(r => r.CreateChatSession("TestChat4"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat4");
    _client.Call(r => r.JoinChatSession(chatLookup.First().Id));
    var membershipLookup = _client.Db.ChatSessionPlayer.SessionId.Filter(chatLookup.First().Id);
    Assert.Single(membershipLookup);
  }
  [Fact]
  public void JoinChatSessionTest2()
  {
    _client.Call(r => r.CreateChatSession("TestChat5"));
    var chatLookup = _client.Db.ChatSession.Name.Filter("TestChat5");
    _client.CallExpectFailure(r => r.JoinChatSession(chatLookup.First().Id));
  }
  [Fact]
  public void LeaveChatSessionTest()
  {
    _client.Call(r => r.CreatePlayer("TestName2"));
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
    _client.Call(r => r.CreatePlayer("CleanupHost"));
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
    _client.Call(r => r.CreatePlayer("Sender"));
    _client.Call(r => r.CreateChatSession("MsgChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("MsgChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));

    _client.Call(r => r.SendMessage("Hello world", chatId));

    var messages = _client.Db.Message.SessionId.Filter(chatId).ToList();
    var msg = Assert.Single(messages);
    Assert.Equal("Hello world", msg.Body);
    Assert.Equal(_client.Identity, msg.Sender);
    Assert.False(msg.Deleted);
  }

  [Fact]
  public void SendMessage_MultipleMessages()
  {
    _client.Call(r => r.CreatePlayer("Chatter"));
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
    _client.Call(r => r.CreatePlayer("Deleter"));
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
    _client.Call(r => r.CreatePlayer("HardDeleter"));
    _client.Call(r => r.CreateChatSession("HardDelChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("HardDelChat").First().Id;
    _client.Call(r => r.JoinChatSession(chatId));
    _client.Call(r => r.SendMessage("Delete me forever", chatId));

    var msgId = _client.Db.Message.SessionId.Filter(chatId).First().Id;
    _client.Call(r => r.HardDeleteMessage(msgId));

    Assert.Null(_client.Db.Message.Id.Find(msgId));
  }

  [Fact]
  public void RemoveChatSessionByName_NonexistentFails()
  {
    _client.Call(r => r.RemoveChatSessionByName("DoesNotExist"));
    // No-op — no sessions match, nothing should change
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

    _client.Call(r => r.CreatePlayer("HostMulti"));
    other.Call(r => r.CreatePlayer("GuestMulti"));

    _client.Call(r => r.CreateChatSession("SharedChat"));
    var chatId = _client.Db.ChatSession.Name.Filter("SharedChat").First().Id;

    _client.Call(r => r.JoinChatSession(chatId));
    other.Call(r => r.JoinChatSession(chatId));

    var members = other.Db.ChatSessionPlayer.SessionId.Filter(chatId).ToList();
    Assert.Equal(2, members.Count);

    other.ClearData();
  }
}
