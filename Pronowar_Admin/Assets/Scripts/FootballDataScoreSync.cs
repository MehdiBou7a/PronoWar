using System.Collections;
using System.Collections.Generic;
using Firebase;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FootballDataScoreSync : MonoBehaviour
{
    [Header("API")]
    public FootballDataApiClient apiClient;

    [Header("UI")]
    public TMP_Text messageText;
    public Button syncScoresButton;

    private FirebaseFirestore _db;

    private void Awake()
    {
        if (syncScoresButton != null)
            syncScoresButton.onClick.AddListener(() => StartCoroutine(SyncScoresCoroutine()));
    }

    private IEnumerator SyncScoresCoroutine()
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

        string journeyId = AdminState.SelectedJourneyId;

        var depTask = FirebaseApp.CheckAndFixDependenciesAsync();
        yield return new WaitUntil(() => depTask.IsCompleted);

        if (depTask.Result != DependencyStatus.Available)
        {
            SetMessage("Firebase not ready.");
            yield break;
        }

        _db = FirebaseFirestore.DefaultInstance;

        var matchesCol = _db.Collection("gameDays").Document(journeyId).Collection("matches");
        var getMatchesTask = matchesCol.GetSnapshotAsync();
        yield return new WaitUntil(() => getMatchesTask.IsCompleted);

        if (getMatchesTask.Exception != null)
        {
            SetMessage("Failed to read Firestore matches.");
            yield break;
        }

        var matchesSnap = getMatchesTask.Result;
        if (matchesSnap.Count == 0)
        {
            SetMessage("No matches found in this journey.");
            yield break;
        }

        int updated = 0;
        int skipped = 0;
        int errors = 0;

        foreach (var doc in matchesSnap.Documents)
        {
            if (!doc.ContainsField("apiMatchId") || doc.GetValue<object>("apiMatchId") == null)
            {
                skipped++;
                continue;
            }

            int apiMatchId = System.Convert.ToInt32(doc.GetValue<long>("apiMatchId"));

            bool done = false;
            bool success = false;
            FootballDataMatch apiMatch = null;
            string errorMsg = "";

            yield return apiClient.GetMatchById(
                apiMatchId,
                match =>
                {
                    success = true;
                    done = true;
                    apiMatch = match;
                },
                error =>
                {
                    success = false;
                    done = true;
                    errorMsg = error;
                }
            );

            if (!done || !success || apiMatch == null)
            {
                Debug.LogWarning($"[ScoreSync] Failed for apiMatchId={apiMatchId}: {errorMsg}");
                errors++;
                continue;
            }

            // only update if finished and fullTime scores exist
            bool isFinished = apiMatch.status == "FINISHED";
            bool hasFullTime =
                apiMatch.score != null &&
                apiMatch.score.fullTime != null;

            var updates = new Dictionary<string, object>
            {
                { "apiStatus", apiMatch.status ?? "" },
                { "lastUpdated", apiMatch.lastUpdated ?? "" },
                { "scoreWinner", apiMatch.score != null ? apiMatch.score.winner ?? "" : "" },
                { "updatedAt", FieldValue.ServerTimestamp }
            };

            if (apiMatch.score != null && apiMatch.score.fullTime != null)
            {
                updates["scoreHomeFullTime"] = (object)apiMatch.score.fullTime.home ?? null;
                updates["scoreAwayFullTime"] = (object)apiMatch.score.fullTime.away ?? null;
            }

            if (apiMatch.score != null && apiMatch.score.halfTime != null)
            {
                updates["scoreHomeHalfTime"] = (object)apiMatch.score.halfTime.home ?? null;
                updates["scoreAwayHalfTime"] = (object)apiMatch.score.halfTime.away ?? null;
            }

            Debug.Log($"[ScoreSync] apiMatchId={apiMatchId} status={apiMatch.status} fullTimeHome={(apiMatch.score != null && apiMatch.score.fullTime != null ? apiMatch.score.fullTime.home.ToString() : "null")} fullTimeAway={(apiMatch.score != null && apiMatch.score.fullTime != null ? apiMatch.score.fullTime.away.ToString() : "null")}");

            if (isFinished && hasFullTime)
            {
                updates["scoreHome"] = apiMatch.score.fullTime.home;
                updates["scoreAway"] = apiMatch.score.fullTime.away;
                updates["status"] = "finished";
                updated++;
            }
            else
            {
                skipped++;
            }

            var updateTask = doc.Reference.SetAsync(updates, SetOptions.MergeAll);
            yield return new WaitUntil(() => updateTask.IsCompleted);

            if (updateTask.Exception != null)
            {
                Debug.LogWarning($"[ScoreSync] Firestore update failed for {doc.Id}");
                errors++;
            }

            // small pause to avoid hammering free API limit too fast
            yield return new WaitForSeconds(0.15f);
        }

        SetMessage($"Sync done. Updated: {updated}, skipped: {skipped}, errors: {errors}");
    }

    private void SetMessage(string msg)
    {
        if (messageText != null)
            messageText.text = msg;

        Debug.Log("[FootballDataScoreSync] " + msg);
    }
}