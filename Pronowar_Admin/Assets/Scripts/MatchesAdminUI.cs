using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchesAdminUI : MonoBehaviour
{
    [Header("Navigation")]
    public AdminUIController ui;

    [Header("Top UI")]
    public TMP_Text journeyTitleText;
    public TMP_Text messageText;

    [Header("Scroll List")]
    public Transform matchesContent;         // MatchesScrollView/Viewport/MatchesContent
    public MatchRowUI matchRowPrefab;        // Prefab MatchRow

    [Header("Add Matches")]
    public TMP_InputField matchesBulkInput;  // lines: "PSG - OM"
    public Button addMatchesButton;

    [Header("Delete Matches (optional by text input)")]
    public TMP_InputField deleteMatchesInput;   // lines: "m001" or "PSG - OM"
    public Button deleteMatchesButton;

    [Header("Actions")]
    public Button setScoreButton;            // go to Score panel (next step)
    public Button cancelButton;

    private FirebaseFirestore _db;
    private CollectionReference DaysCol => _db.Collection("gameDays");

    private void Awake()
    {
        if (addMatchesButton != null) addMatchesButton.onClick.AddListener(() => _ = AddMatchesBulkAsync());
        if (deleteMatchesButton != null) deleteMatchesButton.onClick.AddListener(() => _ = DeleteMatchesFromInputAsync());

        if (cancelButton != null) cancelButton.onClick.AddListener(() => ui.ShowHome());

        if (setScoreButton != null) setScoreButton.onClick.AddListener(() =>
        {
            // Next step: Score panel
            ui.ShowScore();
        });
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
            SetMessage("No journey selected. Back to Home.");
            ui.ShowHome();
            return;
        }

        await RefreshUIAsync();
    }

    private DocumentReference DayDoc(string dayId) => DaysCol.Document(dayId);
    private CollectionReference MatchesCol(string dayId) => DayDoc(dayId).Collection("matches");
    private DocumentReference MatchDoc(string dayId, string matchId) => MatchesCol(dayId).Document(matchId);

    private async Task RefreshUIAsync()
    {
        string dayId = AdminState.SelectedJourneyId;
        string dayName = ExtractName(AdminState.SelectedJourneyName) ?? dayId;

        if (journeyTitleText != null) journeyTitleText.text = $"Journey: {dayName}";

        await RefreshMatchesListUIAsync();

        if (setScoreButton != null)
        {
            var snap = await MatchesCol(dayId).GetSnapshotAsync();
            setScoreButton.interactable = snap.Count > 0;
        }
    }

    // -------------------------
    // LIST UI (ScrollView)
    // -------------------------
    private async Task RefreshMatchesListUIAsync()
    {
        if (matchesContent == null || matchRowPrefab == null)
        {
            Debug.LogWarning("[MatchesAdminUI] matchesContent or matchRowPrefab not set in Inspector.");
            return;
        }

        // Clear existing rows
        for (int i = matchesContent.childCount - 1; i >= 0; i--)
            Destroy(matchesContent.GetChild(i).gameObject);

        string dayId = AdminState.SelectedJourneyId;
        var snap = await MatchesCol(dayId).GetSnapshotAsync();

        if (snap.Count == 0)
        {
            SetMessage("No matches yet for this journey.");
            return;
        }

        // Sort by id m001, m002...
        var docs = snap.Documents.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var doc in docs)
        {
            string matchId = doc.Id;
            string home = doc.ContainsField("homeTeam") ? doc.GetValue<string>("homeTeam") : "?";
            string away = doc.ContainsField("awayTeam") ? doc.GetValue<string>("awayTeam") : "?";

            string label = $"{matchId} — {home} vs {away}";

            var row = Instantiate(matchRowPrefab, matchesContent);
            row.Setup(matchId, label, OnDeleteMatchClicked);
        }
    }

    private void OnDeleteMatchClicked(string matchId)
    {
        _ = DeleteMatchByIdAsync(matchId);
    }

    private async Task DeleteMatchByIdAsync(string matchId)
    {
        SetMessage("");

        string dayId = AdminState.SelectedJourneyId;
        if (string.IsNullOrWhiteSpace(dayId)) return;

        var daySnap = await DayDoc(dayId).GetSnapshotAsync();
        string status = daySnap.Exists && daySnap.ContainsField("status") ? daySnap.GetValue<string>("status") : "open";
        if (status == "closed")
        {
            SetMessage("Journey is CLOSED. You cannot delete matches.");
            return;
        }

        var docRef = MatchDoc(dayId, matchId);
        var snap = await docRef.GetSnapshotAsync();
        if (!snap.Exists)
        {
            SetMessage($"Match not found: {matchId}");
            return;
        }

        await docRef.DeleteAsync();
        await DayDoc(dayId).UpdateAsync("updatedAt", FieldValue.ServerTimestamp);

        SetMessage($"Deleted: {matchId}");
        await RefreshUIAsync();
    }

    // -------------------------
    // ADD (bulk) with rules:
    // - Journey must be OPEN
    // - No duplicate match (home/away or away/home)
    // - A team can appear only once in the journey
    // -------------------------
    private async Task AddMatchesBulkAsync()
    {
        SetMessage("");

        string dayId = AdminState.SelectedJourneyId;
        if (string.IsNullOrWhiteSpace(dayId))
        {
            SetMessage("No journey selected.");
            return;
        }

        var daySnap = await DayDoc(dayId).GetSnapshotAsync();
        if (!daySnap.Exists)
        {
            SetMessage("Journey not found.");
            return;
        }

        string status = daySnap.ContainsField("status") ? daySnap.GetValue<string>("status") : "open";
        if (status == "closed")
        {
            SetMessage("Journey is CLOSED. You cannot add matches.");
            return;
        }

        var pairs = ParsePairs(matchesBulkInput != null ? matchesBulkInput.text : "");
        if (pairs.Count == 0)
        {
            SetMessage("Nothing to add. Use lines like: PSG - OM");
            return;
        }

        // Load existing matches for constraints
        var existingSnap = await MatchesCol(dayId).GetSnapshotAsync();

        var usedTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedPairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int maxIndex = 0;

        foreach (var d in existingSnap.Documents)
        {
            string id = d.Id;
            if (id.Length > 1 && id[0] == 'm' && int.TryParse(id.Substring(1), out int n))
                maxIndex = Math.Max(maxIndex, n);

            string h = d.ContainsField("homeTeam") ? d.GetValue<string>("homeTeam") : "";
            string a = d.ContainsField("awayTeam") ? d.GetValue<string>("awayTeam") : "";

            if (!string.IsNullOrWhiteSpace(h)) usedTeams.Add(NormTeam(h));
            if (!string.IsNullOrWhiteSpace(a)) usedTeams.Add(NormTeam(a));

            usedPairKeys.Add(PairKey(h, a));
            usedPairKeys.Add(PairKey(a, h));
        }

        var batch = _db.StartBatch();
        int idx = maxIndex + 1;

        int added = 0;
        var warnings = new List<string>();

        foreach (var (home, away) in pairs)
        {
            string nh = NormTeam(home);
            string na = NormTeam(away);

            // TEAM already used in journey
            if (usedTeams.Contains(nh) || usedTeams.Contains(na))
            {
                warnings.Add($"Team already has a match in this journey: {home} / {away}");
                continue;
            }

            // MATCH already exists (also reverse)
            if (usedPairKeys.Contains(PairKey(home, away)))
            {
                warnings.Add($"Match already exists: {home} vs {away}");
                continue;
            }

            string matchId = $"m{idx:000}";
            idx++;

            var data = new Dictionary<string, object>
            {
                { "matchId", matchId },
                { "homeTeam", home.Trim() },
                { "awayTeam", away.Trim() },
                { "scoreHome", null },
                { "scoreAway", null },
                { "status", "scheduled" },
                { "createdAt", FieldValue.ServerTimestamp },
                { "updatedAt", FieldValue.ServerTimestamp }
            };

            batch.Set(MatchDoc(dayId, matchId), data, SetOptions.MergeAll);

            usedTeams.Add(nh);
            usedTeams.Add(na);
            usedPairKeys.Add(PairKey(home, away));
            usedPairKeys.Add(PairKey(away, home));
            added++;
        }

        batch.Update(DayDoc(dayId), new Dictionary<string, object>
        {
            { "updatedAt", FieldValue.ServerTimestamp },
            { "status", "open" }
        });

        await batch.CommitAsync();

        if (warnings.Count > 0) Debug.LogWarning(string.Join(" | ", warnings));
        SetMessage(warnings.Count > 0
            ? $"Added {added}. Warnings: {warnings.Count} (see Console)"
            : $"Added {added} match(es).");

        await RefreshUIAsync();
    }

    // -------------------------
    // DELETE from Input (optional)
    // -------------------------
    private async Task DeleteMatchesFromInputAsync()
    {
        SetMessage("");

        string dayId = AdminState.SelectedJourneyId;
        if (string.IsNullOrWhiteSpace(dayId))
        {
            SetMessage("No journey selected.");
            return;
        }

        var daySnap = await DayDoc(dayId).GetSnapshotAsync();
        string status = daySnap.Exists && daySnap.ContainsField("status") ? daySnap.GetValue<string>("status") : "open";
        if (status == "closed")
        {
            SetMessage("Journey is CLOSED. You cannot delete matches.");
            return;
        }

        string raw = deleteMatchesInput != null ? deleteMatchesInput.text : "";
        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .ToList();

        if (lines.Count == 0)
        {
            SetMessage("Enter match IDs (m001) or lines like: PSG - OM");
            return;
        }

        // map existing matches
        var snap = await MatchesCol(dayId).GetSnapshotAsync();
        var idSet = new HashSet<string>(snap.Documents.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);

        var pairToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in snap.Documents)
        {
            string id = d.Id;
            string h = d.ContainsField("homeTeam") ? d.GetValue<string>("homeTeam") : "";
            string a = d.ContainsField("awayTeam") ? d.GetValue<string>("awayTeam") : "";
            pairToId[PairKey(h, a)] = id;
        }

        var batch = _db.StartBatch();
        int deleted = 0;
        var notFound = new List<string>();

        foreach (var line in lines)
        {
            string matchId = null;

            if (Regex.IsMatch(line, @"^m\d{3}$", RegexOptions.IgnoreCase))
            {
                matchId = line.ToLowerInvariant();
                if (!idSet.Contains(matchId)) { notFound.Add(line); continue; }
            }
            else
            {
                var pairs = ParsePairs(line);
                if (pairs.Count == 0) { notFound.Add(line); continue; }

                var (home, away) = pairs[0];
                var key = PairKey(home, away);
                if (!pairToId.TryGetValue(key, out matchId))
                {
                    notFound.Add(line);
                    continue;
                }
            }

            batch.Delete(MatchDoc(dayId, matchId));
            deleted++;
        }

        batch.Update(DayDoc(dayId), new Dictionary<string, object> { { "updatedAt", FieldValue.ServerTimestamp } });
        await batch.CommitAsync();

        SetMessage(notFound.Count > 0
            ? $"Deleted {deleted}. Not found: {string.Join(", ", notFound)}"
            : $"Deleted {deleted} match(es).");

        await RefreshUIAsync();
    }

    // -------------------------
    // Helpers
    // -------------------------
    private void SetMessage(string msg)
    {
        if (messageText != null) messageText.text = msg;
    }

    private static string ExtractName(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var m = Regex.Match(label, @"^(.*)\s+\([^)]+\)\s*$");
        return m.Success ? m.Groups[1].Value.Trim() : label;
    }

    private static List<(string home, string away)> ParsePairs(string raw)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line0 in lines)
        {
            var line = line0.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            line = line.Replace(" vs ", "-").Replace(" VS ", "-").Replace(" Vs ", "-");
            line = line.Replace(";", "-");

            var parts = line.Split('-');
            if (parts.Length < 2) continue;

            string home = parts[0].Trim();
            string away = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) continue;

            list.Add((home, away));
        }
        return list;
    }

    private static string NormTeam(string s)
    {
        s = (s ?? "").Trim().ToUpperInvariant();
        s = Regex.Replace(s, @"\s+", " ");
        return s;
    }

    private static string PairKey(string home, string away)
    {
        return $"{NormTeam(home)}|{NormTeam(away)}";
    }
}