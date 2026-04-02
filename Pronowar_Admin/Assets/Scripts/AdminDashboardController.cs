using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AdminDashboardController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Dropdown openWeeksDropdown;
    [SerializeField] private Button manageWeekButton;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Create Week")]
    [SerializeField] private TMP_InputField newWeekInputField;
    [SerializeField] private Button createWeekButton;

    [Header("Panels")]
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject matchesPanel;

    private FirebaseFirestore db;
    private List<GameWeekItem> openWeeks = new List<GameWeekItem>();

    public static string SelectedWeekId;
    public static string SelectedWeekName;

    [Serializable]
    public class GameWeekItem
    {
        public string documentId;
        public string name;
        public string status;
    }

    private async void Start()
    {
        Debug.Log("[AdminDashboardController] Start");

        if (openWeeksDropdown == null)
        {
            Debug.LogError("[AdminDashboardController] openWeeksDropdown is NULL");
            return;
        }

        if (manageWeekButton == null)
        {
            Debug.LogError("[AdminDashboardController] manageWeekButton is NULL");
            return;
        }

        if (newWeekInputField == null)
        {
            Debug.LogError("[AdminDashboardController] newWeekInputField is NULL");
            return;
        }

        if (createWeekButton == null)
        {
            Debug.LogError("[AdminDashboardController] createWeekButton is NULL");
            return;
        }

        manageWeekButton.onClick.RemoveAllListeners();
        manageWeekButton.onClick.AddListener(OnManageWeekClicked);

        createWeekButton.onClick.RemoveAllListeners();
        createWeekButton.onClick.AddListener(OnCreateWeekClicked);

        SetFeedback("Initializing Firebase...");

        var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();

        if (dependencyStatus != DependencyStatus.Available)
        {
            Debug.LogError("[AdminDashboardController] Firebase dependencies error: " + dependencyStatus);
            SetFeedback("Firebase init failed: " + dependencyStatus);
            return;
        }

        Debug.Log("[AdminDashboardController] Firebase ready");

        db = FirebaseFirestore.DefaultInstance;

        if (db == null)
        {
            Debug.LogError("[AdminDashboardController] Firestore DefaultInstance is NULL");
            SetFeedback("Firestore instance is null");
            return;
        }

        Debug.Log("[AdminDashboardController] Firestore instance OK");

        await LoadOpenWeeksAsync();
    }

    public async Task LoadOpenWeeksAsync()
    {
        Debug.Log("[AdminDashboardController] LoadOpenWeeksAsync called");

        try
        {
            SetFeedback("Loading open weeks...");

            Query query = db.Collection("gameDays").WhereEqualTo("status", "open");
            QuerySnapshot snapshot = await query.GetSnapshotAsync();

            Debug.Log("[AdminDashboardController] Query done. Count = " + snapshot.Count);

            openWeeks.Clear();
            openWeeksDropdown.ClearOptions();

            List<string> options = new List<string>();

            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                if (!doc.Exists)
                    continue;

                Dictionary<string, object> data = doc.ToDictionary();

                string weekName = data.ContainsKey("name") ? data["name"]?.ToString() ?? doc.Id : doc.Id;
                string status = data.ContainsKey("status") ? data["status"]?.ToString() ?? "" : "";

                openWeeks.Add(new GameWeekItem
                {
                    documentId = doc.Id,
                    name = weekName,
                    status = status
                });

                options.Add(weekName);

                Debug.Log("[AdminDashboardController] Added week: " + weekName + " / " + doc.Id);
            }

            if (options.Count == 0)
            {
                options.Add("No open week");
                manageWeekButton.interactable = false;
                Debug.LogWarning("[AdminDashboardController] No open weeks found");
            }
            else
            {
                manageWeekButton.interactable = true;
            }

            openWeeksDropdown.ClearOptions();
            openWeeksDropdown.AddOptions(options);
            openWeeksDropdown.value = 0;
            openWeeksDropdown.RefreshShownValue();

            SetFeedback("Loaded " + openWeeks.Count + " open week(s).");
        }
        catch (Exception ex)
        {
            Debug.LogError("[AdminDashboardController] LoadOpenWeeksAsync failed: " + ex);
            SetFeedback("Failed to load open weeks");
        }
    }

    public async void OnCreateWeekClicked()
    {
        Debug.Log("[AdminDashboardController] OnCreateWeekClicked called");

        if (db == null)
        {
            Debug.LogError("[AdminDashboardController] Firestore is NULL");
            SetFeedback("Firestore not ready");
            return;
        }

        string newWeekName = newWeekInputField.text.Trim();

        if (string.IsNullOrEmpty(newWeekName))
        {
            Debug.LogWarning("[AdminDashboardController] Empty week name");
            SetFeedback("Please enter a week name");
            return;
        }

        if (newWeekName.Contains("/"))
        {
            SetFeedback("Week name cannot contain /");
            return;
        }

        try
        {
            createWeekButton.interactable = false;
            SetFeedback("Checking existing week...");

            DocumentReference docRef = db.Collection("gameDays").Document(newWeekName);
            DocumentSnapshot existingDoc = await docRef.GetSnapshotAsync();

            if (existingDoc.Exists)
            {
                Debug.LogWarning("[AdminDashboardController] Duplicate week name: " + newWeekName);
                SetFeedback("This week already exists");
                return;
            }

            SetFeedback("Creating week: " + newWeekName + "...");

            Dictionary<string, object> newWeekData = new Dictionary<string, object>
            {
                { "name", newWeekName },
                { "status", "open" },
                { "createdAt", Timestamp.GetCurrentTimestamp() },
                { "updatedAt", Timestamp.GetCurrentTimestamp() }
            };

            await docRef.SetAsync(newWeekData);

            Debug.Log("[AdminDashboardController] Week created with id: " + newWeekName);

            newWeekInputField.text = string.Empty;
            SetFeedback("Created: " + newWeekName);

            await LoadOpenWeeksAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError("[AdminDashboardController] Create week failed: " + ex);
            SetFeedback("Failed to create week");
        }
        finally
        {
            createWeekButton.interactable = true;
        }
    }

    public void OnManageWeekClicked()
    {
        Debug.Log("[AdminDashboardController] OnManageWeekClicked called");

        if (openWeeks.Count == 0)
        {
            SetFeedback("No open week selected");
            return;
        }

        int index = openWeeksDropdown.value;

        if (index < 0 || index >= openWeeks.Count)
        {
            SetFeedback("Invalid selection");
            return;
        }

        GameWeekItem selectedWeek = openWeeks[index];

        SelectedWeekId = selectedWeek.documentId;
        SelectedWeekName = selectedWeek.name;

        Debug.Log("[AdminDashboardController] Selected week = " + SelectedWeekName + " / " + SelectedWeekId);

        SetFeedback("Managing: " + SelectedWeekName);

        if (homePanel != null)
            homePanel.SetActive(false);

        if (matchesPanel != null)
            matchesPanel.SetActive(true);
    }

    private void SetFeedback(string message)
    {
        Debug.Log("[AdminDashboardController] " + message);

        if (feedbackText != null)
            feedbackText.text = message;
    }
}