using UnityEngine;
using TMPro;

public class AdminUIController : MonoBehaviour
{
    public GameObject LoginPanel;
    public GameObject HomePanel;
    public GameObject MatchesPanel;
    public GameObject ScorePanel;

    public TMP_Text LoginMessageText;

    private void Start()
    {
        ShowLogin();
        ShowLoginMessage("");
    }

    private void SetOnly(GameObject panel)
    {
        if (LoginPanel != null) LoginPanel.SetActive(false);
        if (HomePanel != null) HomePanel.SetActive(false);
        if (MatchesPanel != null) MatchesPanel.SetActive(false);
        if (ScorePanel != null) ScorePanel.SetActive(false);

        if (panel != null)
            panel.SetActive(true);
    }

    public void ShowLogin() => SetOnly(LoginPanel);
    public void ShowHome() => SetOnly(HomePanel);
    public void ShowMatches() => SetOnly(MatchesPanel);
    public void ShowScore() => SetOnly(ScorePanel);

    public void ShowLoginMessage(string msg)
    {
        if (LoginMessageText != null)
            LoginMessageText.text = msg;
    }
}