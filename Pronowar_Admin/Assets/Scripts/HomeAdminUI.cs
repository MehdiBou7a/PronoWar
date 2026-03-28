using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HomeAdminUI : MonoBehaviour
{
    [Header("Panels / Navigation")]
    public AdminUIController ui; // doit avoir ShowHome/ShowMatches/ShowScore

    [Header("Home UI")]
    public TMP_Dropdown openJourneyDropdown;
    public TMP_InputField newJourneyNameInput;
    public TMP_Text homeMessageText;

    [Header("Buttons")]
    public Button createJourneyButton;
    public Button deleteJourneyButton;
    public Button modifyJourneyButton;
    public Button scoresButton;

    private FirebaseFirestore _db;

    public string SelectedJourneyId { get; private set; }
    public string SelectedJourneyName { get; private set; }

    private const string NEW_OPTION = "New...";

    // ----------------------------
    // Public refresh (called after login)
    // ----------------------------
    public void RefreshHome()
    {
        _ = ReloadOpenJourneysAsync();
    }

    private async void Start()
    {
        SetMessage("");

        var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dep != DependencyStatus.Available)
        {
            SetMessage("Firebase dependencies error.");
            Debug.LogError("Firebase deps error: " + dep);
            return;
        }

        _db = FirebaseFirestore.DefaultInstance;

        WireButtons();
        await ReloadOpenJourneysAsync();

        Debug.Log("[HomeAdminUI] Dropdown loaded.");
    }

    private void WireButtons()
    {
        if (openJourneyDropdown != null)
            openJourneyDropdown.onValueChanged.AddListener(_ => OnDropdownChanged());

        if (createJourneyButton != null)
            createJourneyButton.onClick.AddListener(() => _ = OnCreateJourneyAsync());

        if (deleteJourneyButton != null)
            deleteJourneyButton.onClick.AddListener(() => _ = OnDeleteJourneyAsync());

        if (modifyJourneyButton != null)
            modifyJourneyButton.onClick.AddListener(() =>
            {
                Debug.Log($"[HOME] Modify clicked. ui={(ui != null ? "OK" : "NULL")} | selected={SelectedJourneyId}");
                if (ui != null) ui.ShowMatches();
            });

        if (scoresButton != null)
            scoresButton.onClick.AddListener(() =>
            {
                if (ui != null) ui.ShowScore();
            });
    }

    // ---- Firestore Paths ----
    private CollectionReference DaysCol => _db.Collection("gameDays");

    private async Task ReloadOpenJourneysAsync()
    {
        if (_db == null)
        {
            Debug.LogWarning("[HomeAdminUI] Firestore not ready yet.");
            return;
        }

        SetMessage("Loading open journeys...");

        var snap = await DaysCol.WhereEqualTo("status", "open").GetSnapshotAsync();

        var options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData(NEW_OPTION)
        };

        foreach (var doc in snap.Documents)
        {
            string id = doc.Id;
            string name = doc.ContainsField("name") ? doc.GetValue<string>("name") : id;
            options.Add(new TMP_Dropdown.OptionData($"{name} ({id})"));
        }

        openJourneyDropdown.ClearOptions();
        openJourneyDropdown.AddOptions(options);
        openJourneyDropdown.value = 0;
        openJourneyDropdown.RefreshShownValue();

        if (newJourneyNameInput != null)
        {
            newJourneyNameInput.gameObject.SetActive(true);
            newJourneyNameInput.text = "";
        }

        SelectedJourneyId = null;
        SelectedJourneyName = null;

        OnDropdownChanged();
        SetMessage(snap.Count == 0 ? "No open journeys." : "");
    }

    private void OnDropdownChanged()
    {
        SetMessage("");

        bool isNew = (openJourneyDropdown.value == 0);

        if (newJourneyNameInput != null)
            newJourneyNameInput.gameObject.SetActive(isNew);

        if (createJourneyButton != null) createJourneyButton.interactable = isNew;
        if (deleteJourneyButton != null) deleteJourneyButton.interactable = !isNew;
        if (modifyJourneyButton != null) modifyJourneyButton.interactable = !isNew;
        if (scoresButton != null) scoresButton.interactable = !isNew;

        if (!isNew)
        {
            var label = openJourneyDropdown.options[openJourneyDropdown.value].text;

            var m = Regex.Match(label, @"\(([^)]+)\)\s*$");
            SelectedJourneyId = m.Success ? m.Groups[1].Value : null;

            SelectedJourneyName = label;

            AdminState.SelectedJourneyId = SelectedJourneyId;
            AdminState.SelectedJourneyName = SelectedJourneyName;
        }
        else
        {
            SelectedJourneyId = null;
            SelectedJourneyName = null;

            AdminState.SelectedJourneyId = null;
            AdminState.SelectedJourneyName = null;
        }
    }

    private async Task OnCreateJourneyAsync()
    {
        SetMessage("");

        string name = newJourneyNameInput != null ? newJourneyNameInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(name))
        {
            SetMessage("Please enter a Journey Name.");
            return;
        }

        string journeyId = MakeRobustJourneyId(name);

        // check name already used in OPEN
        var existingByName = await DaysCol.WhereEqualTo("status", "open")
                                          .WhereEqualTo("name", name)
                                          .GetSnapshotAsync();
        if (existingByName.Count > 0)
        {
            SetMessage("This journey already exists in the database.");
            return;
        }

        var docRef = DaysCol.Document(journeyId);
        var docSnap = await docRef.GetSnapshotAsync();
        if (docSnap.Exists)
        {
            SetMessage("This journey already exists in the database.");
            return;
        }

        var data = new Dictionary<string, object>
        {
            { "name", name },
            { "status", "open" },
            { "createdAt", FieldValue.ServerTimestamp },
            { "updatedAt", FieldValue.ServerTimestamp }
        };

        await docRef.SetAsync(data);
        SetMessage($"Journey created: {name}");

        await ReloadOpenJourneysAsync();

        // select newly created
        for (int i = 0; i < openJourneyDropdown.options.Count; i++)
        {
            if (openJourneyDropdown.options[i].text.Contains($"({journeyId})"))
            {
                openJourneyDropdown.value = i;
                openJourneyDropdown.RefreshShownValue();
                OnDropdownChanged();
                break;
            }
        }

        if (ui != null) ui.ShowMatches();
    }

    private async Task OnDeleteJourneyAsync()
    {
        SetMessage("");

        if (string.IsNullOrWhiteSpace(SelectedJourneyId))
        {
            SetMessage("Please select a journey to delete.");
            return;
        }

        string id = SelectedJourneyId;
        SetMessage("Deleting journey...");

        var matchesCol = DaysCol.Document(id).Collection("matches");
        var matchesSnap = await matchesCol.GetSnapshotAsync();

        var batch = _db.StartBatch();
        int ops = 0;

        foreach (var doc in matchesSnap.Documents)
        {
            batch.Delete(doc.Reference);
            ops++;
            if (ops >= 450)
            {
                await batch.CommitAsync();
                batch = _db.StartBatch();
                ops = 0;
            }
        }

        batch.Delete(DaysCol.Document(id));
        await batch.CommitAsync();

        SetMessage($"Journey deleted: {id}");
        await ReloadOpenJourneysAsync();
    }

    // --- Robust ID (slug + short hash) ---
    private static string MakeRobustJourneyId(string journeyName)
    {
        string slug = journeyName.Trim().ToLowerInvariant();
        slug = RemoveDiacritics(slug);
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "_");
        slug = Regex.Replace(slug, @"_+", "_").Trim('_');

        string hash = ShortHashHex(journeyName.Trim().ToLowerInvariant(), 6);
        return $"{slug}_{hash}";
    }

    private static string ShortHashHex(string input, int hexLen)
    {
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length && sb.Length < hexLen; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString().Substring(0, hexLen);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private void SetMessage(string msg)
    {
        if (homeMessageText != null) homeMessageText.text = msg;
    }
}