using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// ponytail: the HUD is drawn with IMGUI (OnGUI), which needs no UI package, Canvas or fonts.
// It is fine for a prototype; a styled HUD would move to UI Toolkit or uGUI.
public class GameUI : MonoBehaviour
{
    public static GameUI Instance { get; private set; }

    public PlayerController player;

    const string ControlsHelp =
        "Mouse / right stick look   WASD / left stick move   Shift / Square run   E / Cross interact\n" +
        "Hold right mouse / L2 to aim over the shoulder   Left mouse / J / R2 shoots, even on the move";

    float fadeAlpha = 1f;
    float damageFlash;
    string message;
    float messageHideTime;
    string prompt;
    string endTitle;
    Color endTint = Color.black;
    GUIStyle centeredStyle;
    GUIStyle hudStyle;

    // OnEnable rather than Awake: it also runs after a script reload in Play mode, which clears statics.
    void OnEnable() => Instance = this;

    IEnumerator Start()
    {
        ShowMessage("LOADING DOCK", 2.5f);
        yield return Fade(0f, 1.2f);
    }

    void Update()
    {
        damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.deltaTime * 2f);
        if (endTitle != null && (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton9))) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ShowMessage(string text, float seconds = 2.5f)
    {
        message = text;
        messageHideTime = Time.time + seconds;
    }

    public void SetPrompt(string text) => prompt = text;

    public void FlashDamage() => damageFlash = 0.5f;

    public void GameOver() => StartCoroutine(ShowEndScreen("GAME OVER", new Color(0.35f, 0f, 0f)));

    public void DemoComplete() => StartCoroutine(ShowEndScreen("YOU ESCAPED\nDEMO COMPLETE", Color.black));

    IEnumerator ShowEndScreen(string title, Color tint)
    {
        yield return new WaitForSeconds(1.2f);
        endTitle = title;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        endTint = tint;
        yield return Fade(0.85f, 1f);
    }

    public IEnumerator Fade(float targetAlpha, float duration)
    {
        float startAlpha = fadeAlpha;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            fadeAlpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }
        fadeAlpha = targetAlpha;
    }

    void OnGUI()
    {
        centeredStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, wordWrap = true };
        hudStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        float width = Screen.width;
        float height = Screen.height;

        DrawOverlay(new Color(0.6f, 0f, 0f, damageFlash));
        DrawOverlay(new Color(endTint.r, endTint.g, endTint.b, fadeAlpha));

        if (endTitle != null)
        {
            centeredStyle.fontSize = 48;
            GUI.Label(new Rect(0f, height * 0.3f, width, height * 0.3f), endTitle, centeredStyle);
            centeredStyle.fontSize = 22;
            GUI.Label(new Rect(0f, height * 0.6f, width, 40f), "Press R / Options to restart", centeredStyle);
            return;
        }

        Color statusColor = player.Status switch
        {
            "FINE" => new Color(0.3f, 1f, 0.4f),
            "CAUTION" => new Color(1f, 0.75f, 0.2f),
            _ => new Color(1f, 0.25f, 0.2f),
        };
        GUI.color = statusColor;
        GUI.Label(new Rect(24f, height - 70f, 300f, 30f), "STATUS: " + player.Status, hudStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(24f, height - 42f, 300f, 30f), "HANDGUN  " + player.ammo, hudStyle);

        if (player.IsAiming) DrawCrosshair(width / 2f, height / 2f);
        if (prompt != null) GUI.Label(new Rect(0f, height - 130f, width, 30f), prompt, centeredStyle);
        if (message != null && Time.time < messageHideTime) GUI.Label(new Rect(0f, height * 0.72f, width, 60f), message, centeredStyle);
        if (Time.timeSinceLevelLoad < 12f)
        {
            centeredStyle.fontSize = 15;
            GUI.Label(new Rect(0f, 10f, width, 50f), ControlsHelp, centeredStyle);
            centeredStyle.fontSize = 22;
        }
    }

    static void DrawCrosshair(float x, float y)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.8f);
        const float gap = 4f, length = 8f, thickness = 2f;
        GUI.DrawTexture(new Rect(x - gap - length, y - thickness / 2f, length, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x + gap, y - thickness / 2f, length, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness / 2f, y - gap - length, thickness, length), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness / 2f, y + gap, thickness, length), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static void DrawOverlay(Color color)
    {
        if (color.a <= 0f) return;
        GUI.color = color;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
