using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchRowUI : MonoBehaviour
{
    public TMP_Text labelText;
    public Button deleteButton;

    private string _matchId;
    private System.Action<string> _onDelete;

    public void Setup(string matchId, string label, System.Action<string> onDelete)
    {
        _matchId = matchId;
        _onDelete = onDelete;

        if (labelText != null) labelText.text = label;

        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(() => _onDelete?.Invoke(_matchId));
        }
    }
}