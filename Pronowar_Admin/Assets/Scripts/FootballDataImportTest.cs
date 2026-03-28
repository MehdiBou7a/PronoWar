using System.Collections;
using System.Collections.Generic;
using System.Text;
using Firebase;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FootballDataImportTest : MonoBehaviour
{
    [Header("API")]
    public FootballDataApiClient apiClient;

    [Header("UI")]
    public TMP_InputField competitionCodeInput;   // ex: FL1 / PL / PD / CL
    public TMP_InputField matchdayInput;          // ex: 1
    public TMP_InputField seasonYearInput;        // ex: 2024
    public TMP_InputField matchesBulkInput;       // optional preview
    public TMP_Text messageText;
    public Button importButton;

    private FirebaseFirestore _db;

    private void Awake()
    {
        if (importButton != null)
            importButton.onClick.AddListener(() => StartCoroutine(ImportMatchesCoroutine()));
    }

    private IEnumerator ImportMatchesCoroutine()
    {
        SetMessage("");

        if (apiClient == null)
        {
            SetMessage("API client is not assigned.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(AdminState.SelectedJourneyId))
        {
            SetMessage("No journey selected.");
            yield break;
        }

        var depTask = FirebaseApp.CheckAndFixDependenciesAsync();
        yield return new WaitUntil(() => depTask.IsCompleted);

        if (depTask.Result != DependencyStatus.Available)
        {
            SetMessage("Firebase not ready.");
            yield break;
        }

        _db = FirebaseFirestore.DefaultInstance;

        string journeyId = AdminState.SelectedJourneyId;
        string competitionCode = competitionCodeInput != null ? competitionCodeInput.text.Trim().ToUpperInvariant() : "";
        string matchdayRaw = matchdayInput != null ? matchdayInput.text.Trim() : "";
        string seasonRaw = seasonYearInput != null ? seasonYearInput.text.Trim() : "";

        if (!int.TryParse(matchdayRaw, out int matchday))
        {
            SetMessage("Invalid matchday.");
            yield break;
        }

        if (!int.TryParse(seasonRaw, out int seasonYear))
        {
            SetMessage("Invalid season year.");
            yield break;
        }

        bool done = false;
        bool success = false;
        FootballDataMatchesResponse apiResponse = null;
        string errorMessage = "";

        yield return apiClient.GetCompetitionMatches(
            competitionCode,
            matchday,
            seasonYear,
            response =>
            {
                success = true;
                done = true;
                apiResponse = response;
            },
            error =>
            {
                success = false;
                done = true;
                errorMessage = error;
            }
        );

        if (!done)
        {
            SetMessage("Unexpected import state.");
            yield break;
        }

        if (!success || apiResponse == null)
        {
            SetMessage(errorMessage);
            yield break;
        }

        // Optional preview in bulk input
        if (matchesBulkInput != null)
        {
            var sb = new StringBuilder();
            foreach (var match in apiResponse.matches)
            {
                if (match?.homeTeam == null || match.awayTeam == null) continue;
                sb.AppendLine($"{match.homeTeam.name} - {match.awayTeam.name}");
            }
            matchesBulkInput.text = sb.ToString().Trim();
        }

        // Load existing matches to avoid duplicates
        var matchesCol = _db.Collection("gameDays").Document(journeyId).Collection("matches");
        var existingTask = matchesCol.GetSnapshotAsync();
        yield return new WaitUntil(() => existingTask.IsCompleted);

        var existingSnap = existingTask.Result;

        var existingApiIds = new HashSet<string>();
        int maxIndex = 0;

        foreach (var doc in existingSnap.Documents)
        {
            if (doc.ContainsField("apiMatchId") && doc.GetValue<object>("apiMatchId") != null)
                existingApiIds.Add(doc.GetValue<long>("apiMatchId").ToString());

            string id = doc.Id; // m001
            if (id.Length > 1 && id[0] == 'm' && int.TryParse(id.Substring(1), out int n))
                if (n > maxIndex) maxIndex = n;
        }

        var batch = _db.StartBatch();

        int imported = 0;
        int skipped = 0;
        int idx = maxIndex + 1;

        foreach (var match in apiResponse.matches)
        {
            if (match == null || match.homeTeam == null || match.awayTeam == null)
            {
                skipped++;
                continue;
            }

            string apiMatchId = match.id.ToString();
            if (existingApiIds.Contains(apiMatchId))
            {
                skipped++;
                continue;
            }

            string matchId = $"m{idx:000}";
            idx++;

            var data = new Dictionary<string, object>
            {
                { "matchId", matchId },
                { "apiMatchId", match.id },

                { "competitionId", match.competition != null ? match.competition.id : 0 },
                { "competitionCode", match.competition != null ? match.competition.code : "" },
                { "competitionName", match.competition != null ? match.competition.name : "" },

                { "seasonId", match.season != null ? match.season.id : 0 },
                { "seasonStartDate", match.season != null ? match.season.startDate : "" },
                { "seasonEndDate", match.season != null ? match.season.endDate : "" },

                { "matchday", match.matchday },
                { "stage", match.stage ?? "" },
                { "group", match.group ?? "" },
                { "utcDate", match.utcDate ?? "" },
                { "status", "scheduled" },
                { "apiStatus", match.status ?? "" },
                { "lastUpdated", match.lastUpdated ?? "" },

                { "homeTeamId", match.homeTeam.id },
                { "homeTeam", match.homeTeam.name ?? "" },

                { "awayTeamId", match.awayTeam.id },
                { "awayTeam", match.awayTeam.name ?? "" },

                { "scoreWinner", match.score != null ? match.score.winner ?? "" : "" },

                { "scoreHome", null },
                { "scoreAway", null },

                { "scoreHomeFullTime", match.score != null && match.score.fullTime != null ? (object)match.score.fullTime.home : null },
                { "scoreAwayFullTime", match.score != null && match.score.fullTime != null ? (object)match.score.fullTime.away : null },

                { "scoreHomeHalfTime", match.score != null && match.score.halfTime != null ? (object)match.score.halfTime.home : null },
                { "scoreAwayHalfTime", match.score != null && match.score.halfTime != null ? (object)match.score.halfTime.away : null },

                { "createdAt", FieldValue.ServerTimestamp },
                { "updatedAt", FieldValue.ServerTimestamp }
            };

            batch.Set(matchesCol.Document(matchId), data, SetOptions.MergeAll);
            imported++;
        }

        batch.Update(_db.Collection("gameDays").Document(journeyId), new Dictionary<string, object>
        {
            { "updatedAt", FieldValue.ServerTimestamp },
            { "status", "open" }
        });

        var commitTask = batch.CommitAsync();
        yield return new WaitUntil(() => commitTask.IsCompleted);

        if (commitTask.Exception != null)
        {
            SetMessage("Firestore import failed: " + commitTask.Exception.Message);
            yield break;
        }

        SetMessage($"Imported {imported} match(es). Skipped {skipped} duplicate(s).");
    }

    private void SetMessage(string msg)
    {
        if (messageText != null)
            messageText.text = msg;

        Debug.Log("[FootballDataImportTest] " + msg);
    }
}