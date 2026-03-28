using UnityEngine;
using TMPro;

public class AdminUIController : MonoBehaviour
{
    public GameObject LoginPanel;
    public GameObject HomePanel;
    public GameObject MatchesPanel;
    public GameObject ScorePanel;

    public TMP_Text LoginMessageText;

    private void SetOnly(GameObject panel)
    {
        LoginPanel.SetActive(panel == LoginPanel);
        HomePanel.SetActive(panel == HomePanel);
        MatchesPanel.SetActive(panel == MatchesPanel);
        ScorePanel.SetActive(panel == ScorePanel);
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