using UnityEngine;
using SpacetimeDB;
using SpacetimeDB.Types;

public class GameManager : MonoBehaviour
{
    public const string ServerAddress = "127.0.0.1:3000";
    public const string ModuleName = "tvs";
    
    public static GameManager Instance {get;  private set;}
    public static DbConnection connection {get; private set;}
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Instance = this;

        var builder = DbConnection.Builder()
            .OnConnect(HandleConnect)
            .OnConnectError((err) =>
            {
                Log.Error(err.Message);
            })
            .OnDisconnect((conn, err) =>
            {
                Log.Info("Disconnect estable");
                if (err != null)
                {
                    Log.Error(err.Message);
                }
            })
            .WithUri(ServerAddress)
            .WithDatabaseName(ModuleName);
        
        connection = builder.Build();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    // DB Handler functions
    public void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        Log.Info("Connection made to spacetime");

        conn.SubscriptionBuilder()
            .OnApplied((ctx) =>
            {})
            .SubscribeToAllTables();
    }
}
