using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class OsowiecFieldSettings : MonoBehaviour
{
    private const string LookKey = "Osowiec.Look";
    private const string DisplayKey = "Osowiec.Fullscreen";
    // Older Windows builds stored 0 for their only available profile (PC).
    private static readonly string QualityKey = Application.platform == RuntimePlatform.WindowsPlayer
        ? "Osowiec.WindowsQualityProfile" : "Osowiec.Quality";
    private static readonly string EffectsKey = OsowiecVideoOptions.RunCheck ? "Osowiec.TestVideo.Effects":"Osowiec.Effects";
    private static readonly string ShakeKey = OsowiecVideoOptions.RunCheck ? "Osowiec.TestVideo.Shake":"Osowiec.Shake";
    private static readonly string InterfaceKey = OsowiecVideoOptions.RunCheck ? "Osowiec.TestVideo.InterfaceScale":"Osowiec.InterfaceScale";
    private const string AmbienceKey = "Osowiec.AmbienceGain";
    private const string UiAudioKey = "Osowiec.UiGain";
    [SerializeField] private Slider ambienceSound, interfaceSound;
    public static float AmbienceGain => PlayerPrefs.GetFloat(AmbienceKey,1);
    public static float InterfaceGain => PlayerPrefs.GetFloat(UiAudioKey,1);
    public void BindMix(Slider ambience, Slider ui) { ambienceSound=ambience; interfaceSound=ui; }

    const string InvertKey = "Osowiec.InvertY";
    const string CaptionsKey = "Osowiec.Captions";
    [SerializeField] Toggle invert, captions;
    public static bool InvertY => PlayerPrefs.GetInt(InvertKey,0)!=0;
    public static bool CaptionsEnabled => PlayerPrefs.GetInt(CaptionsKey,1)!=0;
    public void BindAccessibility(Toggle invertY, Toggle showCaptions)
    {invert=invertY; captions=showCaptions;}

    static readonly string FovKey = OsowiecVideoOptions.RunCheck ? "Osowiec.TestVideo.Fov":"Osowiec.Fov";
    static float? currentFov;
    public static float FieldOfView
    {
        get => currentFov ??= Mathf.Clamp(PlayerPrefs.GetFloat(FovKey, 60),50,95);
        internal set => currentFov = Mathf.Clamp(value, 50, 95);
    }
    [SerializeField] private Slider viewAngle;
    [SerializeField] private Slider look;
    [SerializeField] private Toggle fullscreen;
    [SerializeField] private Toggle reducedEffects;
    [SerializeField] private Toggle reducedShake;
    [SerializeField] private Slider graphics;
    [SerializeField] private Slider interfaceSize;
    private readonly System.Collections.Generic.Dictionary<CanvasScaler,Vector2> canvasSizes = new();

    public static float LookMultiplier => PlayerPrefs.GetFloat(LookKey, 1f);
    internal static bool? ReducedEffectsOverride { get; set; }
    public static bool ReducedEffects => ReducedEffectsOverride ?? (PlayerPrefs.GetInt(EffectsKey, 0) != 0);
    public static bool ReducedShake => PlayerPrefs.GetInt(ShakeKey, 0) != 0;
    public static float InterfaceScale => Mathf.Clamp(PlayerPrefs.GetFloat(InterfaceKey,1f),.85f,1.12f);

    public void Bind(Slider sensitivity, Toggle display, Slider quality, Toggle effects, Toggle shake, Slider textSize, Slider fov = null)
    {
        look = sensitivity;
        viewAngle = fov;
        fullscreen = display;
        graphics = quality;
        reducedEffects = effects;
        reducedShake = shake;
        interfaceSize = textSize;
    }

    internal Slider FovControl => viewAngle;
    internal Slider ScaleControl => interfaceSize;
    internal Toggle EffectsControl => reducedEffects;
    internal Toggle ShakeControl => reducedShake;
    internal const string OldDisplayKey=DisplayKey;

    void BindKitCancel()
    {
        if(!captions) return;
        var row=Instantiate(captions,captions.transform.parent);
        row.name="Отмена подготовки";
        var rect=(RectTransform)row.transform;
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,.395f);
        rect.anchoredPosition=Vector2.zero;
        var legend=captions.transform.parent.Find("Movement keys") as RectTransform;
        if(legend)
        {
            legend.anchorMin=legend.anchorMax=new Vector2(.5f,.190f);
            legend.sizeDelta=new Vector2(legend.sizeDelta.x,180f);
        }
        row.onValueChanged=new Toggle.ToggleEvent();
        if(row.graphic) row.graphic.enabled=false;
        var label=row.GetComponentInChildren<TMPro.TMP_Text>();
        if(!label) return;
        void Refresh() => label.text="Отменить бросок или прыжок      "+OsowiecKitWheel.CancelName+"  ›";
        Refresh();
        row.onValueChanged.AddListener(_=>{
            SaveInt("OsowiecKitCancel",(PlayerPrefs.GetInt("OsowiecKitCancel",0)+1)%3);
            Refresh();
        });
    }

    private void Start()
    {
        BindKitCancel();
        if(invert){
            invert.SetIsOnWithoutNotify(InvertY);
            invert.onValueChanged.AddListener(v=>SaveInt(InvertKey,v?1:0));
        }
        if(captions){
            captions.SetIsOnWithoutNotify(CaptionsEnabled);
            captions.onValueChanged.AddListener(v=>SaveInt(CaptionsKey,v?1:0));
        }
        if (ambienceSound!=null) {
            ambienceSound.SetValueWithoutNotify(AmbienceGain);
            ambienceSound.onValueChanged.AddListener(v=>SaveFloat(AmbienceKey,v));
        }
        if (interfaceSound!=null) {
            interfaceSound.SetValueWithoutNotify(InterfaceGain);
            interfaceSound.onValueChanged.AddListener(v=>SaveFloat(UiAudioKey,v));
        }
        if (viewAngle)
        {
            viewAngle.SetValueWithoutNotify(FieldOfView);
            viewAngle.onValueChanged.AddListener(v => {FieldOfView=v; SaveFloat(FovKey,FieldOfView);});
        }
        look.SetValueWithoutNotify(LookMultiplier);
        bool explicitDisplay=OsowiecVideoOptions.DisplayOverridden;
        fullscreen.SetIsOnWithoutNotify(explicitDisplay ? Screen.fullScreen :
            PlayerPrefs.GetInt(DisplayKey, Screen.fullScreen ? 1 : 0) != 0);
        graphics.SetValueWithoutNotify(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()));
        reducedEffects.SetIsOnWithoutNotify(ReducedEffects);
        reducedShake.SetIsOnWithoutNotify(ReducedShake);
        foreach (var scaler in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include))
            if (scaler.gameObject.scene == gameObject.scene && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                canvasSizes[scaler] = scaler.referenceResolution;
        ApplySize(InterfaceScale);
        if (interfaceSize != null)
        {
            interfaceSize.SetValueWithoutNotify(InterfaceScale);
            interfaceSize.onValueChanged.AddListener(value => { SaveFloat(InterfaceKey,value); ApplySize(value); });
        }
        if (!explicitDisplay && !OsowiecVideoOptions.HasSavedDisplay && PlayerPrefs.HasKey(DisplayKey))
            Screen.fullScreen = fullscreen.isOn;
        if (PlayerPrefs.HasKey(QualityKey))
            QualitySettings.SetQualityLevel((int)graphics.value, true);

        look.onValueChanged.AddListener(value => SaveFloat(LookKey, value));
        fullscreen.onValueChanged.AddListener(value =>
        {
            Screen.fullScreen = value;
            SaveInt(DisplayKey, value ? 1 : 0);
        });
        graphics.onValueChanged.AddListener(value =>
        {
            QualitySettings.SetQualityLevel((int)value, true);
            SaveInt(QualityKey, (int)value);
        });
        reducedEffects.onValueChanged.AddListener(value => SaveInt(EffectsKey, value ? 1 : 0));
        reducedShake.onValueChanged.AddListener(value => SaveInt(ShakeKey, value ? 1 : 0));
        var video=GetComponent<OsowiecVideoOptions>() ?? gameObject.AddComponent<OsowiecVideoOptions>();
        video.Initialize(this);
    }

    void ApplySize(float value)
    {
        foreach (var entry in canvasSizes)
            if(entry.Key!=null) entry.Key.referenceResolution=entry.Value/value;
    }

    private static void SaveFloat(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
    }

    private static void SaveInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    }

    public void ReturnToHeadquarters()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("OsowiecHQ");
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }
}
