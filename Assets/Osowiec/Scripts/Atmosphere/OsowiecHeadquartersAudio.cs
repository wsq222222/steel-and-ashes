using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OsowiecHeadquartersAudio : MonoBehaviour
{
    private AudioSource rain;
    private AudioSource room;
    private AudioSource desk;
    private AudioSource gramophone;
    private Transform recordPlatter;
    private AudioSource distant;
    private AudioSource boots;
    private float nextRumble;
    private Transform walker;
    private AudioLowPassFilter rainFilter;
    private Vector3 lastPosition;
    private float toStep;
    private static OsowiecHeadquartersAudio active;
    const string GramophoneKey = "Osowiec.GramophoneOn";
    public static bool GramophoneOn => PlayerPrefs.GetInt(GramophoneKey, 1) != 0;
    private static readonly bool BootTrace=System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-osowiec-audio-trace")>=0;
    public static bool ProvidesFootsteps => active != null && active.isActiveAndEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "OsowiecHQ") return;
        var station = new GameObject("Headquarters sound");
        SceneManager.MoveGameObjectToScene(station, scene);
        station.AddComponent<OsowiecHeadquartersAudio>();
    }

    private void Start()
    {
        active = this;
        rain = OsowiecSoundBank.Source(transform, "Rain outside the entrance", "Exterior", .40f, 175);
        room = OsowiecSoundBank.Source(transform, "Dugout room tone", "Interior", 0f, 180);
        desk = OsowiecSoundBank.Source(transform, "Campaign map", "UI", 0f, 70);
        boots = OsowiecSoundBank.Source(transform, "Bunker footsteps", "Foley", 0f, 75);
        gramophone = OsowiecSoundBank.Source(transform, "Gramophone horn", "Music", 1f, 110);
        var horn = GameObject.Find("FieldGramophone");
        if (horn)
        {
            gramophone.transform.position = horn.transform.position + Vector3.up*1.28f;
            recordPlatter = horn.transform.Find("RecordPlatter");
        }
        gramophone.minDistance = 1.2f;
        gramophone.maxDistance = 12f;
        gramophone.loop = true;
        gramophone.clip = OsowiecSoundBank.Clip("ShellacWaltz");
        gramophone.volume = .16f * (AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : PlayerPrefs.GetFloat("MusicVolume",1f));
        if (GramophoneOn) gramophone.Play();
        distant = OsowiecSoundBank.Source(transform, "Cannonade beyond the dugout", "World", .70f, 170);
        distant.transform.position = new Vector3(-15f,2f,16f);
        distant.minDistance = 11f;
        distant.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 620f;
        nextRumble = Time.time + 6.3f;
        var landing = GameObject.Find("Wet upper landing");
        if (landing) rain.transform.position = landing.transform.position + new Vector3(.90f,.60f,-.18f);
        rain.minDistance = 2.3f;
        rainFilter = rain.gameObject.AddComponent<AudioLowPassFilter>();
        rainFilter.cutoffFrequency = 3500f;
        rain.clip = OsowiecSoundBank.Clip("Rain");
        room.clip = OsowiecSoundBank.Clip("Room");
        rain.loop = room.loop = true;
        rain.volume = room.volume = 0f;
        OsowiecSoundBank.Space(true, 0f);
        rain.Play();
        room.Play();
    }

    private void Update()
    {
        float ambience = OsowiecFieldSettings.AmbienceGain;
        var manager = CameraManager.Instance;
        var unit = manager ? manager.GetCurrentControlledUnit() : null;
        if (unit)
        {
            var boot = unit.transform;
            Vector3 position = boot.position;
            float depth = Mathf.InverseLerp(1.6f, 8f, Vector3.Distance(position, rain.transform.position));
            rainFilter.cutoffFrequency = Mathf.Lerp(4700f, 920f, depth);
            rain.volume = ambience * Mathf.Lerp(.48f,.26f,depth);
            room.volume = ambience * Mathf.Lerp(.13f,.30f,depth);
            Vector2 travel = new(position.x-lastPosition.x,position.z-lastPosition.z);
            if (walker != boot || travel.magnitude > .8f) toStep = .37f;
            else if (travel.sqrMagnitude > .0000001f && Time.timeScale > 0f)
            {
                toStep -= travel.magnitude;
                if (toStep <= 0f) PlayStep(position);
            }
            walker = boot;
            lastPosition = position;
        }
        gramophone.volume = .27f * (AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : PlayerPrefs.GetFloat("MusicVolume",1f));
        if (recordPlatter && gramophone.isPlaying && !AudioListener.pause)
        {
            const float degreesPerSecond = 78f * 360f / 60f;
            recordPlatter.Rotate(Vector3.up, degreesPerSecond * Time.unscaledDeltaTime, Space.World);
        }
        if (Time.time >= nextRumble)
        {
            nextRumble = Time.time + Random.Range(15f,28f);
            distant.pitch = Random.Range(.74f,1.01f);
            distant.PlayOneShot(OsowiecSoundBank.Clip("Distant"),ambience*.50f);
        }
    }

    void PlayStep(Vector3 position)
    {
        // The ground ray begins inside the walker's capsule and only hits the support underneath.
        bool wet = Physics.Raycast(position + Vector3.up*.27f, Vector3.down, out var hit, .75f, ~0,
            QueryTriggerInteraction.Ignore) && hit.collider.TryGetComponent<OsowiecAcousticSurface>(out var surface)
            && surface.surface == OsowiecGroundSurface.Brick;
        toStep += wet ? .57f : .74f;
        float gain = OsowiecEnvironmentAudio.EffectsVolume;
        var clip = OsowiecSoundBank.Variant(wet ? "StepBrick" : "StepDugout", 8);
        boots.PlayOneShot(clip, gain*(wet ? .58f : .65f));
        if (wet) boots.PlayOneShot(OsowiecSoundBank.Variant("StepWater", 8), gain*.22f);
        if (BootTrace) Debug.Log($"[Osowiec foley] t={Time.time:F2} unit=HQ surface={(wet ? "Wet stone" : "Dugout")} clip={clip.name} target={position}");
    }

    public static void OpenMap()
    {
        if (active != null && active.desk != null)
            active.desk.PlayOneShot(OsowiecSoundBank.Clip("MapPaper"), OsowiecFieldSettings.InterfaceGain * .80f);
    }

    public static void ToggleGramophone()
    {
        bool play = !GramophoneOn;
        PlayerPrefs.SetInt(GramophoneKey, play ? 1 : 0);
        PlayerPrefs.Save();
        if (!active || !active.gramophone) return;
        if (play) active.gramophone.Play();
        else active.gramophone.Stop();
        active.desk.PlayOneShot(OsowiecSoundBank.Clip("MapPaper"), OsowiecFieldSettings.InterfaceGain * .16f);
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
    }
}
