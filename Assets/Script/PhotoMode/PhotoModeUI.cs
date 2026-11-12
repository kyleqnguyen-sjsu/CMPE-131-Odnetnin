using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;


// ============================================================
//  PHOTO MODE MENU
//
//  A panel on the left with five tabs:
//    1 Camera | 2 Effects | 3 Filters | 4 Car | 5 Scene
//
//  Made to be easy to use:
//    - Sections fold open and closed. Effects have an on/off box.
//    - Hover any setting to read what it does (bottom of panel).
//    - Double-click a setting's name to reset it.
//    - The PHOTO button and key bar are always visible.
//
//  Shortcuts
//    1-5 = switch tab          Tab = hide / show the panel
//    H   = hide / show all UI  P   = take a photo
//    F   = frame the car
//
//  The whole menu is built in code (no prefabs), using small
//  helper methods: AddSection, AddSlider, AddToggle, AddButton...
//  Every helper that shows a value also registers a "refresher"
//  so the menu stays in sync when values change elsewhere.
// ============================================================
public class PhotoModeUI : MonoBehaviour
{
    // ----- Layout numbers -----

    // The panel takes this share of the screen width, within limits,
    // so the menu fits any window shape
    private const float PanelFraction = 0.38f;
    private const float MinPanelWidth = 440f;
    private const float MaxPanelWidth = 640f;
    private const float RowHeight = 38f;
    private const float LabelWidth = 200f;
    private const float ButtonHeight = 42f;
    private const int TextSize = 21;

    // Seconds between automatic menu refreshes
    private const float RefreshInterval = 0.25f;

    private const string DefaultHint =
        "Hover a setting for help.  Double-click a setting's name to reset it.";

    private static readonly string[] TabNames =
    {
        "Camera", "Effects", "Filters", "Car", "Scene"
    };

    private static readonly int[] ScreenshotScales = { 1, 2, 4 };

    private static readonly string[] AntiAliasingNames =
    {
        "Off", "FXAA", "SMAA"
    };

    private static readonly string[] FocusModeNames =
    {
        "Manual", "Car", "Screen center"
    };

    private static readonly string[] DofModeNames =
    {
        "Bokeh (camera-like)", "Gaussian (simple)"
    };

    private static readonly string[] TonemapNames =
    {
        "Neutral", "ACES (filmic)"
    };

    private static readonly float[] StepSizes = { 0.05f, 0.25f, 0.5f, 1f, 5f };

    // ----- Things we control -----

    private PhotoModeScene photoScene;
    private PhotoModeCamera cameraRig;
    private PhotoModeEffects fx;
    private PhotoModeCar car;
    private UniversalAdditionalCameraData cameraData;

    // ----- UI pieces -----

    private GameObject canvasRoot;
    private GameObject panelObject;
    private GameObject gridObject;
    private RectTransform tabArea;
    private RectTransform keyBar;
    private TMP_Text toastLabel;
    private TMP_Text hintLabel;

    private readonly List<GameObject> tabContents = new List<GameObject>();
    private readonly List<Button> tabButtons = new List<Button>();
    private readonly List<Action> refreshers = new List<Action>();

    // ----- State -----

    private int screenshotScaleIndex;
    private bool showGrid;
    private bool capturing;
    private float refreshTimer;
    private float toastHideTime;
    private Light sun;

    // Screen fitting (see FitToScreen)
    private CanvasScaler canvasScaler;
    private float panelWidth = 600f;
    private float canvasWidth = 1920f;
    private Vector2 lastScreen;


    // ============================================================
    //  SETUP
    // ============================================================

    public void Init(
        PhotoModeScene scene,
        PhotoModeCamera rig,
        PhotoModeEffects effects,
        PhotoModeCar photoCar)
    {
        photoScene = scene;
        cameraRig = rig;
        fx = effects;
        car = photoCar;

        cameraData = rig.Camera.GetUniversalAdditionalCameraData();

        Canvas canvas = MenuUI.CreateCanvas("Photo Mode Canvas", 100, transform);
        canvasRoot = canvas.gameObject;

        canvasScaler = canvas.GetComponent<CanvasScaler>();
        FitToScreen();

        MenuUI.EnsureEventSystem();

        BuildGrid(canvas.transform);
        BuildPanel(canvas.transform);
        BuildKeyBar(canvas.transform);
        BuildToast(canvas.transform);

        sun = FindSun();

        photoScene.EnvironmentChanged += OnEnvironmentChanged;
        car.Changed += RefreshAll;

        ShowTab(0);
        SetHint(null);
    }

    void OnDestroy()
    {
        if (photoScene != null)
            photoScene.EnvironmentChanged -= OnEnvironmentChanged;

        if (car != null)
            car.Changed -= RefreshAll;
    }

    void OnEnvironmentChanged()
    {
        sun = FindSun();
        RefreshAll();
    }

    Light FindSun()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        Light[] lights = FindObjectsByType<Light>();

        foreach (Light candidate in lights)
        {
            if (candidate.type == LightType.Directional && candidate.enabled)
                return candidate;
        }

        return null;
    }


    // ============================================================
    //  EVERY FRAME
    // ============================================================

    void Update()
    {
        CheckScreenSize();

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        HandleShortcuts(keyboard);
        ClearMenuFocus();
        UpdateFloatingPieces();
        UpdateRefresh();
    }

    void HandleShortcuts(Keyboard keyboard)
    {
        if (capturing)
            return;

        if (keyboard.tabKey.wasPressedThisFrame)
            panelObject.SetActive(!panelObject.activeSelf);

        if (keyboard.hKey.wasPressedThisFrame)
            canvasRoot.SetActive(!canvasRoot.activeSelf);

        if (keyboard.pKey.wasPressedThisFrame)
            TakeScreenshot();

        if (keyboard.fKey.wasPressedThisFrame)
            FrameCar();

        // Keys 1 to 5 pick a tab
        for (int i = 0; i < TabNames.Length; i++)
        {
            Key key = (Key)((int)Key.Digit1 + i);

            if (keyboard[key].wasPressedThisFrame)
            {
                panelObject.SetActive(true);
                ShowTab(i);
            }
        }
    }

    // Clicking a slider or button selects it, which would make the
    // arrow keys change it. Drop the selection so arrow keys only
    // ever move the car.
    void ClearMenuFocus()
    {
        if (EventSystem.current == null || Mouse.current == null)
            return;

        if (Mouse.current.leftButton.isPressed)
            return;

        if (EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // The key bar and the message sit in the middle of the free area
    // (the part of the screen not covered by the panel)
    void UpdateFloatingPieces()
    {
        float panelSpace = panelObject.activeSelf ? panelWidth : 0f;
        float freeWidth = canvasWidth - panelSpace;
        float centerX = panelSpace * 0.5f;

        keyBar.sizeDelta = new Vector2(Mathf.Clamp(freeWidth - 32f, 100f, 1180f), 40f);
        keyBar.anchoredPosition = new Vector2(centerX, 16f);

        toastLabel.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(freeWidth - 32f, 100f, 1000f), 90f);
        toastLabel.rectTransform.anchoredPosition = new Vector2(centerX, 70f);

        if (toastLabel.gameObject.activeSelf && Time.unscaledTime > toastHideTime)
            toastLabel.gameObject.SetActive(false);
    }

    // ----- Fitting any screen shape -----
    //  Wide or square screens: the screen HEIGHT sets the size.
    //  Tall (portrait) screens: the screen WIDTH sets the size.
    //  The panel then takes a share of the width, within limits.

    void CheckScreenSize()
    {
        if (Screen.width != (int)lastScreen.x || Screen.height != (int)lastScreen.y)
            FitToScreen();
    }

    void FitToScreen()
    {
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        float aspect = width / height;

        if (aspect >= 1f)
        {
            canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasScaler.matchWidthOrHeight = 1f;

            canvasWidth = 1080f * aspect;
        }
        else
        {
            canvasScaler.referenceResolution = new Vector2(1080f, 1920f);
            canvasScaler.matchWidthOrHeight = 0f;

            canvasWidth = 1080f;
        }

        panelWidth = Mathf.Clamp(canvasWidth * PanelFraction, MinPanelWidth, MaxPanelWidth);
        panelWidth = Mathf.Min(panelWidth, canvasWidth * 0.9f);

        if (panelObject != null)
            panelObject.GetComponent<RectTransform>().sizeDelta = new Vector2(panelWidth, 0f);

        lastScreen = new Vector2(Screen.width, Screen.height);
    }

    // Keeps values like autofocus distance current on screen
    void UpdateRefresh()
    {
        if (!canvasRoot.activeSelf || !panelObject.activeSelf)
            return;

        refreshTimer += Time.unscaledDeltaTime;

        if (refreshTimer < RefreshInterval)
            return;

        refreshTimer = 0f;
        RefreshAll();
    }

    void RefreshAll()
    {
        foreach (Action refresh in refreshers)
            refresh();
    }

    void ShowToast(string message)
    {
        toastLabel.text = message;
        toastLabel.gameObject.SetActive(true);
        toastHideTime = Time.unscaledTime + 4f;
    }

    // Help line at the bottom of the panel. null shows the default tip.
    void SetHint(string text)
    {
        if (hintLabel == null)
            return;

        hintLabel.text = string.IsNullOrEmpty(text) ? DefaultHint : text;
    }


    // ============================================================
    //  SHORTCUT ACTIONS
    // ============================================================

    void FrameCar()
    {
        if (car.Exists && car.Visible)
            cameraRig.FrameBounds(car.GetBounds());
    }

    void TakeScreenshot()
    {
        if (!capturing)
            StartCoroutine(CaptureRoutine());
    }

    IEnumerator CaptureRoutine()
    {
        capturing = true;

        // Hide the menu for the picture
        canvasRoot.SetActive(false);

        yield return null;

        string folder = GetScreenshotFolder();
        Directory.CreateDirectory(folder);

        string fileName = "PhotoMode_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        string path = Path.Combine(folder, fileName);

        ScreenCapture.CaptureScreenshot(path, ScreenshotScales[screenshotScaleIndex]);

        yield return new WaitForEndOfFrame();
        yield return null;
        yield return null;

        canvasRoot.SetActive(true);
        capturing = false;

        ShowToast("Photo saved: " + fileName + "\n" + folder);
    }

    string GetScreenshotFolder()
    {
#if UNITY_EDITOR
        // Next to the Assets folder
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
#else
        return Path.Combine(Application.persistentDataPath, "Screenshots");
#endif
    }


    // ============================================================
    //  PANEL, TABS AND FLOATING PIECES
    // ============================================================

    void BuildPanel(Transform canvasTransform)
    {
        panelObject = NewRect("Panel", canvasTransform);

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(panelWidth, 0f);
        rect.anchoredPosition = Vector2.zero;

        Image background = panelObject.AddComponent<Image>();
        background.color = MenuUI.PauseDimColor;

        VerticalLayoutGroup layout = panelObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildTopBar(panelObject.transform);
        BuildTabButtons(panelObject.transform);

        // The area the tab pages fill
        GameObject area = NewRect("Tab Area", panelObject.transform);
        tabArea = area.GetComponent<RectTransform>();

        LayoutElement areaLayout = area.AddComponent<LayoutElement>();
        areaLayout.flexibleHeight = 1f;

        BuildCameraTab(CreateTabPage());
        BuildEffectsTab(CreateTabPage());
        BuildFiltersTab(CreateTabPage());
        BuildCarTab(CreateTabPage());
        BuildSceneTab(CreateTabPage());

        // Help line at the bottom
        hintLabel = MenuUI.CreateLabel(
            panelObject.transform,
            DefaultHint,
            18f,
            TextAlignmentOptions.TopLeft,
            MenuUI.MutedTextColor
        );

        // Long hints wrap onto more lines, and shrink if they still do not fit
        hintLabel.textWrappingMode = TextWrappingModes.Normal;
        hintLabel.enableAutoSizing = true;
        hintLabel.fontSizeMin = 12f;
        hintLabel.fontSizeMax = 18f;

        LayoutElement hintLayout = hintLabel.gameObject.AddComponent<LayoutElement>();
        hintLayout.preferredHeight = 62f;
        hintLayout.minHeight = 62f;
    }

    // Title on the left, the PHOTO button on the right
    void BuildTopBar(Transform parent)
    {
        Transform row = NewRow(parent, 48f);

        TMP_Text title = MenuUI.CreateLabel(
            row,
            "PHOTO MODE",
            32f,
            TextAlignmentOptions.MidlineLeft,
            MenuUI.TextColor,
            FontStyles.Bold
        );

        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Button photoButton = CreateButton(row, "TAKE PHOTO  (P)", 48f, TakeScreenshot);

        SetWidthRange(photoButton.gameObject, 170f, 230f);
        StyleButton(photoButton, true);

        AttachHint(photoButton.gameObject, "Hides the menu and saves a picture. Shortcut: P.", null);
    }

    void BuildTabButtons(Transform parent)
    {
        Transform row = NewRow(parent, 44f);

        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;

            Button button = CreateButton(
                row,
                (i + 1) + "  " + TabNames[i],
                44f,
                delegate { ShowTab(index); }
            );

            button.GetComponent<LayoutElement>().flexibleWidth = 1f;

            tabButtons.Add(button);
        }
    }

    void ShowTab(int index)
    {
        for (int i = 0; i < tabContents.Count; i++)
            tabContents[i].SetActive(i == index);

        for (int i = 0; i < tabButtons.Count; i++)
            StyleButton(tabButtons[i], i == index);

        RefreshAll();
    }

    // One scrolling page. Returns the place to put the widgets.
    Transform CreateTabPage()
    {
        GameObject scrollObject = NewRect("Tab Page", tabArea);
        Stretch(scrollObject.GetComponent<RectTransform>());

        // Invisible image so the mouse wheel works over empty space
        Image catcher = scrollObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);

        ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        GameObject viewport = NewRect("Viewport", scrollObject.transform);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<RectMask2D>();

        GameObject content = NewRect("Content", viewport.transform);

        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 12, 4, 20);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;

        tabContents.Add(scrollObject);

        return content.transform;
    }

    // Controls reminder along the bottom of the free area
    void BuildKeyBar(Transform canvasTransform)
    {
        GameObject bar = NewRect("Key Bar", canvasTransform);

        keyBar = bar.GetComponent<RectTransform>();
        keyBar.anchorMin = new Vector2(0.5f, 0f);
        keyBar.anchorMax = new Vector2(0.5f, 0f);
        keyBar.pivot = new Vector2(0.5f, 0f);
        keyBar.sizeDelta = new Vector2(1180f, 40f);
        keyBar.anchoredPosition = new Vector2(panelWidth * 0.5f, 16f);

        Image background = bar.AddComponent<Image>();
        background.color = MenuUI.HudPanelColor;
        background.raycastTarget = false;

        TMP_Text text = MenuUI.CreateLabel(
            bar.transform,
            "RMB  Look      WASD  Move      Q / E  Down / Up      Shift  Fast      Ctrl  Slow      " +
            "Wheel  Speed      Tab  Menu      H  Hide all      P  Photo",
            18f,
            TextAlignmentOptions.Center,
            MenuUI.MutedTextColor
        );

        // One line that shrinks to fit narrow windows
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMin = 9f;
        text.fontSizeMax = 18f;

        Stretch(text.rectTransform);
    }

    void BuildGrid(Transform canvasTransform)
    {
        gridObject = NewRect("Rule Of Thirds", canvasTransform);
        Stretch(gridObject.GetComponent<RectTransform>());

        Color lineColor = new Color(1f, 1f, 1f, 0.35f);

        for (int i = 1; i <= 2; i++)
        {
            float position = i / 3f;

            CreateLine(gridObject.transform, lineColor, new Vector2(position, 0f), new Vector2(position, 1f), new Vector2(2f, 0f));
            CreateLine(gridObject.transform, lineColor, new Vector2(0f, position), new Vector2(1f, position), new Vector2(0f, 2f));
        }

        gridObject.SetActive(false);
    }

    void CreateLine(Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
    {
        GameObject line = NewRect("Line", parent);

        RectTransform rect = line.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        Image image = line.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void BuildToast(Transform canvasTransform)
    {
        toastLabel = MenuUI.CreateLabel(
            canvasTransform,
            "",
            22f,
            TextAlignmentOptions.Center,
            MenuUI.TextColor
        );

        RectTransform rect = toastLabel.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(1000f, 90f);
        rect.anchoredPosition = new Vector2(panelWidth * 0.5f, 70f);

        // Long file paths wrap instead of running off screen
        toastLabel.textWrappingMode = TextWrappingModes.Normal;
        toastLabel.enableAutoSizing = true;
        toastLabel.fontSizeMin = 12f;
        toastLabel.fontSizeMax = 22f;

        toastLabel.gameObject.SetActive(false);
    }


    // ============================================================
    //  TAB 1: CAMERA
    // ============================================================

    void BuildCameraTab(Transform page)
    {
        Transform start = AddSection(page, "Quick start (read me)", true, null, null,
            "A short guide to the basics.");

        AddInfo(start, "1. Hold the RIGHT mouse button and move the mouse to look around.");
        AddInfo(start, "2. While holding it, use W A S D to fly, Q down, E up. Shift = faster.");
        AddInfo(start, "3. Open the Car tab and press 'In front of camera' to place the car.");
        AddInfo(start, "4. Try the Filters tab for a quick look, then press TAKE PHOTO.");
        AddInfo(start, "Tip: hover over any control to see what it does at the bottom of this panel.");

        Transform lens = AddSection(page, "Lens", true, null, null,
            "Field of view, tilt and edge smoothing.");

        AddSlider(lens, "Field of view", 10f, 120f,
            () => cameraRig.Camera.fieldOfView,
            v => cameraRig.Camera.fieldOfView = v,
            "0", true,
            "Low = zoomed in, flatter look. High = wide angle. 35 to 60 suits cars.");

        AddSlider(lens, "Roll (tilt)", -45f, 45f,
            () => cameraRig.roll,
            v => cameraRig.roll = v,
            "0.0", false,
            "Tilts the horizon left or right.");

        AddCycle(lens, "Anti-aliasing", AntiAliasingNames,
            GetAntiAliasing, SetAntiAliasing,
            "Smooths jagged edges. SMAA looks best, FXAA is softer.");

        Transform movement = AddSection(page, "Camera movement", false, null, null,
            "How the free camera feels.");

        AddSlider(movement, "Move speed", 0.5f, 100f,
            () => cameraRig.moveSpeed,
            v => cameraRig.moveSpeed = v,
            "0.0", false,
            "Meters per second. The mouse wheel changes this too.");

        AddSlider(movement, "Look sensitivity", 0.02f, 0.5f,
            () => cameraRig.lookSensitivity,
            v => cameraRig.lookSensitivity = v,
            "0.00", false,
            "How far the view turns for each bit of mouse movement.");

        AddSlider(movement, "Smoothing", 0f, 1f,
            () => cameraRig.smoothing,
            v => cameraRig.smoothing = v,
            "0.00", false,
            "0 = the camera stops instantly. Higher = floaty, cinematic movement.");

        Transform capture = AddSection(page, "Photo", true, null, null,
            "Picture size and composition help.");

        AddCycle(capture, "Picture size",
            new string[] { "1x  (screen size)", "2x", "4x" },
            () => screenshotScaleIndex,
            v => screenshotScaleIndex = v,
            "Multiplies the picture size. 2x or 4x gives a sharper, bigger file.");

        AddToggle(capture, "Rule of thirds grid",
            () => showGrid,
            v =>
            {
                showGrid = v;
                gridObject.SetActive(v);
            },
            "Shows guide lines to help place the subject. They are not in the picture.");

        AddInfo(capture, "Photos are saved to a Screenshots folder\nnext to the Assets folder.");
    }

    int GetAntiAliasing()
    {
        switch (cameraData.antialiasing)
        {
            case AntialiasingMode.FastApproximateAntialiasing: return 1;
            case AntialiasingMode.SubpixelMorphologicalAntiAliasing: return 2;
            default: return 0;
        }
    }

    void SetAntiAliasing(int index)
    {
        if (index == 1)
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        else if (index == 2)
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        else
            cameraData.antialiasing = AntialiasingMode.None;
    }


    // ============================================================
    //  TAB 2: EFFECTS
    //  Each effect is a folding section with an on/off box.
    // ============================================================

    void BuildEffectsTab(Transform page)
    {
        BuildDepthOfFieldSection(page);
        BuildAmbientOcclusionSection(page);

        Transform bloom = AddSection(page, "Bloom", false,
            () => fx.bloom.active, v => fx.bloom.active = v,
            "A soft glow around bright lights and shiny spots.");

        AddSlider(bloom, "Intensity", 0f, 3f, () => fx.bloom.intensity.value, v => fx.bloom.intensity.Override(v),
            "0.00", false, "How strong the glow is.");
        AddSlider(bloom, "Threshold", 0f, 2f, () => fx.bloom.threshold.value, v => fx.bloom.threshold.Override(v),
            "0.00", false, "How bright something must be to glow. Lower = more glow.");
        AddSlider(bloom, "Scatter", 0f, 1f, () => fx.bloom.scatter.value, v => fx.bloom.scatter.Override(v),
            "0.00", false, "How far the glow spreads.");

        Transform vignette = AddSection(page, "Vignette", false,
            () => fx.vignette.active, v => fx.vignette.active = v,
            "Darkens the corners to draw the eye to the middle.");

        AddSlider(vignette, "Intensity", 0f, 1f, () => fx.vignette.intensity.value, v => fx.vignette.intensity.Override(v),
            "0.00", false, "How dark the corners get.");
        AddSlider(vignette, "Smoothness", 0.01f, 1f, () => fx.vignette.smoothness.value, v => fx.vignette.smoothness.Override(v),
            "0.00", false, "How gradual the darkening is.");

        Transform grain = AddSection(page, "Film grain", false,
            () => fx.filmGrain.active, v => fx.filmGrain.active = v,
            "Adds fine noise like photographic film.");

        AddSlider(grain, "Intensity", 0f, 1f, () => fx.filmGrain.intensity.value, v => fx.filmGrain.intensity.Override(v),
            "0.00", false, "How visible the grain is.");
        AddSlider(grain, "Response", 0f, 1f, () => fx.filmGrain.response.value, v => fx.filmGrain.response.Override(v),
            "0.00", false, "Higher = grain shows less in bright areas.");

        Transform chromatic = AddSection(page, "Chromatic aberration", false,
            () => fx.chromaticAberration.active, v => fx.chromaticAberration.active = v,
            "Colored fringes at the edges, like a cheap lens.");

        AddSlider(chromatic, "Intensity", 0f, 1f, () => fx.chromaticAberration.intensity.value,
            v => fx.chromaticAberration.intensity.Override(v),
            "0.00", false, "How strong the color fringes are.");

        Transform distortion = AddSection(page, "Lens distortion", false,
            () => fx.lensDistortion.active, v => fx.lensDistortion.active = v,
            "Bends the picture like a fisheye (or the opposite).");

        AddSlider(distortion, "Amount", -1f, 1f, () => fx.lensDistortion.intensity.value,
            v => fx.lensDistortion.intensity.Override(v),
            "0.00", false, "Below 0 bulges inward, above 0 bulges outward.");

        Transform tonemap = AddSection(page, "Tonemapping", false,
            () => fx.tonemapping.active, v => fx.tonemapping.active = v,
            "Controls how bright areas roll off. Gives a filmic look.");

        AddCycle(tonemap, "Style", TonemapNames, GetTonemapIndex, SetTonemapIndex,
            "ACES is punchy and cinematic. Neutral keeps colors closer to the original.");

        MenuUI.CreateSpacer(page, 6f);

        AddButton(page, "Reset all effects", delegate
        {
            fx.ResetAll();
            RefreshAll();
        }, "Turns every effect off and puts every value back to its start.");
    }

    void BuildDepthOfFieldSection(Transform page)
    {
        Transform dof = AddSection(page, "Depth of field", true,
            () => fx.dof.active, v => fx.dof.active = v,
            "Blurs what is far from the focus point. Great for making the car stand out.");

        AddCycle(dof, "Style", DofModeNames,
            GetDofModeIndex, SetDofModeIndex,
            "Bokeh looks like a real camera lens. Gaussian is a simple distance blur.");

        AddCycle(dof, "Focus on", FocusModeNames,
            () => (int)fx.focusMode,
            v => fx.focusMode = (PhotoModeEffects.FocusMode)v,
            "Car = always sharp on the car. Screen center = sharp on whatever is in the middle.");

        // Settings for Bokeh
        GameObject bokehBody = NewBody(dof);

        AddSlider(bokehBody.transform, "Focus distance (m)", 0.1f, 100f,
            () => fx.dof.focusDistance.value,
            v => fx.dof.focusDistance.Override(v),
            "0.0", false,
            "Only used when Focus on is Manual. Autofocus sets it for you.");

        AddSlider(bokehBody.transform, "Aperture (f-stop)", 1f, 32f,
            () => fx.dof.aperture.value,
            v => fx.dof.aperture.Override(v),
            "0.0", false,
            "Low = strong blur. High = everything sharp. f/2.8 to f/5.6 suits hero shots.");

        AddSlider(bokehBody.transform, "Focal length (mm)", 10f, 300f,
            () => fx.dof.focalLength.value,
            v => fx.dof.focalLength.Override(v),
            "0", false,
            "Higher = more blur and a more zoomed-in look.");

        // Settings for Gaussian
        GameObject gaussianBody = NewBody(dof);

        AddSlider(gaussianBody.transform, "Blur starts at (m)", 0f, 200f,
            () => fx.dof.gaussianStart.value,
            v => fx.dof.gaussianStart.Override(v),
            "0", false,
            "Distance where the blur begins.");

        AddSlider(gaussianBody.transform, "Fully blurred at (m)", 1f, 400f,
            () => fx.dof.gaussianEnd.value,
            v => fx.dof.gaussianEnd.Override(v),
            "0", false,
            "Distance where the blur reaches its maximum.");

        AddSlider(gaussianBody.transform, "Max blur", 0.5f, 1.5f,
            () => fx.dof.gaussianMaxRadius.value,
            v => fx.dof.gaussianMaxRadius.Override(v),
            "0.00", false,
            "How strong the strongest blur is.");

        // Show only the settings of the chosen style
        refreshers.Add(delegate
        {
            bool gaussian = fx.dof.mode.value == DepthOfFieldMode.Gaussian;

            bokehBody.SetActive(!gaussian);
            gaussianBody.SetActive(gaussian);
        });
    }

    void BuildAmbientOcclusionSection(Transform page)
    {
        if (!fx.AoAvailable)
        {
            Transform missing = AddSection(page, "Ambient occlusion", false, null, null, null);

            AddInfo(missing,
                "Not available: the active render pipeline has no\n" +
                "Screen Space Ambient Occlusion renderer feature.\n" +
                "(Check the Quality settings, or add the feature\n" +
                "to the renderer.)");

            return;
        }

        Transform ao = AddSection(page, "Ambient occlusion", false,
            () => fx.AoEnabled, v => fx.AoEnabled = v,
            "Adds soft contact shadows in corners and crevices. Makes things feel grounded.");

        AddSlider(ao, "Intensity", 0f, 4f, () => fx.AoIntensity, v => fx.AoIntensity = v,
            "0.00", false, "How dark the contact shadows are.");
        AddSlider(ao, "Radius", 0.01f, 1f, () => fx.AoRadius, v => fx.AoRadius = v,
            "0.00", false, "How far from a corner the shadow reaches.");
        AddSlider(ao, "Direct light strength", 0f, 1f, () => fx.AoDirectLighting, v => fx.AoDirectLighting = v,
            "0.00", false, "How much the shadows also darken areas lit by the sun.");
    }

    int GetDofModeIndex()
    {
        return fx.dof.mode.value == DepthOfFieldMode.Gaussian ? 1 : 0;
    }

    void SetDofModeIndex(int index)
    {
        if (index == 1)
            fx.dof.mode.Override(DepthOfFieldMode.Gaussian);
        else
            fx.dof.mode.Override(DepthOfFieldMode.Bokeh);
    }

    int GetTonemapIndex()
    {
        return fx.tonemapping.mode.value == TonemappingMode.Neutral ? 0 : 1;
    }

    void SetTonemapIndex(int index)
    {
        if (index == 0)
            fx.tonemapping.mode.Override(TonemappingMode.Neutral);
        else
            fx.tonemapping.mode.Override(TonemappingMode.ACES);
    }


    // ============================================================
    //  TAB 3: FILTERS
    // ============================================================

    void BuildFiltersTab(Transform page)
    {
        Transform looks = AddSection(page, "Choose a look", true, null, null,
            "One click sets the colors plus vignette, grain, bloom and fringing.");

        // Two filter buttons per row
        for (int i = 0; i < PhotoModeEffects.Filters.Length; i += 2)
        {
            Transform row = NewRow(looks, 46f);

            AddFilterButton(row, i);

            if (i + 1 < PhotoModeEffects.Filters.Length)
                AddFilterButton(row, i + 1);
        }

        AddSlider(looks, "Strength", 0f, 1f,
            () => fx.FilterStrength,
            v => fx.FilterStrength = v,
            "0.00", false,
            "Blends the filter in. 60 to 75 percent often looks more natural than full strength.");

        AddInfo(looks, "Moving Strength re-applies the chosen filter,\nwhich replaces any fine tuning below.");

        Transform color = AddSection(page, "Color", true, null, null,
            "Fine tune the look.");

        AddSlider(color, "Exposure", -3f, 3f,
            () => fx.colorAdjustments.postExposure.value,
            v => fx.colorAdjustments.postExposure.Override(v),
            "0.00", false,
            "Overall brightness. Above 0 brighter, below 0 darker.");

        AddSlider(color, "Contrast", -100f, 100f,
            () => fx.colorAdjustments.contrast.value,
            v => fx.colorAdjustments.contrast.Override(v),
            "0", true,
            "Difference between light and dark areas.");

        AddSlider(color, "Saturation", -100f, 100f,
            () => fx.colorAdjustments.saturation.value,
            v => fx.colorAdjustments.saturation.Override(v),
            "0", true,
            "Color strength. -100 is black and white.");

        AddSlider(color, "Hue shift", -180f, 180f,
            () => fx.colorAdjustments.hueShift.value,
            v => fx.colorAdjustments.hueShift.Override(v),
            "0", true,
            "Rotates every color around the color wheel.");

        Transform balance = AddSection(page, "White balance", false, null, null,
            "Make the picture warmer or cooler.");

        AddSlider(balance, "Temperature", -100f, 100f,
            () => fx.whiteBalance.temperature.value,
            v => fx.whiteBalance.temperature.Override(v),
            "0", true,
            "Below 0 cooler and bluer. Above 0 warmer and more orange.");

        AddSlider(balance, "Tint", -100f, 100f,
            () => fx.whiteBalance.tint.value,
            v => fx.whiteBalance.tint.Override(v),
            "0", true,
            "Below 0 greener. Above 0 more magenta.");
    }

    void AddFilterButton(Transform row, int index)
    {
        Button button = CreateButton(
            row,
            PhotoModeEffects.Filters[index].name,
            46f,
            delegate
            {
                fx.ApplyFilter(index);
                RefreshAll();
            }
        );

        button.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // The chosen filter stays blue
        refreshers.Add(delegate { StyleButton(button, fx.CurrentFilter == index); });
    }


    // ============================================================
    //  TAB 4: CAR
    // ============================================================

    void BuildCarTab(Transform page)
    {
        Transform carSection = AddSection(page, "Car", true, null, null,
            "Add or remove the car. It cannot be driven in photo mode.");

        AddButtons(carSection,
            "Spawn car", delegate { car.Spawn(); RefreshAll(); },
            "Remove car", delegate { car.Remove(); RefreshAll(); },
            "Spawn puts the car in front of the camera.");

        AddToggle(carSection, "Visible",
            () => car.Visible,
            v => car.Visible = v,
            "Hide the car without removing it.");

        Transform place = AddSection(page, "Place on the ground", true, null, null,
            "Quick ways to put the car on a surface.");

        AddToggle(place, "Stick to surface",
            () => car.StickToGround,
            v => car.StickToGround = v,
            "On: the car sits on the ground and tilts with slopes and banked corners. " +
            "Off: free placement. Moving it up or down turns this off.");

        AddSlider(place, "Ride height (cm)", -10f, 30f,
            () => car.RideHeight * 100f,
            v => car.RideHeight = v / 100f,
            "0", true,
            "Fine tune how high the tires sit above the surface. 0 = touching.");

        AddButton(place, "In front of camera", car.PlaceInFrontOfCamera,
            "Puts the car on the ground ahead of you, facing the camera.");

        AddButton(place, "At the screen center", car.PlaceAtCrosshair,
            "Puts the car on whatever is under the middle of the screen.");

        AddButton(place, "Snap to surface", car.SnapToGround,
            "Drops the car onto the ground below it and matches the slope.");

        AddButton(place, "Frame car with camera  (F)", FrameCar,
            "Moves the camera back until the whole car is in view.");

        Transform position = AddSection(page, "Position", true, null, null,
            "Hold + or - to keep moving. Arrow keys work too.");

        AddCycle(position, "Step size", StepSizeNames(),
            GetStepSizeIndex,
            v => car.stepMeters = StepSizes[v],
            "How far one press of + or - moves the car.");

        AddStepper(position, "X (east-west)", () => car.Position.x,
            dir => car.Nudge(Vector3.right * dir * car.stepMeters),
            "Move along the world X axis.");

        AddStepper(position, "Y (height)", () => car.Position.y,
            dir => car.Nudge(Vector3.up * dir * car.stepMeters),
            "Move up or down. This turns Stick to surface off.");

        AddStepper(position, "Z (north-south)", () => car.Position.z,
            dir => car.Nudge(Vector3.forward * dir * car.stepMeters),
            "Move along the world Z axis.");

        Transform paint = AddSection(page, "Paint", true, null, null,
            "Pick a body color. These are the paint materials already in the project.");

        AddCycle(paint, "Color", PaintNames(),
            () => car.PaintIndex,
            v => car.SetPaint(v),
            "Click to go to the next color.");

        AddSwatchRow(paint);

        Transform wheels = AddSection(page, "Steering", true, null, null,
            "Turn the front wheels, as if the driver is steering.");

        AddSlider(wheels, "Steering angle", -45f, 45f,
            () => car.SteerAngle,
            v => car.SteerAngle = v,
            "0", true,
            "Below 0 turns the wheels left, above 0 turns them right. Double-click to reset.");

        Transform rotation = AddSection(page, "Rotation", true, null, null,
            "Turn the car. With Stick to surface on, pitch and roll are on top of the ground angle.");

        AddSlider(rotation, "Heading", 0f, 360f,
            () => car.Yaw,
            v => car.Yaw = v,
            "0", true,
            "Which way the car faces. [ and ] keys turn it too.",
            false);

        AddSlider(rotation, "Pitch (nose)", -45f, 45f,
            () => car.Pitch,
            v => car.Pitch = v,
            "0", true,
            "Tilts the nose down (above 0) or up (below 0).");

        AddSlider(rotation, "Roll (lean)", -45f, 45f,
            () => car.Roll,
            v => car.Roll = v,
            "0", true,
            "Leans the car to one side.");

        AddButton(rotation, "Reset pitch and roll", car.ResetRotation,
            "Back to sitting flat on the surface.");

        AddInfo(page,
            "Keys: arrows = move, Page Up / Down = up / down,\n" +
            "[ and ] = turn, Shift = fast, Ctrl = slow.");
    }

    static readonly string[] PaintLabels = { "Default", "Blue", "Red", "Gray", "Purple" };

    string[] PaintNames()
    {
        string[] names = new string[Mathf.Max(1, car.PaintCount)];

        for (int i = 0; i < names.Length; i++)
            names[i] = i < PaintLabels.Length ? PaintLabels[i] : "Color " + (i + 1);

        return names;
    }

    // A row of buttons, one per paint color, so you can pick directly
    void AddSwatchRow(Transform parent)
    {
        Transform row = NewRow(parent, ButtonHeight);
        string[] names = PaintNames();

        for (int i = 0; i < names.Length; i++)
        {
            int index = i;

            Button button = CreateButton(row, names[i], ButtonHeight, delegate
            {
                car.SetPaint(index);
                RefreshAll();
            });

            AttachHint(button.gameObject, "Paint the car " + names[i].ToLower() + ".", null);

            refreshers.Add(delegate { StyleButton(button, car.PaintIndex == index); });
        }
    }

    string[] StepSizeNames()
    {
        string[] names = new string[StepSizes.Length];

        for (int i = 0; i < StepSizes.Length; i++)
            names[i] = StepSizes[i].ToString("0.##") + " m";

        return names;
    }

    int GetStepSizeIndex()
    {
        int best = 0;

        for (int i = 0; i < StepSizes.Length; i++)
        {
            if (Mathf.Abs(StepSizes[i] - car.stepMeters) < Mathf.Abs(StepSizes[best] - car.stepMeters))
                best = i;
        }

        return best;
    }


    // ============================================================
    //  TAB 5: SCENE
    // ============================================================

    void BuildSceneTab(Transform page)
    {
        Transform background = AddSection(page, "Background", true, null, null,
            "Choose what the car and camera are in.");

        // One button per background. The current one is blue.
        Transform row = NewRow(background, 50f);

        for (int i = 0; i < PhotoModeScene.EnvironmentNames.Length; i++)
        {
            int index = i;

            Button button = CreateButton(
                row,
                PhotoModeScene.EnvironmentNames[i],
                50f,
                delegate { photoScene.SetEnvironment(index); }
            );

            button.GetComponent<LayoutElement>().flexibleWidth = 1f;

            AttachHint(button.gameObject,
                index == 0
                    ? "A plain gray floor. Good for clean product-style shots."
                    : "The game's race track.",
                null);

            refreshers.Add(delegate { StyleButton(button, photoScene.CurrentEnvironment == index); });
        }

        AddDynamicInfo(background, GetEnvironmentStatus);

        AddButton(background, "Camera to the start view", photoScene.ResetCameraToScene,
            "Jumps the camera back to where it began for this background.");

        AddToggle(background, "Use the track's own post-processing",
            () => fx.SceneVolumesEnabled,
            v => fx.SceneVolumesEnabled = v,
            "Turn off to see only the effects chosen in this menu.");

        Transform lighting = AddSection(page, "Sun", true, null, null,
            "Change the light direction. Nothing here is saved.");

        AddSlider(lighting, "Sun height", 0f, 90f,
            () => sun != null ? NormalizeSigned(sun.transform.eulerAngles.x) : 0f,
            SetSunAngle,
            "0", true,
            "0 = the sun is on the horizon (long shadows). 90 = straight overhead.",
            false);

        AddSlider(lighting, "Sun direction", 0f, 360f,
            () => sun != null ? sun.transform.eulerAngles.y : 0f,
            SetSunHeading,
            "0", true,
            "Turns the sun around the scene.",
            false);

        AddSlider(lighting, "Sun brightness", 0f, 5f,
            () => sun != null ? sun.intensity : 0f,
            v => { if (sun != null) sun.intensity = v; },
            "0.00", false,
            "How strong the sunlight is.",
            false);
    }

    string GetEnvironmentStatus()
    {
        if (!string.IsNullOrEmpty(photoScene.Status))
            return photoScene.Status;

        return "Showing: " + PhotoModeScene.EnvironmentNames[photoScene.CurrentEnvironment];
    }

    void SetSunAngle(float value)
    {
        if (sun == null)
            return;

        Vector3 angles = sun.transform.eulerAngles;
        angles.x = value;
        sun.transform.eulerAngles = angles;
    }

    void SetSunHeading(float value)
    {
        if (sun == null)
            return;

        Vector3 angles = sun.transform.eulerAngles;
        angles.y = value;
        sun.transform.eulerAngles = angles;
    }

    static float NormalizeSigned(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;

        return angle;
    }


    // ============================================================
    //  WIDGETS  (small building blocks for the pages)
    // ============================================================

    // A folding section. Click the title bar to open or close it.
    // If getOn and setOn are given, the bar also has an on/off box.
    // Returns the place to put the section's widgets.
    Transform AddSection(
        Transform parent,
        string title,
        bool expanded,
        Func<bool> getOn,
        Action<bool> setOn,
        string hint)
    {
        GameObject header = NewRect("Section Header", parent);

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 10, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        LayoutElement element = header.AddComponent<LayoutElement>();
        element.preferredHeight = 44f;
        element.minHeight = 44f;

        Image headerImage = header.AddComponent<Image>();
        headerImage.color = Color.white;

        Button headerButton = header.AddComponent<Button>();
        headerButton.targetGraphic = headerImage;
        headerButton.navigation = NoNavigation();
        StyleButton(headerButton, false);

        TMP_Text titleLabel = MenuUI.CreateLabel(
            header.transform,
            "",
            TextSize,
            TextAlignmentOptions.MidlineLeft,
            MenuUI.TextColor,
            FontStyles.Bold
        );

        titleLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        if (getOn != null)
            CreateToggleBox(header.transform, getOn, setOn);

        GameObject body = NewBody(parent);
        body.SetActive(expanded);

        Action updateTitle = delegate
        {
            titleLabel.text = (body.activeSelf ? "-   " : "+   ") + title;
        };

        updateTitle();

        headerButton.onClick.AddListener(delegate
        {
            body.SetActive(!body.activeSelf);
            updateTitle();
        });

        if (hint != null)
            AttachHint(header, hint, null);

        return body.transform;
    }

    // The part of a section that opens and closes
    GameObject NewBody(Transform parent)
    {
        GameObject body = NewRect("Section Body", parent);

        VerticalLayoutGroup layout = body.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 0, 4, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return body;
    }

    // A few lines of small gray text. It wraps to the panel width.
    void AddInfo(Transform parent, string text)
    {
        TMP_Text label = MenuUI.CreateLabel(
            parent,
            text.Replace("\n", " "),
            17f,
            TextAlignmentOptions.TopLeft,
            MenuUI.MutedTextColor
        );

        label.textWrappingMode = TextWrappingModes.Normal;
    }

    // Text that is re-read from a function whenever the menu refreshes
    void AddDynamicInfo(Transform parent, Func<string> getText)
    {
        TMP_Text label = MenuUI.CreateLabel(
            parent,
            "",
            19f,
            TextAlignmentOptions.MidlineLeft,
            MenuUI.TextColor
        );

        label.textWrappingMode = TextWrappingModes.Normal;

        refreshers.Add(delegate { label.text = getText(); });
    }

    // allowReset: double-clicking the name puts the slider back to the
    // value it had when the menu was built
    void AddSlider(
        Transform parent,
        string label,
        float min,
        float max,
        Func<float> get,
        Action<float> set,
        string format,
        bool whole,
        string hint,
        bool allowReset = true)
    {
        Transform row = NewRow(parent, RowHeight);

        TMP_Text nameLabel = AddRowLabel(row, label);

        Slider slider = CreateSlider(row);
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;

        TMP_Text valueText = MenuUI.CreateLabel(
            row,
            "",
            TextSize,
            TextAlignmentOptions.MidlineRight,
            MenuUI.MutedTextColor
        );

        SetWidthRange(valueText.gameObject, 56f, 76f);

        slider.onValueChanged.AddListener(delegate (float value)
        {
            set(value);
            valueText.text = value.ToString(format);
        });

        refreshers.Add(delegate
        {
            float value = get();

            slider.SetValueWithoutNotify(value);
            valueText.text = value.ToString(format);
        });

        // Remember the starting value so the name can reset to it
        float startValue = get();

        Action reset = null;

        if (allowReset)
        {
            reset = delegate
            {
                set(startValue);
                RefreshAll();
            };
        }

        MakeHoverable(row.gameObject);
        AttachHint(row.gameObject, hint, null);

        // Only the name reacts to double-click (a slider needs its own clicks)
        if (reset != null)
        {
            nameLabel.raycastTarget = true;

            PhotoModeHint resetter = nameLabel.gameObject.AddComponent<PhotoModeHint>();
            resetter.onDoubleClick = reset;
        }
    }

    void AddToggle(Transform parent, string label, Func<bool> get, Action<bool> set, string hint)
    {
        Transform row = NewRow(parent, RowHeight);

        TMP_Text text = AddRowLabel(row, label);
        text.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreateToggleBox(row, get, set);

        MakeHoverable(row.gameObject);
        AttachHint(row.gameObject, hint, null);
    }

    // A button that shows the current choice and moves to the next one when clicked
    void AddCycle(
        Transform parent,
        string label,
        string[] options,
        Func<int> get,
        Action<int> set,
        string hint)
    {
        Transform row = NewRow(parent, RowHeight);

        AddRowLabel(row, label);

        Button button = CreateButton(row, "", 38f, delegate
        {
            int next = (get() + 1) % options.Length;

            set(next);
            RefreshAll();
        });

        button.GetComponent<LayoutElement>().flexibleWidth = 1f;

        TMP_Text valueText = button.GetComponentInChildren<TMP_Text>();

        refreshers.Add(delegate
        {
            valueText.text = options[Mathf.Clamp(get(), 0, options.Length - 1)];
        });

        MakeHoverable(row.gameObject);
        AttachHint(row.gameObject, hint + "  (Click to change.)", null);
    }

    void AddButton(Transform parent, string text, Action onClick, string hint)
    {
        Button button = CreateButton(parent, text, ButtonHeight, onClick);

        AttachHint(button.gameObject, hint, null);
    }

    // Two buttons side by side
    void AddButtons(Transform parent, string textA, Action clickA, string textB, Action clickB, string hint)
    {
        Transform row = NewRow(parent, ButtonHeight);

        Button a = CreateButton(row, textA, ButtonHeight, clickA);
        Button b = CreateButton(row, textB, ButtonHeight, clickB);

        a.GetComponent<LayoutElement>().flexibleWidth = 1f;
        b.GetComponent<LayoutElement>().flexibleWidth = 1f;

        AttachHint(a.gameObject, hint, null);
        AttachHint(b.gameObject, hint, null);
    }

    // "-" and "+" buttons that repeat while held. "step" gets -1 or +1.
    void AddStepper(Transform parent, string label, Func<float> get, Action<int> step, string hint)
    {
        Transform row = NewRow(parent, RowHeight);

        AddRowLabel(row, label);

        TMP_Text valueText = MenuUI.CreateLabel(
            row,
            "",
            TextSize,
            TextAlignmentOptions.Center,
            MenuUI.TextColor
        );

        valueText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        AddHoldButton(row, "-", delegate { step(-1); });
        AddHoldButton(row, "+", delegate { step(1); });

        refreshers.Add(delegate { valueText.text = get().ToString("0.00"); });

        MakeHoverable(row.gameObject);
        AttachHint(row.gameObject, hint, null);
    }

    void AddHoldButton(Transform row, string text, Action onStep)
    {
        // The click itself does nothing. The hold component does the work.
        Button button = CreateButton(row, text, 38f, delegate { });

        SetFixedWidth(button.gameObject, 56f);

        PhotoModeHoldButton hold = button.gameObject.AddComponent<PhotoModeHoldButton>();
        hold.onStep = onStep;
    }

    TMP_Text AddRowLabel(Transform row, string text)
    {
        TMP_Text label = MenuUI.CreateLabel(
            row,
            text,
            TextSize,
            TextAlignmentOptions.MidlineLeft,
            MenuUI.TextColor
        );

        // Names shrink on narrow panels, and end in "..." if still too long
        SetWidthRange(label.gameObject, 100f, LabelWidth);

        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        return label;
    }

    // The small square check box
    Toggle CreateToggleBox(Transform parent, Func<bool> get, Action<bool> set)
    {
        GameObject box = NewRect("Toggle", parent);

        LayoutElement boxLayout = box.AddComponent<LayoutElement>();
        boxLayout.preferredWidth = 30f;
        boxLayout.minWidth = 30f;
        boxLayout.preferredHeight = 30f;

        Image boxImage = box.AddComponent<Image>();
        boxImage.color = new Color(0.07f, 0.08f, 0.12f, 1f);

        GameObject mark = NewRect("Checkmark", box.transform);

        RectTransform markRect = mark.GetComponent<RectTransform>();
        markRect.anchorMin = Vector2.zero;
        markRect.anchorMax = Vector2.one;
        markRect.offsetMin = new Vector2(5f, 5f);
        markRect.offsetMax = new Vector2(-5f, -5f);

        Image markImage = mark.AddComponent<Image>();
        markImage.color = new Color(0.35f, 0.65f, 1f);

        Toggle toggle = box.AddComponent<Toggle>();
        toggle.targetGraphic = boxImage;
        toggle.graphic = markImage;
        toggle.navigation = NoNavigation();

        toggle.onValueChanged.AddListener(delegate (bool value)
        {
            set(value);
            RefreshAll();
        });

        refreshers.Add(delegate { toggle.SetIsOnWithoutNotify(get()); });

        return toggle;
    }


    // ============================================================
    //  HINTS
    // ============================================================

    // Show "hint" when the mouse is over the object.
    // If onDoubleClick is given, double-clicking the object runs it.
    void AttachHint(GameObject target, string hint, Action onDoubleClick)
    {
        PhotoModeHint hover = target.AddComponent<PhotoModeHint>();

        hover.hint = hint;
        hover.onHintChanged = SetHint;
        hover.onDoubleClick = onDoubleClick;
    }

    // Rows are see-through, so give them something the mouse can hit
    void MakeHoverable(GameObject row)
    {
        if (row.GetComponent<Image>() != null)
            return;

        Image catcher = row.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);
    }


    // ============================================================
    //  LOW-LEVEL UI BUILDING
    // ============================================================

    GameObject NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        return go;
    }

    void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void SetFixedWidth(GameObject go, float width)
    {
        SetWidthRange(go, width, width);
    }

    // Wants "preferred" width, but can shrink down to "minimum" when space is tight
    void SetWidthRange(GameObject go, float minimum, float preferred)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();

        if (element == null)
            element = go.AddComponent<LayoutElement>();

        element.preferredWidth = preferred;
        element.minWidth = minimum;
        element.flexibleWidth = 0f;
    }

    Navigation NoNavigation()
    {
        Navigation navigation = new Navigation();
        navigation.mode = Navigation.Mode.None;

        return navigation;
    }

    // A horizontal strip. Children sit side by side.
    Transform NewRow(Transform parent, float height)
    {
        GameObject row = NewRect("Row", parent);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        LayoutElement element = row.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;

        return row.transform;
    }

    Button CreateButton(Transform parent, string text, float height, Action onClick)
    {
        GameObject go = NewRect(text + " Button", parent);

        Image image = go.AddComponent<Image>();
        image.color = Color.white;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = NoNavigation();
        button.onClick.AddListener(delegate { onClick(); });

        StyleButton(button, false);

        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;

        // Buttons in a column fill the width
        element.flexibleWidth = 1f;

        TMP_Text label = MenuUI.CreateLabel(
            go.transform,
            text,
            TextSize,
            TextAlignmentOptions.Center,
            MenuUI.TextColor
        );

        // Button text shrinks to fit narrow buttons
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
        label.fontSizeMax = TextSize;

        Stretch(label.rectTransform);

        return button;
    }

    // Normal buttons are dark. The chosen one is blue.
    void StyleButton(Button button, bool active)
    {
        ColorBlock colors = button.colors;

        colors.normalColor = active ? MenuUI.ButtonHighlight : MenuUI.ButtonNormal;
        colors.highlightedColor = active ? new Color(0.30f, 0.55f, 0.92f) : new Color(0.24f, 0.28f, 0.38f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = MenuUI.ButtonPressed;
        colors.fadeDuration = 0.08f;

        button.colors = colors;
    }

    Slider CreateSlider(Transform parent)
    {
        GameObject root = NewRect("Slider", parent);

        LayoutElement element = root.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minWidth = 90f;
        element.preferredHeight = 28f;

        // Thin track
        GameObject track = NewRect("Track", root.transform);

        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0.35f);
        trackRect.anchorMax = new Vector2(1f, 0.65f);
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;

        Image trackImage = track.AddComponent<Image>();
        trackImage.color = MenuUI.ButtonNormal;

        // The colored part up to the handle
        GameObject fillArea = NewRect("Fill Area", root.transform);

        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
        fillAreaRect.offsetMin = new Vector2(10f, 0f);
        fillAreaRect.offsetMax = new Vector2(-10f, 0f);

        GameObject fill = NewRect("Fill", fillArea.transform);

        // The fill must match the height of the track exactly.
        // (The slider only sets its width.)
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = MenuUI.ButtonHighlight;

        // Handle
        GameObject handleArea = NewRect("Handle Slide Area", root.transform);

        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10f, 0f);
        handleAreaRect.offsetMax = new Vector2(-10f, 0f);

        GameObject handle = NewRect("Handle", handleArea.transform);

        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0f);
        handleRect.anchorMax = new Vector2(0f, 1f);
        handleRect.sizeDelta = new Vector2(18f, 0f);

        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = MenuUI.TextColor;

        Slider slider = root.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.navigation = NoNavigation();

        return slider;
    }
}
