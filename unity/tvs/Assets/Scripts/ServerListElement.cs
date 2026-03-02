using System;
using SpacetimeDB.Types;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ServerListElement : MonoBehaviour
{
    public Button serverListElement;
    public TMP_Text buttonText;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Fill(Player owner, GameSession gameSession, int currentPlayerCount)
    {
        String conent =
            $"Id: {gameSession.Id}\tplayer count: {currentPlayerCount}/{gameSession.MaxPlayers}\tCreated by: {owner.Name}\n";
        buttonText.text = conent;
    }
}
