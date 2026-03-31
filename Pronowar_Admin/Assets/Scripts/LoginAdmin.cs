using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class LoginAdmin : MonoBehaviour
{
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;

    public AdminUIController uiController;
    public HomeAdminUI homeUI;

    private FirebaseAuth auth;
    private bool firebaseReady = false;
    private bool firebaseInitStarted = false;

    void Start()
    {
        Debug.Log("=== TEST FIREBASE INIT START ===");

        if (uiController != null)
        {
            uiController.ShowLogin();
            uiController.ShowLoginMessage("Initializing Firebase...");
        }

        firebaseInitStarted = true;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            Debug.Log("Dependency result: " + task.Result);

            if (task.Result == DependencyStatus.Available)
            {
                auth = FirebaseAuth.DefaultInstance;
                firebaseReady = true;

                auth.SignOut();

                Debug.Log("Firebase Admin Ready");

                if (uiController != null)
                {
                    uiController.ShowLogin();
                    uiController.ShowLoginMessage("");
                }
            }
            else
            {
                firebaseReady = false;
                Debug.LogError("Firebase not ready: " + task.Result);

                if (uiController != null)
                    uiController.ShowLoginMessage("Firebase initialization failed.");
            }
        });
    }

    private async Task<bool> EnsureFirebaseReady()
    {
        if (firebaseReady && auth != null)
            return true;

        if (firebaseInitStarted)
        {
            int timeout = 0;
            while (!firebaseReady && auth == null && timeout < 200)
            {
                await Task.Delay(50);
                timeout++;
            }

            return firebaseReady && auth != null;
        }

        firebaseInitStarted = true;

        try
        {
            if (uiController != null)
                uiController.ShowLoginMessage("Initializing Firebase...");

            var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();

            if (dependencyStatus != DependencyStatus.Available)
            {
                Debug.LogError("Firebase not ready: " + dependencyStatus);

                if (uiController != null)
                    uiController.ShowLoginMessage("Firebase initialization failed.");

                return false;
            }

            auth = FirebaseAuth.DefaultInstance;
            firebaseReady = true;

            Debug.Log("Firebase Admin Ready");

            if (uiController != null)
                uiController.ShowLoginMessage("");

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Firebase init exception: " + ex);

            if (uiController != null)
                uiController.ShowLoginMessage("Firebase initialization failed.");

            return false;
        }
    }

    public async void Login()
    {
        string email = emailInput != null ? emailInput.text.Trim() : "";
        string password = passwordInput != null ? passwordInput.text : "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            if (uiController != null)
                uiController.ShowLoginMessage("Please enter email and password.");
            return;
        }

        bool ok = await EnsureFirebaseReady();
        if (!ok || auth == null)
            return;

        try
        {
            if (uiController != null)
                uiController.ShowLoginMessage("Signing in...");

            Debug.Log($"[DEBUG] ApiKey={FirebaseApp.DefaultInstance.Options.ApiKey} | AppId={FirebaseApp.DefaultInstance.Options.AppId}");

            var result = await auth.SignInWithEmailAndPasswordAsync(email, password);

            if (result != null && result.User != null)
            {
                Debug.Log("Admin Login Success UID: " + result.User.UserId);

                if (uiController != null)
                    uiController.ShowLoginMessage("");

                if (uiController != null)
                    uiController.ShowHome();

                var h = homeUI != null ? homeUI : UnityEngine.Object.FindFirstObjectByType<HomeAdminUI>();
                if (h != null)
                    h.RefreshHome();
            }
            else
            {
                if (uiController != null)
                    uiController.ShowLoginMessage("Login failed.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Login Failed: " + ex);

            string errorMsg = "Login failed.";
            string lower = ex.ToString().ToLower();

            if (lower.Contains("badly formatted"))
                errorMsg = "Invalid email format.";
            else if (lower.Contains("no user record") || lower.Contains("user-not-found"))
                errorMsg = "User not found.";
            else if (lower.Contains("wrong-password") || lower.Contains("password is invalid"))
                errorMsg = "Wrong password.";
            else if (lower.Contains("network"))
                errorMsg = "Network error.";
            else if (lower.Contains("internal error"))
                errorMsg = "Firebase internal error.";

            if (uiController != null)
                uiController.ShowLoginMessage(errorMsg);
        }
    }

    public void Logout()
    {
        if (auth == null)
            return;

        auth.SignOut();
        Debug.Log("Admin Logout Success");

        if (uiController != null)
        {
            uiController.ShowLogin();
            uiController.ShowLoginMessage("");
        }
    }
}