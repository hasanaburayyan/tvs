using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Reducer]
  public static void CreateChatSession(ReducerContext ctx, string name)
  {
    ctx.Db.ChatSession.Insert(new ChatSession{
      Owner = ctx.Sender,
      TimeCreated = ctx.Timestamp,
      Name = name,
      Active = true
    });
  }
  [SpacetimeDB.Reducer]
  public static void RemoveChatSessionByName(ReducerContext ctx, string name)
  {
    var ChatSessions = ctx.Db.ChatSession.Owner.Filter(ctx.Sender);
    foreach(var session in ChatSessions){
      if(session.Name.Equals(name)){
        //ctx.Db.ChatSession.Id.Delete(session.Id);
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
    var player = ctx.Db.player.Identity.Find(ctx.Sender);
    if(player == null){
      throw new Exception("Player not found");
    }
    ctx.Db.ChatSessionPlayer.Insert(new ChatSessionPlayer
    {
      PlayerIdentity = ctx.Sender,
      SessionId = id
    });
  }
  [SpacetimeDB.Reducer]
  public static void LeaveChatSession(ReducerContext ctx, ulong id)
  {
    foreach(var chatsession in ctx.Db.ChatSessionPlayer.PlayerIdentity.Filter(ctx.Sender)){
      if(chatsession.SessionId == id){
        ctx.Db.ChatSessionPlayer.Id.Delete(chatsession.Id);
      }
    }
  }
  [SpacetimeDB.Reducer]
  public static void SendMessage(ReducerContext ctx, string message, ulong chatSessionId)
  {
    ctx.Db.Message.Insert(new Message
    {
      Body = message,
      SessionId = chatSessionId,
      Sender = ctx.Sender,
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