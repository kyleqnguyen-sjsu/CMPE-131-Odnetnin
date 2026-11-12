using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


// ============================================================
//  PHOTO MODE EFFECTS
//
//  Owns everything that changes how the picture looks:
//    - a post-processing Volume (DOF, bloom, vignette, grain...)
//    - the Ambient Occlusion renderer feature
//    - the one-click filters
//
//  The menu reads and writes the public effect objects below.
//  Each effect has "active" (on/off) and its settings.
// ============================================================
public class PhotoModeEffects : MonoBehaviour
{
    // ----- Effects the menu can edit -----

    public DepthOfField dof;
    public Bloom bloom;
    public Vignette vignette;
    public FilmGrain filmGrain;
    public ChromaticAberration chromaticAberration;
    public LensDistortion lensDistortion;
    public Tonemapping tonemapping;
    public ColorAdjustments colorAdjustments;
    public WhiteBalance whiteBalance;
    public SplitToning splitToning;

    // What the depth of field focuses on
    public enum FocusMode
    {
        Manual,
        Car,
        ScreenCenter
    }

    public FocusMode focusMode = FocusMode.Car;

    // Index into Filters (0 = None)
    public int CurrentFilter { get; private set; }


    // ============================================================
    //  FILTERS  (each one is just a set of numbers)
    // ============================================================

    public class FilterPreset
    {
        public string name;

        // Color adjustments
        public float exposure;
        public float contrast;
        public float saturation;
        public float hue;
        public Color colorFilter = Color.white;

        // White balance
        public float temperature;
        public float tint;

        // Split toning (gray = no tint)
        public Color shadows = Color.gray;
        public Color highlights = Color.gray;

        // 0 means the effect is switched off
        public float vignette;
        public float grain;
        public float chromatic;
        public float bloom;
    }

    public static readonly FilterPreset[] Filters = new FilterPreset[]
    {
        new FilterPreset
        {
            name = "None"
        },

        new FilterPreset
        {
            name = "Vivid",
            exposure = 0.1f, contrast = 15f, saturation = 30f,
            bloom = 0.3f
        },

        new FilterPreset
        {
            name = "Cinematic",
            contrast = 20f, saturation = -12f, temperature = 8f,
            shadows = new Color(0.25f, 0.45f, 0.55f),
            highlights = new Color(0.8f, 0.6f, 0.4f),
            vignette = 0.3f, grain = 0.15f, bloom = 0.2f
        },

        new FilterPreset
        {
            name = "Vintage",
            exposure = 0.1f, contrast = -8f, saturation = -30f,
            temperature = 25f, tint = 10f,
            colorFilter = new Color(1f, 0.95f, 0.85f),
            vignette = 0.4f, grain = 0.35f
        },

        new FilterPreset
        {
            name = "Noir",
            contrast = 35f, saturation = -100f,
            vignette = 0.45f, grain = 0.3f
        },

        new FilterPreset
        {
            name = "Cold",
            contrast = 8f, saturation = 5f,
            temperature = -35f, tint = -5f
        },

        new FilterPreset
        {
            name = "Sunset",
            exposure = 0.2f, saturation = 15f,
            temperature = 35f, tint = 8f,
            shadows = new Color(0.55f, 0.35f, 0.5f),
            bloom = 0.4f
        },

        new FilterPreset
        {
            name = "Faded",
            exposure = 0.35f, contrast = -25f, saturation = -25f,
            colorFilter = new Color(0.95f, 0.97f, 1f)
        },

        new FilterPreset
        {
            name = "Cyberpunk",
            contrast = 15f, saturation = 25f,
            shadows = new Color(0.45f, 0.2f, 0.7f),
            highlights = new Color(0.2f, 0.8f, 0.9f),
            vignette = 0.25f, chromatic = 0.2f, bloom = 1f
        },

        new FilterPreset
        {
            name = "Dramatic",
            exposure = -0.2f, contrast = 40f, saturation = -15f,
            vignette = 0.5f, grain = 0.1f
        }
    };


    // ============================================================
    //  INTERNAL STATE
    // ============================================================

    private Camera cam;
    private PhotoModeCar car;
    private Volume volume;
    private VolumeProfile profile;
    private float smoothFocusDistance = 10f;
    private bool sceneVolumesEnabled = true;

    // Ambient occlusion (a renderer feature, not a volume effect)
    private ScriptableRendererFeature ssao;
    private FieldInfo ssaoSettingsField;
    private object ssaoSettings;

    private bool originalSsaoActive;
    private float originalIntensity;
    private float originalRadius;
    private float originalDirectStrength;


    // ============================================================
    //  SETUP
    // ============================================================

    public void Init(Camera camera, PhotoModeCar photoCar)
    {
        cam = camera;
        car = photoCar;

        // Post-processing must be on for the camera
        UniversalAdditionalCameraData cameraData =
            cam.GetUniversalAdditionalCameraData();

        cameraData.renderPostProcessing = true;
        cameraData.volumeLayerMask = ~0;

        BuildVolume();
        FindAmbientOcclusion();
    }

    void BuildVolume()
    {
        GameObject go = new GameObject("Photo Mode Volume");
        go.transform.SetParent(transform, false);

        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 1f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;

        dof = AddEffect<DepthOfField>(false);
        bloom = AddEffect<Bloom>(false);
        vignette = AddEffect<Vignette>(false);
        filmGrain = AddEffect<FilmGrain>(false);
        chromaticAberration = AddEffect<ChromaticAberration>(false);
        lensDistortion = AddEffect<LensDistortion>(false);
        tonemapping = AddEffect<Tonemapping>(false);

        // These start on, with "no change" values
        colorAdjustments = AddEffect<ColorAdjustments>(true);
        whiteBalance = AddEffect<WhiteBalance>(true);
        splitToning = AddEffect<SplitToning>(true);

        SetDefaultValues();
    }

    T AddEffect<T>(bool startActive) where T : VolumeComponent
    {
        // "true" = every setting of the effect is overridden by us
        T effect = profile.Add<T>(true);

        effect.active = startActive;

        return effect;
    }

    // Starting numbers for the effects that need something sensible
    void SetDefaultValues()
    {
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(10f);
        dof.aperture.Override(5.6f);
        dof.focalLength.Override(50f);
        dof.gaussianStart.Override(10f);
        dof.gaussianEnd.Override(30f);
        dof.gaussianMaxRadius.Override(1f);

        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.4f);
        bloom.scatter.Override(0.7f);

        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.4f);

        filmGrain.intensity.Override(0.3f);
        filmGrain.response.Override(0.8f);

        chromaticAberration.intensity.Override(0.15f);

        lensDistortion.intensity.Override(-0.2f);

        tonemapping.mode.Override(TonemappingMode.ACES);
    }


    // ============================================================
    //  EVERY FRAME  (autofocus)
    // ============================================================

    void Update()
    {
        if (cam == null || dof == null || !dof.active)
            return;

        if (focusMode == FocusMode.Manual)
            return;

        float distance;

        if (!TryGetFocusDistance(out distance))
            return;

        // Glide to the new distance instead of snapping
        float t = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);

        smoothFocusDistance = Mathf.Lerp(smoothFocusDistance, distance, t);

        ApplyFocusDistance(smoothFocusDistance);
    }

    bool TryGetFocusDistance(out float distance)
    {
        distance = 0f;

        if (focusMode == FocusMode.Car)
        {
            if (car == null || !car.Exists)
                return false;

            distance = Vector3.Distance(cam.transform.position, car.Center);
            return true;
        }

        // Screen center: focus on whatever is in the middle
        RaycastHit hit;

        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 500f))
        {
            distance = hit.distance;
            return true;
        }

        return false;
    }

    void ApplyFocusDistance(float distance)
    {
        dof.focusDistance.Override(Mathf.Max(0.1f, distance));

        // Gaussian blur starts a little behind the subject.
        // Keep the width of the blur zone the user chose.
        float width = dof.gaussianEnd.value - dof.gaussianStart.value;

        dof.gaussianStart.Override(distance + 2f);
        dof.gaussianEnd.Override(distance + 2f + Mathf.Max(1f, width));
    }


    // ============================================================
    //  FILTERS
    // ============================================================

    // 0 = no filter, 1 = full filter. Blends between the two.
    public float FilterStrength
    {
        get { return filterStrength; }
        set
        {
            filterStrength = Mathf.Clamp01(value);
            ApplyFilter(CurrentFilter);
        }
    }

    private float filterStrength = 1f;

    public void ApplyFilter(int index)
    {
        index = Mathf.Clamp(index, 0, Filters.Length - 1);

        FilterPreset p = Filters[index];
        float s = filterStrength;

        CurrentFilter = index;

        colorAdjustments.active = true;
        colorAdjustments.postExposure.Override(p.exposure * s);
        colorAdjustments.contrast.Override(p.contrast * s);
        colorAdjustments.saturation.Override(p.saturation * s);
        colorAdjustments.hueShift.Override(p.hue * s);
        colorAdjustments.colorFilter.Override(Color.Lerp(Color.white, p.colorFilter, s));

        whiteBalance.active = true;
        whiteBalance.temperature.Override(p.temperature * s);
        whiteBalance.tint.Override(p.tint * s);

        splitToning.active = true;
        splitToning.shadows.Override(Color.Lerp(Color.gray, p.shadows, s));
        splitToning.highlights.Override(Color.Lerp(Color.gray, p.highlights, s));
        splitToning.balance.Override(0f);

        // These switch on only when the filter uses them
        SetOptional(vignette, p.vignette * s, vignette.intensity);
        SetOptional(filmGrain, p.grain * s, filmGrain.intensity);
        SetOptional(chromaticAberration, p.chromatic * s, chromaticAberration.intensity);
        SetOptional(bloom, p.bloom * s, bloom.intensity);
    }

    // Switch an effect on when the filter uses it, and off when it does not
    void SetOptional(VolumeComponent effect, float amount, ClampedFloatParameter parameter)
    {
        effect.active = amount > 0.001f;

        if (effect.active)
            parameter.Override(amount);
    }

    void SetOptional(VolumeComponent effect, float amount, MinFloatParameter parameter)
    {
        effect.active = amount > 0.001f;

        if (effect.active)
            parameter.Override(amount);
    }

    // Everything back to its starting value
    public void ResetAll()
    {
        SetDefaultValues();

        dof.active = false;
        bloom.active = false;
        vignette.active = false;
        filmGrain.active = false;
        chromaticAberration.active = false;
        lensDistortion.active = false;
        tonemapping.active = false;

        focusMode = FocusMode.Car;

        filterStrength = 1f;
        ApplyFilter(0);

        if (ssao != null)
            RestoreAmbientOcclusion();
    }


    // ============================================================
    //  OTHER VOLUMES IN THE LOADED SCENE
    // ============================================================

    // Turn the loaded scene's own post-processing on or off
    public bool SceneVolumesEnabled
    {
        get { return sceneVolumesEnabled; }
        set
        {
            sceneVolumesEnabled = value;
            ApplySceneVolumeSetting();
        }
    }

    // Call again after a new scene has loaded
    public void ApplySceneVolumeSetting()
    {
        Volume[] volumes = FindObjectsByType<Volume>();

        foreach (Volume other in volumes)
        {
            if (other != volume)
                other.enabled = sceneVolumesEnabled;
        }
    }


    // ============================================================
    //  AMBIENT OCCLUSION
    //  URP has no volume setting for this. It is a "renderer
    //  feature" on the renderer asset, so we find it and change
    //  its settings with reflection (those fields are internal).
    // ============================================================

    public bool AoAvailable
    {
        get { return ssao != null; }
    }

    public bool AoEnabled
    {
        get { return ssao != null && ssao.isActive; }
        set
        {
            if (ssao != null)
                ssao.SetActive(value);
        }
    }

    public float AoIntensity
    {
        get { return GetAoValue("Intensity"); }
        set { SetAoValue("Intensity", value); }
    }

    public float AoRadius
    {
        get { return GetAoValue("Radius"); }
        set { SetAoValue("Radius", value); }
    }

    public float AoDirectLighting
    {
        get { return GetAoValue("DirectLightingStrength"); }
        set { SetAoValue("DirectLightingStrength", value); }
    }

    void FindAmbientOcclusion()
    {
        UniversalRenderPipelineAsset asset =
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (asset == null)
            return;

        FieldInfo listField = typeof(UniversalRenderPipelineAsset).GetField(
            "m_RendererDataList",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (listField == null)
            return;

        ScriptableRendererData[] rendererDataList =
            listField.GetValue(asset) as ScriptableRendererData[];

        if (rendererDataList == null)
            return;

        foreach (ScriptableRendererData data in rendererDataList)
        {
            if (data == null)
                continue;

            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion")
                {
                    ssao = feature;
                    break;
                }
            }

            if (ssao != null)
                break;
        }

        if (ssao == null)
            return;

        ssaoSettingsField = ssao.GetType().GetField(
            "m_Settings",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
        );

        if (ssaoSettingsField == null)
        {
            ssao = null;
            return;
        }

        ssaoSettings = ssaoSettingsField.GetValue(ssao);

        // Remember how it was, so we can put it back
        originalSsaoActive = ssao.isActive;
        originalIntensity = GetAoValue("Intensity");
        originalRadius = GetAoValue("Radius");
        originalDirectStrength = GetAoValue("DirectLightingStrength");
    }

    float GetAoValue(string fieldName)
    {
        if (ssaoSettings == null)
            return 0f;

        FieldInfo field = ssaoSettings.GetType().GetField(
            fieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (field == null)
            return 0f;

        return (float)field.GetValue(ssaoSettings);
    }

    void SetAoValue(string fieldName, float value)
    {
        if (ssaoSettings == null)
            return;

        FieldInfo field = ssaoSettings.GetType().GetField(
            fieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (field == null)
            return;

        field.SetValue(ssaoSettings, value);

        // Write it back (needed if the settings are a struct)
        ssaoSettingsField.SetValue(ssao, ssaoSettings);
    }

    void RestoreAmbientOcclusion()
    {
        ssao.SetActive(originalSsaoActive);

        SetAoValue("Intensity", originalIntensity);
        SetAoValue("Radius", originalRadius);
        SetAoValue("DirectLightingStrength", originalDirectStrength);
    }

    // The renderer asset is shared with the real game, so leave it
    // exactly how we found it
    void OnDestroy()
    {
        if (ssao != null)
            RestoreAmbientOcclusion();

        if (profile != null)
            Destroy(profile);
    }
}
