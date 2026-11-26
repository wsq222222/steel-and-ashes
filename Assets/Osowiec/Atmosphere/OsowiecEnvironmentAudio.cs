using UnityEngine;

[DisallowMultipleComponent]
public sealed class OsowiecEnvironmentAudio : MonoBehaviour
{
    private readonly AudioSource[] impactVoices = new AudioSource[4];
    private int nextImpact;
    private AudioSource strap, door, signal, breathing, rain, room, drips, distance, warning;
    private OsowiecFieldScore score;
    private AudioLowPassFilter weatherFilter;
    private Bounds vault;
    private Unit tracked;
    private float breathGain, nextDrip, nextDistance;
    private bool mask, indoors;
    private AudioSource ring;
    private float shockUntil, shockGain;
    public void CloseBlast(Vector3 position)
    {
        var cameras=CameraManager.Instance;
        if(!cameras || !cameras.IsActionMode()) return;
        float gain=Mathf.Clamp01(1f-Vector3.Distance(cameras.GetActionCamera().transform.position,position)/8f);
        if(gain<=0) return;
        shockUntil=Time.time+1.25f; shockGain=gain;
        if(!ring) ring=OsowiecSoundBank.Source(transform,"Near blast ring","Foley",0,70);
        ring.Stop(); ring.PlayOneShot(OsowiecSoundBank.Clip("CloseBlastRing"),EffectsVolume*.47f*gain);
    }

    public static float EffectsVolume => Mathf.Clamp01(AudioManager.Instance != null
        ? AudioManager.Instance.SFXVolume : PlayerPrefs.GetFloat("SFXVolume", 1f));

    private void Awake()
    {
        strap = OsowiecSoundBank.Source(transform, "Mask straps", "Foley", 0f, 78);
        score = gameObject.AddComponent<OsowiecFieldScore>();
        warning = OsowiecSoundBank.Source(transform, "Incoming shell cue", "World", .65f, 50);
        warning.minDistance=9; warning.maxDistance=90;
        door = OsowiecSoundBank.Source(transform, "Casemate gate", "World", 1f, 78);
        signal = OsowiecSoundBank.Source(transform, "Battery signal", "UI", 0f, 52);
        breathing = OsowiecSoundBank.Source(transform, "Respirator", "Foley", 0f, 68);
        rain = OsowiecSoundBank.Source(transform, "Rain across the marsh", "Exterior", 0f, 179);
        room = OsowiecSoundBank.Source(transform, "Dripping vault", "Interior", 0f, 177);
        drips = OsowiecSoundBank.Source(transform, "Water at portal", "World", 1f, 165);
        distance = OsowiecSoundBank.Source(transform, "Distant horizon", "Exterior", .82f, 182);
        for (int i = 0; i < impactVoices.Length; i++)
            impactVoices[i] = OsowiecSoundBank.Source(transform, "Surface impact", "World", 1f, 120);
        distance.minDistance = 17f;
        distance.maxDistance = 95f;
        weatherFilter = rain.gameObject.AddComponent<AudioLowPassFilter>();
        rain.clip = OsowiecSoundBank.Clip("Rain");
        room.clip = OsowiecSoundBank.Clip("Room");
        breathing.clip = OsowiecSoundBank.Clip("Breathing");
        rain.loop = room.loop = breathing.loop = true;
        rain.volume = room.volume = breathing.volume = 0f;
        rain.Play();
        room.Play();
        OsowiecSoundBank.Space(false, 0f);
        nextDrip = Time.time + 1.8f;
        nextDistance = Time.time + 12.6f;
    }

    public void SetMask(bool active)
    {
        if (mask != active && tracked != null && UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame)
            strap.PlayOneShot(OsowiecSoundBank.Clip("MaskStrap"), EffectsVolume * .58f);
        mask = active;
    }

    public void PlayDoor(Vector3 position)
    {
        door.transform.position = position;
        door.PlayOneShot(OsowiecSoundBank.Clip("Door"), EffectsVolume * .88f);
    }

    public void PlayCapture() => signal.PlayOneShot(OsowiecSoundBank.Clip("Capture"), OsowiecFieldSettings.InterfaceGain * .72f);

    public void PlayWarning(Vector3 position)
    {
        score.DuckWarning();
        if (warning.isPlaying) return;
        warning.transform.position=position+Vector3.up*2;
        warning.PlayOneShot(OsowiecSoundBank.Clip("ShellWarning"),EffectsVolume*.83f);
    }
    public void ReportEnemyContact() => score.MarkContact();

    public void PlayImpact(Collider collider, Vector3 position)
    {
        var surface = collider != null ? collider.GetComponentInParent<OsowiecAcousticSurface>() : null;
        string clip = surface != null && surface.surface == OsowiecGroundSurface.Timber ? "PlankChip" :
            InVault(position) || (surface != null && surface.surface == OsowiecGroundSurface.Brick) ? "BrickChip" : "MudHit";
        PlayWorldCue(clip, position, .56f);
    }

    public void PlayWorldCue(string clip, Vector3 position, float volume)
    {
        var voice = impactVoices[nextImpact];
        nextImpact = (nextImpact + 1) % impactVoices.Length;
        voice.Stop();
        voice.transform.position = position;
        voice.pitch = Random.Range(.91f, 1.08f);
        voice.PlayOneShot(OsowiecSoundBank.Clip(clip), EffectsVolume * volume);
    }

    public void MixSpace(Vector3 observer, bool overhead, bool underCover, Bounds cover)
    {
        vault = cover;
        if (underCover != indoors)
        {
            indoors = underCover;
            OsowiecSoundBank.Space(underCover, .73f);
        }
        float blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 3.5f);
        float volume = OsowiecFieldSettings.AmbienceGain;
        rain.volume = Mathf.Lerp(rain.volume, volume * (overhead ? .22f : .43f), blend);
        room.volume = Mathf.Lerp(room.volume, volume * (overhead ? .16f : .52f), blend);
        float cutoff = underCover ? 1300f : 10500f;
        if (!overhead && mask) cutoff *= .54f;
        if (!overhead && Time.time<shockUntil)
            cutoff=Mathf.Lerp(cutoff,580f,Mathf.Clamp01((shockUntil-Time.time)/.65f)*shockGain);
        weatherFilter.cutoffFrequency = Mathf.Lerp(weatherFilter.cutoffFrequency, cutoff, blend);
        if (Time.time > nextDrip && !overhead)
        {
            nextDrip = Time.time + Random.Range(3.1f, 6.8f);
            var portal = new Vector3(observer.x < cover.center.x ? cover.min.x : cover.max.x,
                                     cover.max.y - .55f, cover.center.z);
            if (underCover && !cover.Contains(observer)) portal=observer+new Vector3(1.15f,.45f,1.3f);
            drips.transform.position = portal;
            drips.PlayOneShot(OsowiecSoundBank.Variant("Drop", 3), volume * .56f);
        }
        if (Time.time > nextDistance)
        {
            nextDistance = Time.time + Random.Range(24f, 52f);
            distance.transform.position = new Vector3(cover.max.x + 31f, 3f, cover.center.z + 27f);
            distance.PlayOneShot(OsowiecSoundBank.Clip("Distant"), volume * (overhead ? .27f : .54f));
        }
    }

    public bool InVault(Vector3 position) => vault.Contains(position + Vector3.up * .3f) ||
        OsowiecFortCutaway.Sheltered(position + Vector3.up * .3f);

    private void LateUpdate()
    {
        var game = GameManager.Instance;
        var cameras = CameraManager.Instance;
        var unit = cameras != null && cameras.IsActionMode() ? cameras.GetCurrentControlledUnit() : null;
        if (game == null || game.IsPaused() || game.IsGameOver() || unit == null ||
            unit.owner != Player.Player1 || unit.GetHealth() <= 0) unit = null;

        float fatigue = unit && !OsowiecMarch.Free(unit) ? unit.FieldTurn.Breath(OsowiecPlanner.FullBudget(unit)) : 0f;
        float exertion = Mathf.InverseLerp(.50f, .97f, fatigue);
        float targetGain = unit ? Mathf.Max(mask ? .60f : 0f, .62f * exertion) : 0f;
        bool activeBreath = targetGain > .001f;
        breathing.pitch = 1f + .29f * exertion;
        if (activeBreath && !breathing.isPlaying) breathing.Play();
        breathGain = Mathf.MoveTowards(breathGain, targetGain, Time.unscaledDeltaTime * 2.1f);
        breathing.volume = breathGain * EffectsVolume;
        if (!activeBreath && breathGain <= .001f && breathing.isPlaying) breathing.Stop();

        tracked=unit;
    }
}
