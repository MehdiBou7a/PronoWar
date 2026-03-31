using System;
using System.Collections.Generic;
using System.Globalization;
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
    private bool _isInitialized;
    private bool _isBusy;
    private bool _listenersWired;

    public string SelectedJourneyId { get; private set; }
    public string SelectedJourneyName { get; private set; }

    private const string NEW_OPTION = "New...";

    private CollectionReference DaysCol => _db.Collection("gameDays");

    // ----------------------------
    // Public refresh (called after login)
    // ----------------------------
    public void RefreshHome()
    {
        if (!_isInitialized)
        {
            Debug.LogWarning("[HomeAdminUI] RefreshHome called before initialization.");
            return;
        }

        if (_isBusy)
        {
            Debug.Log("[HomeAdminUI] RefreshHome ignored because an operation is already running.");
            return;
        }

        _ = ReloadOpenJourneysAsync();
    }

    private async void Start()
    {
        await InitializeAsync();
    }

    private void OnDestroy()
    {
        UnwireButtons();
    }

    private async Task InitializeAsync()
    {
        SetMessage("");
        SetBusy(false);
        ClearSelectionState();

        if (!ValidateReferences())
        {
            SetMessage("HomeAdminUI references are missing.");
            return;
        }

        try
        {
            var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dep != DependencyStatus.Available)
            {
                SetMessage("Firebase dependencies error.");
                Debug.LogError("[HomeAdminUI] Firebase deps error: " + dep);
                return;
            }

            _db = FirebaseFirestore.DefaultInstance;

            WireButtons();
            _isInitialized = true;

            await ReloadOpenJourneysAsync();

            Debug.Log("[HomeAdminUI] Initialized successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[HomeAdminUI] Initialization failed: " + ex);
            SetMessage("Initialization failed.");
        }
    }

    private bool ValidateReferences()
    {
        bool ok = true;

        if (openJourneyDropdown == null)
        {
            Debug.LogError("[HomeAdminUI] openJourneyDropdown is not assigned.");
            ok = false;
        }

        if (newJourneyNameInput == null)
        {
            Debug.LogWarning("[HomeAdminUI] newJourneyNameInput is not assigned.");
        }

        if (homeMessageText == null)
        {
            Debug.LogWarning("[HomeAdminUI] homeMessageText is not assigned.");
        }

        if (createJourneyButton == null)
        {
            Debug.LogWarning("[HomeAdminUI] createJourneyButton is not assigned.");
        }

        if (deleteJourneyButton == null)
        {
            Debug.LogWarning("[HomeAdminUI] deleteJourneyButton is not assigned.");
        }

        if (modifyJourneyButton == null)
        {
            Debug.LogWarning("[HomeAdminUI] modifyJourneyButton is not assigned.");
        }

        if (scoresButton == null)
        {
            Debug.LogWarning("[HomeAdminUI] scoresButton is not assigned.");
        }

        if (ui == null)
        {
            Debug.LogWarning("[HomeAdminUI] ui is not assigned.");
        }

        return ok;
    }

    private void WireButtons()
    {
        if (_listenersWired) return;

        if (openJourneyDropdown != null)
        {
            openJourneyDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            openJourneyDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        }

        if (createJourneyButton != null)
        {
            createJourneyButton.onClick.RemoveListener(OnCreateJourneyClicked);
            createJourneyButton.onClick.AddListener(OnCreateJourneyClicked);
        }

        if (deleteJourneyButton != null)
        {
            deleteJourneyButton.onClick.RemoveListener(OnDeleteJourneyClicked);
            deleteJourneyButton.onClick.AddListener(OnDeleteJourneyClicked);
        }

        if (modifyJourneyButton != null)
        {
            modifyJourneyButton.onClick.RemoveListener(OnModifyJourneyClicked);
            modifyJourneyButton.onClick.AddListener(OnModifyJourneyClicked);
        }

        if (scoresButton != null)
        {
            scoresButton.onClick.RemoveListener(OnScoresClicked);
            scoresButton.onClick.AddListener(OnScoresClicked);
        }

        _listenersWired = true;
    }

    private void UnwireButtons()
    {
        if (!_listenersWired) return;

        if (openJourneyDropdown != null)
            openJourneyDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);

        if (createJourneyButton != null)
            createJourneyButton.onClick.RemoveListener(OnCreateJourneyClicked);

        if (deleteJourneyButton != null)
            deleteJourneyButton.onClick.RemoveListener(OnDeleteJourneyClicked);

        if (modifyJourneyButton != null)
            modifyJourneyButton.onClick.RemoveListener(OnModifyJourneyClicked);

        if (scoresButton != null)
            scoresButton.onClick.RemoveListener(OnScoresClicked);

        _listenersWired = false;
    }

    private void OnDropdownValueChanged(int _)
    {
        OnDropdownChanged();
    }

    private void OnCreateJourneyClicked()
    {
        if (_isBusy) return;
        _ = OnCreateJourneyAsync();
    }

    private void OnDeleteJourneyClicked()
    {
        if (_isBusy) return;
        _ = OnDeleteJourneyAsync();
    }

    private void OnModifyJourneyClicked()
    {
        if (_isBusy) return;

        Debug.Log($"[HOME] Modify clicked. ui={(ui != null ? "OK" : "NULL")} | selected={SelectedJourneyId}");

        if (string.IsNullOrWhiteSpace(SelectedJourneyId))
        {
            SetMessage("Please select a journey.");
            return;
        }

        if (ui != null)
            ui.ShowMatches();
        else
            Debug.LogWarning("[HomeAdminUI] ui is null. Cannot navigate to Matches.");
    }

    private void OnScoresClicked()
    {
        if (_isBusy) return;

        if (string.IsNullOrWhiteSpace(SelectedJourneyId))
        {
            SetMessage("Please select a journey.");
            return;
        }

        if (ui != null)
            ui.ShowScore();
        else
            Debug.LogWarning("[HomeAdminUI] ui is null. Cannot navigate to Score.");
    }

    private async Task ReloadOpenJourneysAsync()
    {
        if (_db == null)
        {
            Debug.LogWarning("[HomeAdminUI] Firestore not ready yet.");
            SetMessage("Firestore not ready.");
            return;
        }

        SetBusy(true);

        try
        {
            SetMessage("Loading open journeys...");

            var snap = await DaysCol
                .WhereEqualTo("status", "open")
                .GetSnapshotAsync();

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

            if (openJourneyDropdown != null)
            {
                openJourneyDropdown.ClearOptions();
                openJourneyDropdown.AddOptions(options);
                openJourneyDropdown.value = 0;
                openJourneyDropdown.RefreshShownValue();
            }

            if (newJourneyNameInput != null)
            {
                newJourneyNameInput.gameObject.SetActive(true);
                newJourneyNameInput.text = "";
            }

            ClearSelectionState();
            OnDropdownChanged();

            SetMessage(snap.Count == 0 ? "No open journeys." : "");
            Debug.Log($"[HomeAdminUI] Reloaded {snap.Count} open journey(s).");
        }
        catch (Exception ex)
        {
            Debug.LogError("[HomeAdminUI] Failed to load journeys: " + ex);
            SetMessage("Failed to load journeys.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnDropdownChanged()
    {
        if (openJourneyDropdown == null || openJourneyDropdown.options == null || openJourneyDropdown.options.Count == 0)
        {
            ClearSelectionState();
            UpdateButtonsState();
            return;
        }

        bool isNew = openJourneyDropdown.value <= 0;

        if (newJourneyNameInput != null)
            newJourneyNameInput.gameObject.SetActive(isNew);

        if (!isNew)
        {
            string label = openJourneyDropdown.options[openJourneyDropdown.value].text;

            var idMatch = Regex.Match(label, @"\(([^)]+)\)\s*$");
            SelectedJourneyId = idMatch.Success ? idMatch.Groups[1].Value.Trim() : null;

            SelectedJourneyName = Regex.Replace(label, @"\s*\([^)]+\)\s*$", "").Trim();

            AdminState.SelectedJourneyId = SelectedJourneyId;
            AdminState.SelectedJourneyName = SelectedJourneyName;
        }
        else
        {
            ClearSelectionState();
        }

        UpdateButtonsState();
    }

    private async Task OnCreateJourneyAsync()
    {
        if (_db == null)
        {
            SetMessage("Firestore not ready.");
            return;
        }

        SetBusy(true);

        try
        {
            SetMessage("");

            string name = newJourneyNameInput != null ? newJourneyNameInput.text.Trim() : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                SetMessage("Please enter a Journey Name.");
                return;
            }

            string journeyId = MakeRobustJourneyId(name);

            var existingByName = await DaysCol
                .WhereEqualTo("status", "open")
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

            Debug.Log($"[HomeAdminUI] Journey created: {name} ({journeyId})");
            SetMessage($"Journey created: {name}");

            await ReloadOpenJourneysAsync();

            SelectJourneyInDropdown(journeyId);
            OnDropdownChanged();

            if (ui != null)
                ui.ShowMatches();
        }
        catch (Exception ex)
        {
            Debug.LogError("[HomeAdminUI] Create journey failed: " + ex);
            SetMessage("Failed to create journey.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OnDeleteJourneyAsync()
    {
        if (_db == null)
        {
            SetMessage("Firestore not ready.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedJourneyId))
        {
            SetMessage("Please select a journey to delete.");
            return;
        }

        SetBusy(true);

        try
        {
            string id = SelectedJourneyId;
            SetMessage("Deleting journey...");

            var journeyRef = DaysCol.Document(id);
            var matchesCol = journeyRef.Collection("matches");
            var matchesSnap = await matchesCol.GetSnapshotAsync();

            WriteBatch batch = _db.StartBatch();
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

            batch.Delete(journeyRef);
            await batch.CommitAsync();

            Debug.Log($"[HomeAdminUI] Journey deleted: {id}");
            SetMessage($"Journey deleted: {id}");

            ClearSelectionState();
            await ReloadOpenJourneysAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError("[HomeAdminUI] Delete journey failed: " + ex);
            SetMessage("Failed to delete journey.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectJourneyInDropdown(string journeyId)
    {
        if (openJourneyDropdown == null || openJourneyDropdown.options == null)
            return;

        for (int i = 0; i < openJourneyDropdown.options.Count; i++)
        {
            string label = openJourneyDropdown.options[i].text;
            if (label.Contains($"({journeyId})"))
            {
                openJourneyDropdown.value = i;
                openJourneyDropdown.RefreshShownValue();
                return;
            }
        }
    }

    private void UpdateButtonsState()
    {
        bool isNew = true;

        if (openJourneyDropdown != null && openJourneyDropdown.options != null && openJourneyDropdown.options.Count > 0)
            isNew = openJourneyDropdown.value <= 0;

        bool hasSelection = !isNew && !string.IsNullOrWhiteSpace(SelectedJourneyId);
        bool interactable = !_isBusy && _isInitialized;

        if (createJourneyButton != null)
            createJourneyButton.interactable = interactable && isNew;

        if (deleteJourneyButton != null)
            deleteJourneyButton.interactable = interactable && hasSelection;

        if (modifyJourneyButton != null)
            modifyJourneyButton.interactable = interactable && hasSelection;

        if (scoresButton != null)
            scoresButton.interactable = interactable && hasSelection;

        if (openJourneyDropdown != null)
            openJourneyDropdown.interactable = interactable;

        if (newJourneyNameInput != null)
            newJourneyNameInput.interactable = interactable && isNew;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        UpdateButtonsState();
    }

    private void ClearSelectionState()
    {
        SelectedJourneyId = null;
        SelectedJourneyName = null;

        AdminState.SelectedJourneyId = null;
        AdminState.SelectedJourneyName = null;
    }

    private void SetMessage(string msg)
    {
        if (homeMessageText != null)
            homeMessageText.text = msg;
    }

    // --- Robust ID (slug + short hash) ---
    private static string MakeRobustJourneyId(string journeyName)
    {
        string normalizedInput = journeyName.Trim();
        string slug = normalizedInput.ToLowerInvariant();
        slug = RemoveDiacritics(slug);
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "_");
        slug = Regex.Replace(slug, @"_+", "_").Trim('_');

        if (string.IsNullOrWhiteSpace(slug))
            slug = "journey";

        string hash = ShortHashHex(normalizedInput.ToLowerInvariant(), 6);
        return $"{slug}_{hash}";
    }

    private static string ShortHashHex(string input, int hexLen)
    {
        using var sha1 = SHA1.Create();
        byte[] bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));

        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length && sb.Length < hexLen; i++)
            sb.Append(bytes[i].ToString("x2"));

        string full = sb.ToString();
        return full.Length >= hexLen ? full.Substring(0, hexLen) : full;
    }

    private static string RemoveDiacritics(string text)
    {
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (char c in normalized)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}