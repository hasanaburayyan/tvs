using SpacetimeDB;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NameEntryScript : MonoBehaviour
{
    public TMP_InputField nameInput;
    public Button submitButton;

    private void OnEnable()
    {
        submitButton.onClick.AddListener(OnSubmit);
        nameInput.onSubmit.AddListener(_ => OnSubmit());
    }

    private void OnDisable()
    {
        submitButton.onClick.RemoveListener(OnSubmit);
        nameInput.onSubmit.RemoveAllListeners();
    }

    private void OnSubmit()
    {
        Log.Info("Wow this was submitted");
        string playerName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(playerName))
            return;

        Debug.Log($"Player name entered: {playerName}");

        GameManager.connection.Reducers.CreatePlayer(playerName);
            
        gameObject.SetActive(false);
    }
}