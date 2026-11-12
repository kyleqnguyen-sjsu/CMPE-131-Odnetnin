using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif


// ============================================================
//  PHOTO MODE  (internal development tool)
//
//  This scene is NOT in the Build Settings and no menu links
//  to it, so players cannot reach it. To use it: open
//  Assets/Scenes/PhotoMode.unity in the editor and press Play.
//
//  The scene itself is almost empty (a camera and this script).
//  This script builds everything else:
//    - the free camera           (PhotoModeCamera)
//    - the effects and filters   (PhotoModeEffects)
//    - the placeable car         (PhotoModeCar)
//    - the menu                  (PhotoModeUI)
//  and loads a "background" scene (the track) behind the camera.
// ============================================================
public class PhotoModeScene : MonoBehaviour
{
    // Name of the scene file (other scripts skip their auto-spawn here)
    public const string SceneName = "PhotoMode";

    // The car to place (assigned in the scene file)
    public GameObject carPrefab;

    // Paint options for the car: Default, Blue, Red, Gray, Purple
    // (the "Color Variations" materials in the Racing Car folder)
    public Material[] carPaintMaterials;

    // The two backgrounds you can pick in the menu:
    // a blank surface, or the race track (the game's CombinedScene).
    public static readonly string[] EnvironmentNames = new string[]
    {
        "Blank surface",
        "Race track"
    };

    private static readonly string[] EnvironmentPaths = new string[]
    {
        "",
        "Assets/Scenes/CombinedScene.unity"
    };

    // Which background to load first
    private const int StartEnvironment = 1;

    // Where the camera starts in the empty studio
    private static readonly Vector3 StudioCameraPosition = new Vector3(0f, 2f, -8f);

    // ----- Things the menu uses -----

    public PhotoModeCamera CameraRig { get; private set; }
    public PhotoModeEffects Effects { get; private set; }
    public PhotoModeCar Car { get; private set; }

    public int CurrentEnvironment { get; private set; }
    public bool IsLoading { get; private set; }
    public string Status { get; private set; }

    // Tells the menu that a new background is ready
    public event Action EnvironmentChanged;

    // ----- State -----

    private Scene photoScene;
    private GameObject studioRoot;

    private Scene environmentScene;
    private bool hasEnvironmentScene;

    // Filled in by the sceneLoaded callback
    private Scene arrivedScene;
    private bool sceneArrived;

    private bool hasSceneCameraPose;
    private Vector3 sceneCameraPosition;
    private Quaternion sceneCameraRotation;


    // Players must never get this. It only runs in the editor
    // and in development builds.
    static bool IsAllowed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    void Awake()
    {
        if (!IsAllowed())
        {
            Destroy(gameObject);
            return;
        }

        photoScene = gameObject.scene;
        Status = "";

        SceneManager.sceneLoaded += OnSceneLoaded;

        Camera cam = FindOrCreateCamera();

        BuildStudio();

        CameraRig = cam.gameObject.AddComponent<PhotoModeCamera>();
        CameraRig.Init(cam);

        Car = gameObject.AddComponent<PhotoModeCar>();
        Car.Init(carPrefab, CameraRig, carPaintMaterials);

        Effects = gameObject.AddComponent<PhotoModeEffects>();
        Effects.Init(cam, Car);

        PhotoModeUI ui = gameObject.AddComponent<PhotoModeUI>();
        ui.Init(this, CameraRig, Effects, Car);
    }

    void Start()
    {
        if (CameraRig != null)
            SetEnvironment(StartEnvironment);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    Camera FindOrCreateCamera()
    {
        Camera cam = Camera.main;

        if (cam == null)
            cam = FindAnyObjectByType<Camera>();

        if (cam == null)
        {
            GameObject go = new GameObject("Photo Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            cam = go.GetComponent<Camera>();
        }

        return cam;
    }


    // ============================================================
    //  EMPTY STUDIO  (a big gray floor and a sun)
    // ============================================================

    void BuildStudio()
    {
        studioRoot = new GameObject("Studio");
        studioRoot.transform.SetParent(transform, false);

        // Floor: a plane is 10 x 10 meters, so scale 100 = 1 km
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Studio Floor";
        floor.transform.SetParent(studioRoot.transform, false);
        floor.transform.localScale = new Vector3(100f, 1f, 100f);

        floor.GetComponent<Renderer>().material.color = new Color(0.55f, 0.57f, 0.60f);

        // Sun
        GameObject sunObject = new GameObject("Studio Sun");
        sunObject.transform.SetParent(studioRoot.transform, false);
        sunObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.5f;
        sun.shadows = LightShadows.Soft;

        studioRoot.SetActive(false);
    }


    // ============================================================
    //  CHANGING THE BACKGROUND
    // ============================================================

    public void SetEnvironment(int index)
    {
        if (IsLoading || index < 0 || index >= EnvironmentPaths.Length)
            return;

        StartCoroutine(SwitchEnvironment(index));
    }

    IEnumerator SwitchEnvironment(int index)
    {
        IsLoading = true;
        Status = "Loading " + EnvironmentNames[index] + "...";
        NotifyChanged();

        // Remove the old background scene
        if (hasEnvironmentScene && environmentScene.isLoaded)
        {
            SceneManager.SetActiveScene(photoScene);

            AsyncOperation unload = SceneManager.UnloadSceneAsync(environmentScene);

            while (unload != null && !unload.isDone)
                yield return null;
        }

        hasEnvironmentScene = false;
        hasSceneCameraPose = false;

        studioRoot.SetActive(index == 0);

        bool loadedOk = true;

        if (index > 0)
        {
            yield return LoadEnvironmentScene(EnvironmentPaths[index]);

            loadedOk = hasEnvironmentScene;
        }

        // Let colliders register before we raycast against them
        yield return null;
        Physics.SyncTransforms();

        CurrentEnvironment = index;

        PlaceCameraForEnvironment(index);

        if (Car != null)
        {
            if (Car.Exists)
                Car.PlaceInFrontOfCamera();
            else
                Car.Spawn();
        }

        Effects.ApplySceneVolumeSetting();

        Status = loadedOk ? "" : "Could not load that scene (see Console)";
        IsLoading = false;

        NotifyChanged();
    }

    IEnumerator LoadEnvironmentScene(string path)
    {
        sceneArrived = false;

        AsyncOperation load = BeginLoad(path);

        if (load == null)
            yield break;

        while (!load.isDone)
            yield return null;

        yield return null;

        if (!sceneArrived)
            yield break;

        environmentScene = arrivedScene;
        hasEnvironmentScene = true;

        // Use the new scene's lighting (sky, ambient light)
        SceneManager.SetActiveScene(environmentScene);
    }

    AsyncOperation BeginLoad(string path)
    {
        try
        {
#if UNITY_EDITOR
            // Works even for scenes that are not in the Build Settings
            return EditorSceneManager.LoadSceneAsyncInPlayMode(
                path,
                new LoadSceneParameters(LoadSceneMode.Additive)
            );
#else
            // In a development build the scene must be in the build
            return SceneManager.LoadSceneAsync(
                Path.GetFileNameWithoutExtension(path),
                LoadSceneMode.Additive
            );
#endif
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Photo Mode: could not load " + path + "\n" + exception.Message);
            return null;
        }
    }

    // Runs as soon as a scene has loaded, before its Start methods
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Additive || scene == photoScene)
            return;

        PrepareEnvironment(scene);

        arrivedScene = scene;
        sceneArrived = true;
    }

    // Make a loaded game scene safe to look around in:
    // no second camera, no game logic, no game UI
    void PrepareEnvironment(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera sceneCamera in root.GetComponentsInChildren<Camera>(true))
            {
                // Remember where the level's camera was, as a starting view
                if (!hasSceneCameraPose)
                {
                    hasSceneCameraPose = true;
                    sceneCameraPosition = sceneCamera.transform.position;
                    sceneCameraRotation = sceneCamera.transform.rotation;
                }

                sceneCamera.enabled = false;
            }

            foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;

            foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                eventSystem.gameObject.SetActive(false);

            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                canvas.gameObject.SetActive(false);

            // Switch off our own game scripts (kart, timer, laps...)
            foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script != null && script.GetType().Assembly.GetName().Name == "Assembly-CSharp")
                    script.enabled = false;
            }
        }
    }

    void PlaceCameraForEnvironment(int index)
    {
        if (index == 0)
        {
            CameraRig.SetPose(StudioCameraPosition, Quaternion.identity);
        }
        else if (hasSceneCameraPose)
        {
            CameraRig.SetPose(sceneCameraPosition, sceneCameraRotation);
        }
    }

    // Jump to where the loaded scene's own camera was
    public void ResetCameraToScene()
    {
        PlaceCameraForEnvironment(CurrentEnvironment);
    }

    void NotifyChanged()
    {
        if (EnvironmentChanged != null)
            EnvironmentChanged();
    }
}
