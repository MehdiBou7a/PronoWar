using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using TMPro;

public class LoginAdmin : MonoBehaviour
{
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;

    public AdminUIController uiController; // drag AdminManager here
    public HomeAdminUI homeUI;             // (optionnel) drag HomePanel/HomeAdminUI ici

    private FirebaseAuth auth;

    void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                auth = FirebaseAuth.DefaultInstance;
                Debug.Log("Firebase Admin Ready");
            }
            else
            {
                Debug.LogError("Firebase not ready: " + task.Result);
            }
        });
    }

    public void Login()
    {
        if (auth == null)
        {
            Debug.LogWarning("Auth not ready yet.");
            return;
        }

        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        auth.SignInWithEmailAndPasswordAsync(email, password)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsFaulted && !task.IsCanceled)
                {
                    Debug.Log("Admin Login Success UID: " + task.Result.User.UserId);

                    if (uiController != null) uiController.ShowLoginMessage(""); // clear msg
                    if (uiController != null) uiController.ShowHome();

                    // Refresh Home
                    var h = homeUI != null ? homeUI : Object.FindFirstObjectByType<HomeAdminUI>();
                    if (h != null) h.RefreshHome();
                }
                else
                {
                    Debug.LogError("Login Failed: " + task.Exception);
                    if (uiController != null) uiController.ShowLoginMessage("Wrong email or password.");
                }
            });
    }
}