using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class FootballDataCompetition
{
    public int id;
    public string name;
    public string code;
    public string type;
    public string emblem;
}

[Serializable]
public class FootballDataSeason
{
    public int id;
    public string startDate;
    public string endDate;
    public int currentMatchday;
}

[Serializable]
public class FootballDataTeam
{
    public int id;
    public string name;
    public string shortName;
    public string tla;
    public string crest;
}

[Serializable]
public class FootballDataScoreTime
{
    public int home;
    public int away;
}

[Serializable]
public class FootballDataScore
{
    public string winner;
    public string duration;
    public FootballDataScoreTime fullTime;
    public FootballDataScoreTime halfTime;
}

[Serializable]
public class FootballDataArea
{
    public int id;
    public string name;
    public string code;
    public string flag;
}

[Serializable]
public class FootballDataMatch
{
    public FootballDataArea area;
    public FootballDataCompetition competition;
    public FootballDataSeason season;

    public int id;
    public string utcDate;
    public string status;
    public string venue;
    public int matchday;
    public string stage;
    public string group;
    public string lastUpdated;

    public FootballDataTeam homeTeam;
    public FootballDataTeam awayTeam;
    public FootballDataScore score;
}

[Serializable]
public class FootballDataMatchesResponse
{
    public FootballDataCompetition competition;
    public FootballDataSeason season;
    public FootballDataMatch[] matches;
}

public class FootballDataApiClient : MonoBehaviour
{
    [Header("football-data.org")]
    [SerializeField] private string apiToken = "PUT_YOUR_TOKEN_HERE";
    [SerializeField] private string baseUrl = "https://api.football-data.org/v4";

    public IEnumerator GetCompetitionMatches(
        string competitionCode,
        int matchday,
        int seasonYear,
        Action<FootballDataMatchesResponse> onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(apiToken) || apiToken == "PUT_YOUR_TOKEN_HERE")
        {
            onError?.Invoke("API token is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(competitionCode))
        {
            onError?.Invoke("Competition code is empty.");
            yield break;
        }

        string url = $"{baseUrl}/competitions/{competitionCode}/matches?matchday={matchday}&season={seasonYear}";

        using UnityWebRequest req = UnityWebRequest.Get(url);
        req.SetRequestHeader("X-Auth-Token", apiToken);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"HTTP error {req.responseCode}: {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        string json = req.downloadHandler.text;

        FootballDataMatchesResponse response = null;
        try
        {
            response = JsonUtility.FromJson<FootballDataMatchesResponse>(json);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"JSON parse error: {ex.Message}\n{json}");
            yield break;
        }

        if (response == null || response.matches == null)
        {
            onError?.Invoke("No matches found in API response.");
            yield break;
        }

        onSuccess?.Invoke(response);
    }

    public IEnumerator GetMatchById(
    int apiMatchId,
    Action<FootballDataMatch> onSuccess,
    Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(apiToken) || apiToken == "PUT_YOUR_TOKEN_HERE")
        {
            onError?.Invoke("API token is missing.");
            yield break;
        }

        string url = $"{baseUrl}/matches/{apiMatchId}";

        using UnityWebRequest req = UnityWebRequest.Get(url);
        req.SetRequestHeader("X-Auth-Token", apiToken);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"HTTP error {req.responseCode}: {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        string json = req.downloadHandler.text;
        Debug.Log($"[FootballDataApiClient] Raw single match JSON: {json}");

        // sécurité : si l'API renvoie un objet d'erreur JSON
        if (json.Contains("\"error\""))
        {
            onError?.Invoke("API returned an error JSON: " + json);
            yield break;
        }

        FootballDataMatch response = null;
        try
        {
            response = JsonUtility.FromJson<FootballDataMatch>(json);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"JSON parse error: {ex.Message}\n{json}");
            yield break;
        }

        if (response == null || response.id == 0)
        {
            onError?.Invoke("No valid match found in API response.\n" + json);
            yield break;
        }

        onSuccess?.Invoke(response);
    }
}