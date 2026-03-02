using System.Linq;
using SpacetimeDB;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ServerBrowserScript : MonoBehaviour
{
    public TMP_Text nameInput;
    public Button refreshButton;
    public ServerListElement listElementPrefab;
    public RectTransform serverlistContent;

    public ServerBrowserScript()
    {
    }

    void Start()
    {
        refreshButton.onClick.AddListener(() =>
        {
            Log.Info("Refresh Servers");
            GetServerList();
        });
    }

    private void GetServerList()
    {
        Log.Info("Getting Server List");

        foreach (Transform child in serverlistContent)
        {
            Destroy(child.gameObject);
        }

        var servers = GameManager.connection.Db.GameSession.Iter();
        foreach (var server in servers)
        {
            var entry = Instantiate(listElementPrefab, serverlistContent);
            var currentPlayerList = GameManager.connection.Db.GamePlayer.GameSessionId.Filter(server.Id);
            var owner = GameManager.connection.Db.Player.Identity.Find(server.OwnerIdentity);
            entry.Fill(owner, server, currentPlayerList.Count());
        }
    }
}
