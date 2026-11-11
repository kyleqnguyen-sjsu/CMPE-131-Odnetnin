using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Small helpers that build menu UI from code.
// Shared by the Main Menu and the Pause Menu so both look the same.
public static class MenuUI
{
    // ---------- Look ----------

    public static readonly Color BackdropColor = new Color(0.06f, 0.07f, 0.10f, 1f);
    public static readonly Color PauseDimColor = new Color(0.03f, 0.04f, 0.07f, 0.85f);

    public static readonly Color ButtonNormal = new Color(0.16f, 0.18f, 0.24f, 1f);
    public static readonly Color ButtonHighlight = new Color(0.20f, 0.45f, 0.85f, 1f);
    public static readonly Color ButtonPressed = new Color(0.12f, 0.30f, 0.65f, 1f);

    public static readonly Color TextColor = Color.white;
    public static readonly Color MutedTextColor = new Color(0.78f, 0.81f, 0.88f, 1f);

    public const float ColumnWidth = 680f;
    public const float ButtonHeight = 64f;
    public const float Spacing = 16f;


    // ---------- Canvas and EventSystem ----------

    public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent)
    {
        GameObject go = new GameObject(
            name,
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        go.transform.SetParent(parent, false);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    // Menus need an EventSystem for mouse and keyboard navigation.
    // Reuses the scene's one if it already has it.
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        if (Object.FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();

        InputSystemUIInputModule module =
            go.AddComponent<InputSystemUIInputModule>();

        module.AssignDefaultActions();
    }

    public static void Select(Selectable target)
    {
        if (EventSystem.current == null || target == null)
            return;

        EventSystem.current.SetSelectedGameObject(target.gameObject);
    }


    // ---------- Layout pieces ----------

    public static RectTransform CreateFullScreenPanel(
        Transform parent,
        string name,
        Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        go.GetComponent<Image>().color = color;

        return rect;
    }

    // A page is a centered vertical column. Children stack top to bottom.
    public static RectTransform CreatePage(Transform parent, string name)
    {
        GameObject go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );

        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(ColumnWidth, 0f);

        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = Spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rect;
    }

    public static void CreateSpacer(Transform parent, float height)
    {
        GameObject go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
    }

    public static TMP_Text CreateLabel(
        Transform parent,
        string text,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles style = FontStyles.Normal)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.fontStyle = style;
        label.raycastTarget = false;

        return label;
    }

    public static Button CreateButton(
        Transform parent,
        string text,
        UnityAction onClick)
    {
        GameObject go = new GameObject(
            text + " Button",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement),
            typeof(MenuHoverSelect)
        );

        go.transform.SetParent(parent, false);

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.preferredHeight = ButtonHeight;
        element.minHeight = ButtonHeight;

        Image image = go.GetComponent<Image>();
        image.color = Color.white;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = ButtonNormal;
        colors.highlightedColor = ButtonHighlight;
        colors.selectedColor = ButtonHighlight;
        colors.pressedColor = ButtonPressed;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        button.onClick.AddListener(onClick);

        // Button text fills the button
        TMP_Text label = CreateLabel(
            go.transform,
            text,
            30f,
            TextAlignmentOptions.Center,
            TextColor
        );

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }


    // ---------- Shared pages ----------

    public static TMP_Text CreateTitle(Transform page, string text, float size = 56f)
    {
        return CreateLabel(
            page,
            text,
            size,
            TextAlignmentOptions.Center,
            TextColor,
            FontStyles.Bold
        );
    }

    // Controls page used by both menus. Returns the Back button.
    public static Button BuildControlsPage(
        RectTransform page,
        float pauseHoldSeconds,
        float restartHoldSeconds,
        UnityAction onBack)
    {
        CreateTitle(page, "Controls");
        CreateSpacer(page, 8f);

        string body =
            Row("W", "Accelerate") +
            Row("S", "Brake / Reverse") +
            Row("A / D", "Steer left / right") +
            Row("Space", "Drift (hold while turning)") +
            Row("C", "Switch camera view") +
            Row("P", "Hold " + pauseHoldSeconds.ToString("0") + "s to pause") +
            Row("R", "Hold " + restartHoldSeconds.ToString("0") + "s to restart") +
            Row("Esc", "Back (in menus)");

        TMP_Text text = CreateLabel(
            page,
            body,
            30f,
            TextAlignmentOptions.TopLeft,
            MutedTextColor
        );

        text.lineSpacing = 20f;

        CreateSpacer(page, 8f);

        return CreateButton(page, "Back", onBack);
    }

    private static string Row(string key, string action)
    {
        return "<b><color=#FFFFFF>" + key + "</color></b><pos=30%>" + action + "\n";
    }
}
