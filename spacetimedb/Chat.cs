using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Reducer]
  public static void CreateChatSession(ReducerContext ctx, string name)
  {
    var player = GetPlayerForSender(ctx);
    ctx.Db.ChatSession.Insert(new ChatSession{
      OwnerPlayerId = player.Id,
      TimeCreated = ctx.Timestamp,
      Name = name,
      Active = true
    });
  }

  [SpacetimeDB.Reducer]
  public static void RemoveChatSessionByName(ReducerContext ctx, string name)
  {
    var player = GetPlayerForSender(ctx);
    var ChatSessions = ctx.Db.ChatSession.OwnerPlayerId.Filter(player.Id);
    foreach(var session in ChatSessions){
      if(session.Name.Equals(name)){
        RemoveChatSessionById(ctx, session.Id);
      }
    }
  }

  [SpacetimeDB.Reducer]
  public static void RemoveChatSessionById(ReducerContext ctx, ulong id)
  {
    foreach(var sessionMembership in ctx.Db.ChatSessionPlayer.SessionId.Filter(id)){
      ctx.Db.ChatSessionPlayer.Id.Delete(sessionMembership.Id);
    }
    var originalChatSession = ctx.Db.ChatSession.Id.Find(id)?? throw new Exception("Chat session not found");
    originalChatSession.Active = false;
    ctx.Db.ChatSession.Id.Update(originalChatSession);
  }

  [SpacetimeDB.Reducer]
  public static void JoinChatSession(ReducerContext ctx, ulong id)
  {
    var player = GetPlayerForSender(ctx);
    ctx.Db.ChatSessionPlayer.Insert(new ChatSessionPlayer
    {
      PlayerId = player.Id,
      SessionId = id
    });
  }

  [SpacetimeDB.Reducer]
  public static void LeaveChatSession(ReducerContext ctx, ulong id)
  {
    var player = GetPlayerForSender(ctx);
    foreach(var chatsession in ctx.Db.ChatSessionPlayer.PlayerId.Filter(player.Id)){
      if(chatsession.SessionId == id){
        ctx.Db.ChatSessionPlayer.Id.Delete(chatsession.Id);
      }
    }
  }

  [SpacetimeDB.Reducer]
  public static void SendMessage(ReducerContext ctx, string message, ulong chatSessionId)
  {
    var player = GetPlayerForSender(ctx);
    ctx.Db.Message.Insert(new Message
    {
      Body = message,
      SessionId = chatSessionId,
      SenderPlayerId = player.Id,
      TimeSent = ctx.Timestamp,
      Deleted = false
    });
  }

  [SpacetimeDB.Reducer]
  public static void SoftDeleteMessage(ReducerContext ctx, ulong id)
  {
    var originalMessage = ctx.Db.Message.Id.Find(id)?? throw new Exception("Message not found");
    originalMessage.Deleted = true;
    ctx.Db.Message.Id.Update(originalMessage);
  }

  [SpacetimeDB.Reducer]
  public static void HardDeleteMessage(ReducerContext ctx, ulong id)
  {
    ctx.Db.Message.Id.Delete(id);
  }
}
