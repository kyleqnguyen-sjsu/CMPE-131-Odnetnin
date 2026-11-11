using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Race HUD (lap, time, speed) in the same style as the menus.
//
// Spawns itself in any scene except the main menu. It reuses the
// existing LapCounter, RaceTimer and SpeedHUD scripts: it hands each
// one of its own labels and hides the old text, so the scenes and
// those scripts do not need to change.
public class RaceHud : MonoBehaviour
{
    private const int SortingOrder = 10;
    private const float EdgeMargin = 48f;
    private const float AccentWidth = 6f;


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

        if (scene.name == PauseMenu.MainMenuSceneName)
            return;

        if (FindAnyObjectByType<RaceHud>() != null)
            return;

        new GameObject("Race HUD").AddComponent<RaceHud>();
    }


    // ---------- Setup ----------

    // Runs before the other scripts' Start, so they write into our
    // labels from their very first update.
    void Awake()
    {
        LapCounter lapCounter = FindAnyObjectByType<LapCounter>();
        RaceTimer raceTimer = FindAnyObjectByType<RaceTimer>();
        SpeedHUD speedHud = FindAnyObjectByType<SpeedHUD>();

        // Not a race scene
        if (lapCounter == null && raceTimer == null && speedHud == null)
        {
            Destroy(gameObject);
            return;
        }

        Canvas canvas = MenuUI.CreateCanvas("Race HUD Canvas", SortingOrder, transform);

        if (lapCounter != null || raceTimer != null)
            BuildRacePanel(canvas.transform, lapCounter, raceTimer);

        if (speedHud != null)
            BuildSpeedPanel(canvas.transform, speedHud);
    }

    // Top left: lap and race time
    void BuildRacePanel(Transform parent, LapCounter lapCounter, RaceTimer raceTimer)
    {
        RectTransform panel = CreatePanel(
            parent,
            "Race Panel",
            new Vector2(0f, 1f),
            new Vector2(EdgeMargin, -EdgeMargin),
            340f
        );

        if (lapCounter != null)
        {
            TMP_Text label = MenuUI.CreateLabel(
                panel,
                "Lap 1 / " + lapCounter.totalLaps,
                46f,
                TextAlignmentOptions.Left,
                MenuUI.TextColor,
                FontStyles.Bold
            );

            Replace(lapCounter.lapText, label);
            lapCounter.lapText = label;
        }

        if (raceTimer != null)
        {
            TMP_Text label = MenuUI.CreateLabel(
                panel,
                "00:00:000",
                38f,
                TextAlignmentOptions.Left,
                MenuUI.MutedTextColor
            );

            Replace(raceTimer.timerText, label);
            raceTimer.timerText = label;
        }
    }

    // Bottom right: speed
    void BuildSpeedPanel(Transform parent, SpeedHUD speedHud)
    {
        RectTransform panel = CreatePanel(
            parent,
            "Speed Panel",
            new Vector2(1f, 0f),
            new Vector2(-EdgeMargin, EdgeMargin),
            280f
        );

        TMP_Text label = MenuUI.CreateLabel(
            panel,
            "Speed: 0.0",
            40f,
            TextAlignmentOptions.Left,
            MenuUI.TextColor,
            FontStyles.Bold
        );

        Replace(speedHud.speedText, label);
        speedHud.speedText = label;
    }

    // Copy the old text across, then stop the old label from drawing
    void Replace(TMP_Text oldLabel, TMP_Text newLabel)
    {
        if (oldLabel == null)
            return;

        newLabel.text = oldLabel.text;
        oldLabel.enabled = false;
    }


    // ---------- Panel ----------

    // A dark box with a blue accent bar on the left edge, pinned to a
    // screen corner. Grows to fit what is inside it.
    RectTransform CreatePanel(
        Transform parent,
        string name,
        Vector2 corner,
        Vector2 offset,
        float minWidth)
    {
        GameObject go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(LayoutElement)
        );

        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = corner;
        rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;

        Image background = go.GetComponent<Image>();
        background.color = MenuUI.HudPanelColor;
        background.raycastTarget = false;

        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 28, 18, 18);
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        go.GetComponent<LayoutElement>().minWidth = minWidth;

        CreateAccentBar(rect);

        return rect;
    }

    void CreateAccentBar(RectTransform panel)
    {
        GameObject go = new GameObject(
            "Accent",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement)
        );

        go.transform.SetParent(panel, false);

        // Sits on the edge, outside the layout
        go.GetComponent<LayoutElement>().ignoreLayout = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(AccentWidth, 0f);

        Image image = go.GetComponent<Image>();
        image.color = MenuUI.ButtonHighlight;
        image.raycastTarget = false;
    }
}
