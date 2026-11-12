
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GameFlowController : MonoBehaviour
{
    [Header("Controls")]
    public float holdDuration = 1f; // seconds

    private float restartTimer = 0f;
    private float pauseTimer = 0f;

    private bool isPaused = false;
    private bool restartTriggered = false;
    private bool pauseTriggered = false;

    void Awake()
    {
        Time.timeScale = 1f;
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        // Unscaled time works even when paused
        float dt = Time.unscaledDeltaTime;

        // HOLD R - RESTART
        if (Keyboard.current.rKey.isPressed)
        {
            if (!restartTriggered)
            {
                restartTimer += dt;

                if (restartTimer >= holdDuration)
                {
                    restartTriggered = true;
                    RestartGame();
                    return;
                }
            }
        }
        else
        {
            restartTimer = 0f;
            restartTriggered = false;
        }

        // HOLD P - PAUSE / RESUME
        if (Keyboard.current.pKey.isPressed)
        {
            if (!pauseTriggered)
            {
                pauseTimer += dt;

                if (pauseTimer >= holdDuration)
                {
                    pauseTriggered = true;
                    TogglePause();
                }
            }
        }
        else
        {
            pauseTimer = 0f;
            pauseTriggered = false;
        }
    }

    void TogglePause()
    {
        isPaused = !isPaused;

        Time.timeScale = isPaused ? 0f : 1f;

        Debug.Log(isPaused ? "Game Paused" : "Game Resumed");
    }

    void RestartGame()
    {
        Scene currentScene = SceneManager.GetActiveScene();

        if (currentScene.buildIndex < 0)
        {
            Debug.LogError(
                "Add the current scene to Build Profiles!"
            );
            return;
        }

        isPaused = false;
        Time.timeScale = 1f;

        SceneManager.LoadScene(currentScene.buildIndex);
    }

    void OnGUI()
    {
        if (!isPaused)
            return;

        float width = 320f;
        float height = 130f;

        Rect panel = new Rect(
            (Screen.width - width) / 2f,
            (Screen.height - height) / 2f,
            width,
            height
        );

        GUI.Box(panel, "");

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 28;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;

        GUI.Label(
            new Rect(panel.x, panel.y + 15, width, 40),
            "PAUSED",
            titleStyle
        );

        GUI.Label(
            new Rect(panel.x, panel.y + 65, width, 25),
            "Hold P to Resume",
            CenteredStyle()
        );

        GUI.Label(
            new Rect(panel.x, panel.y + 90, width, 25),
            "Hold R to Restart",
            CenteredStyle()
        );
    }

    GUIStyle CenteredStyle()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        return style;
    }

    void OnDestroy()
    {
        // Prevent time from remaining frozen
        if (isPaused)
            Time.timeScale = 1f;
    }
}
