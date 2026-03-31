using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ScoreAdminUI : MonoBehaviour
{
    [Header("Navigation")]
    public AdminUIController ui;

    [Header("Top UI")]
    public TMP_Text journeyTitleText;
    public TMP_Text messageText;

    [Header("Scroll List")]
    public RectTransform scoreContent;
    public ScoreRowUI scoreRowPrefab;

    [Header("Buttons")]
    public Button saveScoreButton;
    public Button closeJourneyButton;
    public Button backButton;   // back to Matches
    public Button cancelButton; // cancel to Home

    private FirebaseFirestore _db;
    private readonly List<ScoreRowUI> _rows = new();

    private CollectionReference DaysCol => _db.Collection("gameDays");
    private DocumentReference DayDoc(string dayId) => DaysCol.Document(dayId);
    private CollectionReference MatchesCol(string dayId) => DayDoc(dayId).Collection("matches");
    private DocumentReference MatchDoc(string dayId, string matchId) => MatchesCol(dayId).Document(matchId);

    private void Awake()
    {
        if (saveScoreButton != null) saveScoreButton.onClick.AddListener(() => _ = SaveScoresAsync());
        if (closeJourneyButton != null) closeJourneyButton.onClick.AddListener(() => _ = CloseJourneyAsync());

        if (backButton != null) backButton.onClick.AddListener(() => { if (ui != null) ui.ShowMatches(); });
        if (cancelButton != null) cancelButton.onClick.AddListener(() => { if (ui != null) ui.ShowHome(); });
    }

    private void OnEnable()
    {
        _ = InitAndRefreshAsync();
    }

    private async Task InitAndRefreshAsync()
    {
        SetMessage("");

        var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dep != DependencyStatus.Available)
        {
            SetMessage("Firebase not ready.");
            Debug.LogError(dep);
            return;
        }

        _db = FirebaseFirestore.DefaultInstance;

        if (string.IsNullOrWhiteSpace(AdminState.SelectedJourneyId))
        {
            SetMessage("No journey selected.");
            return;
        }

        await RefreshUIAsync();
    }

    private async Task RefreshUIAsync()
    {
        string dayId = AdminState.SelectedJourneyId;
        string dayName = ExtractName(AdminState.SelectedJourneyName) ?? dayId;

        if (journeyTitleText != null)
            journeyTitleText.text = $"Journey: {dayName}";

        await BuildRowsAsync();
        await UpdateButtonsAndInputsStateAsync();
    }

    private async Task BuildRowsAsync()
    {
        ClearRows();

        string dayId = AdminState.SelectedJourneyId;

        var snap = await MatchesCol(dayId).GetSnapshotAsync();
        if (snap.Count == 0)
        {
            SetMessage("No matches in this journey. Add matches first.");
            if (closeJourneyButton != null) closeJourneyButton.interactable = false;
            if (saveScoreButton != null) saveScoreButton.interactable = false;
            return;
        }

        var docs = snap.Documents.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var doc in docs)
        {
            string matchId = doc.Id;
            string home = doc.ContainsField("homeTeam") ? doc.GetValue<string>("homeTeam") : "?";
            string away = doc.ContainsField("awayTeam") ? doc.GetValue<string>("awayTeam") : "?";

            int? sh = null;
            int? sa = null;

            if (doc.ContainsField("scoreHome") && doc.GetValue<object>("scoreHome") != null)
                sh = Convert.ToInt32(doc.GetValue<long>("scoreHome"));

            if (doc.ContainsField("scoreAway") && doc.GetValue<object>("scoreAway") != null)
                sa = Convert.ToInt32(doc.GetValue<long>("scoreAway"));

            var go = Instantiate(scoreRowPrefab.gameObject);
            go.transform.SetParent(scoreContent, false);
            go.transform.localScale = Vector3.one;

            var rt = go.transform as RectTransform;
            if (rt != null)
            {
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.identity;
            }

            var row = go.GetComponent<ScoreRowUI>();
            row.matchId = matchId;
            row.SetLabel($"{matchId} — {home} vs {away}");
            row.SetScores(sh, sa);

            _rows.Add(row);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scoreContent);

        SetMessage("");
    }

    private async Task SaveScoresAsync()
    {
        SetMessage("");

        string dayId = AdminState.SelectedJourneyId;
        if (string.IsNullOrWhiteSpace(dayId))
        {
            SetMessage("No journey selected.");
            return;
        }

        var daySnap = await DayDoc(dayId).GetSnapshotAsync();
        string status = daySnap.Exists && daySnap.ContainsField("status")
            ? daySnap.GetValue<string>("status")
            : "open";

        if (status == "closed")
        {
            SetMessage("Journey is CLOSED. You cannot edit scores.");
            return;
        }

        int saved = 0;
        int invalid = 0;

        var batch = _db.StartBatch();

        foreach (var row in _rows)
        {
            if (row == null) continue;

            string h = row.homeInput != null ? row.homeInput.text.Trim() : "";
            string a = row.awayInput != null ? row.awayInput.text.Trim() : "";

            // nothing entered
            if (string.IsNullOrEmpty(h) && string.IsNullOrEmpty(a))
                continue;

            // partially filled = invalid
            if (string.IsNullOrEmpty(h) || string.IsNullOrEmpty(a))
            {
                invalid++;
                continue;
            }

            if (!row.TryGetScores(out int sh, out int sa))
            {
                invalid++;
                continue;
            }

            if (sh < 0 || sa < 0)
            {
                invalid++;
                continue;
            }

            var updates = new Dictionary<string, object>
            {
                { "scoreHome", sh },
                { "scoreAway", sa },
                { "status", "finished" },
                { "updatedAt", FieldValue.ServerTimestamp }
            };

            batch.Set(MatchDoc(dayId, row.matchId), updates, SetOptions.MergeAll);
            saved++;
        }

        batch.Update(DayDoc(dayId), new Dictionary<string, object>
        {
            { "updatedAt", FieldValue.ServerTimestamp }
        });

        await batch.CommitAsync();

        if (invalid > 0)
            SetMessage($"Saved {saved}. Invalid rows: {invalid}.");
        else
            SetMessage($"Saved {saved} score(s).");

        // Important: reload from Firestore to get exact truth
        await RefreshUIAsync();
    }

    private async Task UpdateButtonsAndInputsStateAsync()
    {
        string dayId = AdminState.SelectedJourneyId;
        if (string.IsNullOrWhiteSpace(dayId)) return;

        var daySnap = await DayDoc(dayId).GetSnapshotAsync();
        string journeyStatus = daySnap.Exists && daySnap.ContainsField("status")
            ? daySnap.GetValue<string>("status")
            : "open";

        var matchesSnap = await MatchesCol(dayId).GetSnapshotAsync();

        bool hasMatches = matchesSnap.Count > 0;
        bool allScored = hasMatches;

        foreach (var doc in matchesSnap.Documents)
        {
            bool hasHome = doc.ContainsField("scoreHome") && doc.GetValue<object>("scoreHome") != null;
            bool hasAway = doc.ContainsField("scoreAway") && doc.GetValue<object>("scoreAway") != null;

            if (!hasHome || !hasAway)
            {
                allScored = false;
                break;
            }
        }

        bool isClosed = journeyStatus == "closed";

        if (saveScoreButton != null)
            saveScoreButton.interactable = !isClosed && hasMatches;

        if (closeJourneyButton != null)
            closeJourneyButton.interactable = !isClosed && hasMatches && allScored;

        // Lock/unlock inputs
        foreach (var row in _rows)
        {
            if (row == null) continue;

            if (row.homeInput != null)
                row.homeInput.interactable = !isClosed;

            if (row.awayInput != null)
                row.awayInput.interactable = !isClosed;
        }

        if (isClosed)
        {
            SetMessage("Journey is CLOSED.");
        }
        else if (!hasMatches)
        {
            SetMessage("No matches in this journey.");
        }
        else if (!allScored)
        {
            SetMessage("Close is disabled until all matches have scores.");
        }
        else
        {
            SetMessage("All scores are filled. You can close the journey.");
        }
    }

    private async Task CloseJourneyAsync()
    {
        SetMessage("");

        string dayId = AdminState.SelectedJourneyId;
        if (string.IsNullOrWhiteSpace(dayId))
        {
            SetMessage("No journey selected.");
            return;
        }

        var daySnap = await DayDoc(dayId).GetSnapshotAsync();
        string journeyStatus = daySnap.Exists && daySnap.ContainsField("status")
            ? daySnap.GetValue<string>("status")
            : "open";

        if (journeyStatus == "closed")
        {
            SetMessage("Journey already closed.");
            return;
        }

        var snap = await MatchesCol(dayId).GetSnapshotAsync();
        if (snap.Count == 0)
        {
            SetMessage("No matches in this journey.");
            return;
        }

        foreach (var doc in snap.Documents)
        {
            bool hasHome = doc.ContainsField("scoreHome") && doc.GetValue<object>("scoreHome") != null;
            bool hasAway = doc.ContainsField("scoreAway") && doc.GetValue<object>("scoreAway") != null;

            if (!hasHome || !hasAway)
            {
                SetMessage("All matches must have scores before closing.");
                if (closeJourneyButton != null)
                    closeJourneyButton.interactable = false;
                return;
            }
        }

        await DayDoc(dayId).UpdateAsync(new Dictionary<string, object>
        {
            { "status", "closed" },
            { "closedAt", FieldValue.ServerTimestamp },
            { "updatedAt", FieldValue.ServerTimestamp }
        });

        SetMessage("Journey CLOSED.");
        await RefreshUIAsync();
    }

    private void ClearRows()
    {
        foreach (var r in _rows)
            if (r != null) Destroy(r.gameObject);

        _rows.Clear();
    }

    private void SetMessage(string msg)
    {
        if (messageText != null)
            messageText.text = msg;
    }

    private static string ExtractName(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var m = Regex.Match(label, @"^(.*)\s+\([^)]+\)\s*$");
        return m.Success ? m.Groups[1].Value.Trim() : label;
    }
}