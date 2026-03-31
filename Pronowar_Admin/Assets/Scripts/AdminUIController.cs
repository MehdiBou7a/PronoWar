using UnityEngine;
using TMPro;

public class AdminUIController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject LoginPanel;
    public GameObject HomePanel;
    public GameObject MatchesPanel;
    public GameObject ScorePanel;

    [Header("Messages")]
    public TMP_Text LoginMessageText;

    public enum AdminPanel
    {
        None,
        Login,
        Home,
        Matches,
        Score
    }

    public AdminPanel CurrentPanel { get; private set; } = AdminPanel.None;

    private void Awake()
    {
        ValidateReferences();
    }

    private void Start()
    {
        ShowLogin();
        ShowLoginMessage("");
    }

    private void ValidateReferences()
    {
        if (LoginPanel == null)
            Debug.LogWarning("[AdminUIController] LoginPanel is not assigned.");

        if (HomePanel == null)
            Debug.LogWarning("[AdminUIController] HomePanel is not assigned.");

        if (MatchesPanel == null)
            Debug.LogWarning("[AdminUIController] MatchesPanel is not assigned.");

        if (ScorePanel == null)
            Debug.LogWarning("[AdminUIController] ScorePanel is not assigned.");

        if (LoginMessageText == null)
            Debug.LogWarning("[AdminUIController] LoginMessageText is not assigned.");
    }

    private void SetOnly(GameObject panel, AdminPanel panelType)
    {
        SafeSetActive(LoginPanel, false);
        SafeSetActive(HomePanel, false);
        SafeSetActive(MatchesPanel, false);
        SafeSetActive(ScorePanel, false);

        if (panel != null)
        {
            panel.SetActive(true);
            CurrentPanel = panelType;
            Debug.Log($"[AdminUIController] Current panel = {panelType}");
        }
        else
        {
            CurrentPanel = AdminPanel.None;
            Debug.LogWarning($"[AdminUIController] Tried to show panel {panelType}, but reference is null.");
        }
    }

    private void SafeSetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    public void ShowLogin()
    {
        SetOnly(LoginPanel, AdminPanel.Login);
    }

    public void ShowHome()
    {
        SetOnly(HomePanel, AdminPanel.Home);
    }

    public void ShowMatches()
    {
        if (string.IsNullOrWhiteSpace(AdminState.SelectedJourneyId))
        {
            Debug.LogWarning("[AdminUIController] Cannot open Matches: no selected journey.");
            return;
        }

        SetOnly(MatchesPanel, AdminPanel.Matches);
    }

    public void ShowScore()
    {
        if (string.IsNullOrWhiteSpace(AdminState.SelectedJourneyId))
        {
            Debug.LogWarning("[AdminUIController] Cannot open Score: no selected journey.");
            return;
        }

        SetOnly(ScorePanel, AdminPanel.Score);
    }

    public void ShowLoginMessage(string msg)
    {
        if (LoginMessageText != null)
            LoginMessageText.text = msg ?? "";
    }

    public void ClearMessages()
    {
        ShowLoginMessage("");
    }

    public bool IsCurrentPanel(AdminPanel panel)
    {
        return CurrentPanel == panel;
    }
}