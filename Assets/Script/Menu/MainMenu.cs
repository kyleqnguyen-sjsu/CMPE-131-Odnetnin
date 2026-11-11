using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main menu. Builds its own UI at runtime, so the scene only
// needs one GameObject with this script on it.
public class MainMenu : MonoBehaviour
{
    public string gameSceneName = "SampleScene";

    private const string PageMain = "Main";
    private const string PageControls = "Controls";
    private const string PageSettings = "Settings";
    private const string PageCredits = "Credits";

    // Name and role shown on the Credits page, in display order
    private static readonly string[,] Credits =
    {
        { "Kyle Nguyen",          "Project Manager" },
        { "Johnny Rollins",       "Project Marketing Manager" },
        { "Hemang Mehra",         "System Architect" },
        { "Phuong Le Duy Trinh",  "Developer" },
        { "Thanh Nguyen",         "Developer" },
        { "Jason Yeh",            "QA Engineer" },
    };

    private MenuPages pages;


    void Awake()
    {
        // Coming back from a paused race, make sure nothing is frozen
        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        EnsureCamera();
        BuildUI();
    }

    void Update()
    {
        if (pages == null)
            return;

        pages.RestoreSelectionIfLost();

        if (Keyboard.current == null)
            return;

        // Esc goes back to the main page
        if (Keyboard.current.escapeKey.wasPressedThisFrame && !pages.IsAtRoot)
            pages.ShowRoot();
    }


    // ---------- Setup ----------

    // The menu scene has no camera, so make one for the background color
    void EnsureCamera()
    {
        if (FindAnyObjectByType<Camera>() != null)
            return;

        GameObject go = new GameObject("Menu Camera");
        go.tag = "MainCamera";

        Camera camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = MenuUI.BackdropColor;

        go.AddComponent<AudioListener>();
    }

    void BuildUI()
    {
        Canvas canvas = MenuUI.CreateCanvas("Main Menu Canvas", 0, transform);
        MenuUI.EnsureEventSystem();

        MenuUI.CreateFullScreenPanel(
            canvas.transform,
            "Backdrop",
            MenuUI.BackdropColor
        );

        pages = new MenuPages(PageMain);

        BuildMainPage(canvas.transform);
        BuildControlsPage(canvas.transform);
        BuildSettingsPage(canvas.transform);
        BuildCreditsPage(canvas.transform);

        pages.ShowRoot();
    }


    // ---------- Pages ----------

    void BuildMainPage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Main Page");

        MenuUI.CreateTitle(page, "SJSU KART", 84f);
        MenuUI.CreateSpacer(page, 40f);

        Button start = MenuUI.CreateButton(page, "Start", StartGame);

        MenuUI.CreateButton(page, "Controls", () => pages.Show(PageControls));
        MenuUI.CreateButton(page, "Settings", () => pages.Show(PageSettings));
        MenuUI.CreateButton(page, "Credits", () => pages.Show(PageCredits));

        pages.Add(PageMain, page.gameObject, start);
    }

    void BuildControlsPage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Controls Page");

        Button back = MenuUI.BuildControlsPage(
            page,
            PauseMenu.HoldSeconds,
            PauseMenu.RestartHoldSeconds,
            () => pages.ShowRoot()
        );

        pages.Add(PageControls, page.gameObject, back);
    }

    void BuildSettingsPage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Settings Page");

        MenuUI.CreateTitle(page, "Settings");
        MenuUI.CreateSpacer(page, 16f);

        // TODO: volume controls go here

        MenuUI.CreateSpacer(page, 16f);
        Button back = MenuUI.CreateButton(page, "Back", () => pages.ShowRoot());

        pages.Add(PageSettings, page.gameObject, back);
    }

    void BuildCreditsPage(Transform parent)
    {
        RectTransform page = MenuUI.CreatePage(parent, "Credits Page");

        MenuUI.CreateTitle(page, "Credits");
        MenuUI.CreateSpacer(page, 16f);

        MenuUI.CreateLabel(
            page,
            BuildCreditsText(),
            28f,
            TextAlignmentOptions.TopLeft,
            MenuUI.MutedTextColor
        ).lineSpacing = 20f;

        // TODO: asset credits (car, track, sounds) go here

        MenuUI.CreateSpacer(page, 16f);
        Button back = MenuUI.CreateButton(page, "Back", () => pages.ShowRoot());

        pages.Add(PageCredits, page.gameObject, back);
    }


    string BuildCreditsText()
    {
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < Credits.GetLength(0); i++)
        {
            sb.AppendLine(
                "<b><color=#FFFFFF>" + Credits[i, 0] + "</color></b>" +
                "<pos=42%>" + Credits[i, 1]
            );
        }

        return sb.ToString();
    }


    // ---------- Actions ----------

    void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}
