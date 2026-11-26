using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OsowiecHeadquartersAudio : MonoBehaviour
{
    private AudioSource rain;
    private AudioSource room;
    private AudioSource desk;
    private AudioSource gramophone;
    private AudioSource distant;
    private AudioSource boots;
    private float nextRumble;
    private Transform listener;
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
        rain = OsowiecSoundBank.Source(transform, "Rain against roof", "Exterior", 0f, 175);
        room = OsowiecSoundBank.Source(transform, "Dugout room tone", "Interior", 0f, 180);
        desk = OsowiecSoundBank.Source(transform, "Campaign map", "UI", 0f, 70);
        boots = OsowiecSoundBank.Source(transform, "Dugout boards", "Foley", 0f, 75);
        gramophone = OsowiecSoundBank.Source(transform, "Gramophone horn", "Music", 1f, 110);
        gramophone.transform.position = new Vector3(3.18f,1.28f,2.12f);
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
        var muffled = rain.gameObject.AddComponent<AudioLowPassFilter>();
        muffled.cutoffFrequency = 1200f;
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
        float volume = OsowiecEnvironmentAudio.EffectsVolume;
        float ambience=OsowiecFieldSettings.AmbienceGain;
        rain.volume = ambience * .29f;
        room.volume = ambience * .27f;
        var camera = Camera.main;
        if (camera != null)
        {
            Vector3 pos = camera.transform.position;
            Vector2 delta = new(pos.x-lastPosition.x,pos.z-lastPosition.z);
            if (listener != camera.transform || delta.magnitude > .8f) toStep = .37f;
            else if (delta.magnitude > .000005f && Time.timeScale > 0f)
            {
                toStep -= delta.magnitude;
                if (toStep <= 0f)
                {
                    toStep += .79f;
                    var clip=OsowiecSoundBank.Variant("StepDugout",8);
                    boots.PlayOneShot(clip,volume*.60f);
                    if (BootTrace) Debug.Log($"[Osowiec foley] t={Time.time:F2} unit=HQ surface=Dugout clip={clip.name} target={pos}");
                }
            }
            listener = camera.transform;
            lastPosition = pos;
        }
        gramophone.volume = .27f * (AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : PlayerPrefs.GetFloat("MusicVolume",1f));
        if (Time.time >= nextRumble)
        {
            nextRumble = Time.time + Random.Range(15f,28f);
            distant.pitch = Random.Range(.74f,1.01f);
            distant.PlayOneShot(OsowiecSoundBank.Clip("Distant"),ambience*.50f);
        }
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
