using TMPro;
using UnityEngine;

public class PasswordToggleUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField passwordInput;
    private bool isVisible = false;

    public void TogglePasswordVisibility()
    {
        if (passwordInput == null)
            return;

        isVisible = !isVisible;

        passwordInput.contentType = isVisible
            ? TMP_InputField.ContentType.Standard
            : TMP_InputField.ContentType.Password;

        passwordInput.ForceLabelUpdate();
    }
}