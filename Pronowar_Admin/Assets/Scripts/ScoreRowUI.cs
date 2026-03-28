using TMPro;
using UnityEngine;

public class ScoreRowUI : MonoBehaviour
{
    public TMP_Text labelText;
    public TMP_InputField homeInput;
    public TMP_InputField awayInput;

    [HideInInspector] public string matchId;

    public void SetLabel(string label) => labelText.text = label;

    public bool TryGetScores(out int home, out int away)
    {
        home = 0; away = 0;

        string h = homeInput != null ? homeInput.text.Trim() : "";
        string a = awayInput != null ? awayInput.text.Trim() : "";

        if (!int.TryParse(h, out home)) return false;
        if (!int.TryParse(a, out away)) return false;
        return true;
    }

    public void SetScores(int? home, int? away)
    {
        if (homeInput != null) homeInput.text = home.HasValue ? home.Value.ToString() : "";
        if (awayInput != null) awayInput.text = away.HasValue ? away.Value.ToString() : "";
    }
}