using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pause menu and hold-to-restart for any gameplay scene.
// Spawns itself whenever a scene other than the main menu loads,
// so no scene needs to be edited to get it.
public class PauseMenu : MonoBehaviour
{
    // How long P must be held before the game pauses
    public const float HoldSeconds = 10f;

    // How long R must be held before the race restarts
    public const float RestartHoldSeconds = 1f;

    // Scene that Restart reloads
    public const string GameSceneName = "CombinedScene";

    public const string MainMenuSceneName = "MainMenu";

    private const string PagePause = "Pause";
    private const string PageControls = "Controls";
    private const string PageLeaderboard = "Leaderboard";

    private const int LeaderboardRows = 10;
    private const float IndicatorShowDelay = 0.25f;
    private const float FillHeight = 0.12f;

    private MenuPages pages;
    private Canvas menuCanvas;

    private GameObject holdIndicator;
    private RectTransform holdFill;
    private TMP_Text holdLabel;

    private TMP_Text leaderboardText;
    private LapDataSO fallbackLapData;

    // Tracks one hold-to-activate key
    private class HoldState
    {
        public float timer;
        public bool consumed;
    }

    private readonly HoldState pauseHold = new HoldState();
    private readonly HoldState restartHold = new HoldState();

    private bool isPaused;

    private bool cursorWasVisible;
    private CursorLockMode cursorWasLockState;


    // ---------- Auto spawn ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;

        if (scene.name == MainMenuSceneName)
            return;

        // Internal dev scene: no pause or restart keys there
        if (scene.name == PhotoModeScene.SceneName)
            return;

        if (FindAnyObjectByType<PauseMenu>() != null)
            return;

        new GameObject("Pause Menu").AddComponent<PauseMenu>();
    }


    // ---------- Unity ----------

    void Awake()
    {
        BuildHoldIndicator();
        BuildMenu();
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        UpdateHolds();

        if (isPaused)
            UpdatePaused();
    }

    void OnDestroy()
    {
        // Never leave the game frozen or silent
        if (isPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        if (fallbackLapData != null)
            Destroy(fallbackLapData);
    }


    // ---------- Hold keys ----------

    void UpdateHolds()
    {
        bool pauseDone = Tick(
            pauseHold,
            Keyboard.current.pKey.isPressed,
            HoldSeconds
        );

        bool restartDone = Tick(
            restartHold,
            Keyboard.current.rKey.isPressed,
            RestartHoldSeconds
        );

        // Restart works both in-game and from the pause menu
        if (restartDone)
        {
            RestartGame();
            return;
        }

        // Holding P while already paused does nothing
        if (pauseDone && !isPaused)
            Pause();

        RefreshHoldIndicator();
    }

    // Counts up while the key is held. Returns true once, when the hold
    // completes. The key must be released before it can trigger again.
    bool Tick(HoldState state, bool pressed, float seconds)
    {
        if (!pressed)
        {
            state.timer = 0f;
            state.consumed = false;
            return false;
        }

        if (state.consumed)
            return false;

        state.timer += Time.unscaledDeltaTime;

        if (state.timer < seconds)
            return false;

        state.consumed = true;
        state.timer = 0f;
        return true;
    }

    void RefreshHoldIndicator()
    {
        HoldState active = null;
        string action = "";
        float seconds = 0f;

        // Skip the flash on a quick tap
        if (restartHold.timer >= IndicatorShowDelay)
        {
            active = restartHold;
            action = "Hold R to restart";
            seconds = RestartHoldSeconds;
        }
        else if (!isPaused && pauseHold.timer >= IndicatorShowDelay)
        {
            active = pauseHold;
            action = "Hold P to pause";
            seconds = HoldSeconds;
        }

        if (active == null)
        {
            holdIndicator.SetActive(false);
            return;
        }

        float progress = Mathf.Clamp01(active.timer / seconds);
        float secondsLeft = Mathf.Ceil(seconds - active.timer);

        holdIndicator.SetActive(true);
        holdFill.anchorMax = new Vector2(progress, FillHeight);
        holdLabel.text = action + "  -  " + secondsLeft.ToString("0") + "s";
    }


    // ---------- While paused ----------

    void UpdatePaused()
    {
        pages.RestoreSelectionIfLost();

        if (!Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        // Esc backs out of a sub page, or resumes from the main page
        if (pages.IsAtRoot)
            Resume();
        else
            pages.ShowRoot();
    }

    void Pause()
    {
        isPaused = true;

        Time.timeScale = 0f;
        AudioListener.pause = true;

        cursorWasVisible = Cursor.visible;
        cursorWasLockState = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        MenuUI.EnsureEventSystem();

        menuCanvas.gameObject.SetActive(true);
        pages.ShowRoot();
    }

    void Resume()
    {
        isPaused = false;

        menuCanvas.gameObject.SetActive(false);
        pages.HideAll();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = cursorWasVisible;
        Cursor.lockState = cursorWasLockState;
    }

    void RestartGame()
    {
        isPaused = false;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        SceneManager.LoadScene(GameSceneName);
    }

    void ExitToMainMenu()
    {
        isPaused = false;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        SceneManager.LoadScene(MainMenuSceneName);
    }


    // ---------- Building the UI ----------

    void BuildHoldIndicator()
    {
        Canvas canvas = MenuUI.CreateCanvas("Hold Indicator Canvas", 50, transform);

        GameObject root = new GameObject("Hold Indicator", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 60f);
        rootRect.sizeDelta = new Vector2(520f, 64f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.6f);
        background.raycastTarget = false;

        // Progress fill grows along the bottom edge
        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(root.transform, false);

        holdFill = fill.GetComponent<RectTransform>();
        holdFill.anchorMin = new Vector2(0f, 0f);
        holdFill.anchorMax = new Vector2(0f, FillHeight);
        holdFill.offsetMin = Vector2.zero;
        holdFill.offsetMax = Vector2.zero;

        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = MenuUI.ButtonHighlight;
        fillImage.raycastTarget = false;

        holdLabel = MenuUI.CreateLabel(
            root.transform,
            "",
            28f,
            TextAlignmentOptions.Center,
            MenuUI.TextColor
        );

        RectTransform labelRect = holdLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        holdIndicator = root;
        holdIndicator.SetActive(false);
    }

    void BuildMenu()
    {
        menuCanvas = MenuUI.CreateCanvas("Pause Menu Canvas", 100, transform);

        MenuUI.CreateFullScreenPanel(
            menuCanvas.transform,
            "Dim",
            MenuUI.PauseDimColor
        );

        pages = new MenuPages(PagePause);

        BuildPausePage(menuCanvas.transform);
        BuildControlsPage(menuCanvas.transform);
        BuildLeaderboardPage(menuCanvas.transform);

        menuCanvas.gameObject.SetActive(false);
    }

    void BuildPausePage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Pause Page");

        MenuUI.CreateTitle(page, "Paused", 72f);
        MenuUI.CreateSpacer(page, 32f);

        Button resume = MenuUI.CreateButton(page, "Resume", Resume);

        MenuUI.CreateButton(page, "Restart", RestartGame);
        MenuUI.CreateButton(page, "Controls", () => pages.Show(PageControls));
        MenuUI.CreateButton(page, "Local Leaderboard", ShowLeaderboard);
        MenuUI.CreateButton(page, "Exit to Main Menu", ExitToMainMenu);

        pages.Add(PagePause, page.gameObject, resume);
    }

    void BuildControlsPage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Controls Page");

        Button back = MenuUI.BuildControlsPage(
            page,
            HoldSeconds,
            RestartHoldSeconds,
            () => pages.ShowRoot()
        );

        pages.Add(PageControls, page.gameObject, back);
    }

    void BuildLeaderboardPage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Leaderboard Page");

        MenuUI.CreateTitle(page, "Local Leaderboard");
        MenuUI.CreateSpacer(page, 8f);

        leaderboardText = MenuUI.CreateLabel(
            page,
            "",
            28f,
            TextAlignmentOptions.TopLeft,
            MenuUI.MutedTextColor
        );

        MenuUI.CreateSpacer(page, 8f);

        Button back = MenuUI.CreateButton(page, "Back", () => pages.ShowRoot());

        pages.Add(PageLeaderboard, page.gameObject, back);
    }


    // ---------- Leaderboard ----------

    void ShowLeaderboard()
    {
        leaderboardText.text = BuildLeaderboardText();
        pages.Show(PageLeaderboard);
    }

    string BuildLeaderboardText()
    {
        LapDataSO data = GetLapData();

        bool hasRaces = data != null && data.history.Count > 0;

        if (!hasRaces && !(data != null && data.HasBestRace))
            return "No races recorded yet.\nFinish a race to set a time!";

        StringBuilder sb = new StringBuilder();

        sb.AppendLine(
            "<b><color=#FFFFFF>Best lap</color></b><pos=38%>" +
            LapSaveSystem.FormatTime(data.bestLapTime)
        );

        sb.AppendLine(
            "<b><color=#FFFFFF>Best race</color></b><pos=38%>" +
            LapSaveSystem.FormatTime(data.bestRace.totalTime)
        );

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Fastest races</color></b>");

        // Fastest first
        List<RaceResult> sorted = new List<RaceResult>(data.history);
        sorted.Sort((a, b) => a.totalTime.CompareTo(b.totalTime));

        int rows = Mathf.Min(LeaderboardRows, sorted.Count);

        for (int i = 0; i < rows; i++)
        {
            sb.AppendLine(
                (i + 1) + ".<pos=10%>" +
                LapSaveSystem.FormatTime(sorted[i].totalTime) +
                "<pos=38%>" + sorted[i].date
            );
        }

        return sb.ToString();
    }

    // Prefer the live data from the lap counter, otherwise read the save
    LapDataSO GetLapData()
    {
        LapCounter lapCounter = FindAnyObjectByType<LapCounter>();

        if (lapCounter != null && lapCounter.lapData != null)
            return lapCounter.lapData;

        if (fallbackLapData != null)
            Destroy(fallbackLapData);

        fallbackLapData = ScriptableObject.CreateInstance<LapDataSO>();
        LapSaveSystem.Load(fallbackLapData);

        return fallbackLapData;
    }
}
