using UnityEngine;
using UnityEngine.InputSystem;

public sealed class OsowiecVisorState
{
    const float Duration = .46f;
    float wipeLeft;
    public float Fog { get; private set; }
    public float WipeProgress => wipeLeft > 0f ? 1f - wipeLeft / Duration : 0f;

    public bool Step(bool masked, bool moving, bool startWipe, float dt)
    {
        if (!masked)
        {
            Fog = Mathf.MoveTowards(Fog,0,dt*.5f);
            wipeLeft = 0;
            return false;
        }
        bool began = startWipe && wipeLeft <= 0;
        if (began) wipeLeft = Duration;
        if (wipeLeft > 0)
        {
            wipeLeft = Mathf.Max(0,wipeLeft-dt);
            if (wipeLeft == 0) Fog = 0;
        }
        else Fog = Mathf.MoveTowards(Fog,1f,dt*(moving ? .062f : .029f));
        return began;
    }
}

/// Presentation of close impacts, breath on glass and the hand sweep. Per-soldier amounts live in the mission.
public sealed class OsowiecStressEffects : MonoBehaviour
{
    Texture2D vignette, mist;
    AudioSource interior;
    float pulseUntil, impactEnvelope, nextPulse;
    Unit heardBy;
    OsowiecOperation operation;

    void Awake()
    {
        operation = GetComponent<OsowiecOperation>();
        interior = OsowiecSoundBank.Source(transform, "Impact and lens contact", "Foley", 0, 55);
        vignette = CreateFilm(false);
        mist = CreateFilm(true);
    }

    void OnDestroy()
    {
        if (vignette != null) Destroy(vignette);
        if (mist != null) Destroy(mist);
    }

    public static float DistanceToPath(Vector3 observer, Vector3 start, Vector3 end, out Vector3 closest)
    {
        Vector3 delta = end - start;
        float t = delta.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(observer - start, delta) / delta.sqrMagnitude) : 0f;
        closest = start + delta * t;
        return Vector3.Distance(observer, closest);
    }

    public static void PassBy(Unit firer, Vector3 start, Vector3 end)
    {
        var mission = OsowiecOperation.Instance;
        var actor = mission != null ? mission.LocalViewUnit() : null;
        if (actor == null || firer == null || firer.owner == actor.owner || actor == firer) return;
        Vector3 ear = actor.transform.position + Vector3.up * 1.56f;
        float distance = DistanceToPath(ear, start, end, out var closest);
        if (distance > 3.8f || !CanHear(actor, closest, ear)) return;
        mission.OnNearbyImpact(actor, .29f * (1 - distance / 3.8f));
    }

    public static void Earthfall(Vector3 position)
    {
        var mission = OsowiecOperation.Instance;
        var actor = mission != null ? mission.LocalViewUnit() : null;
        if (actor == null) return;
        var ear = actor.transform.position + Vector3.up * 1.56f;
        var source = position + Vector3.up * 1.0f;
        float range = Vector3.Distance(ear, source);
        if (range < 9f && CanHear(actor, source, ear))
            mission.OnNearbyImpact(actor, .38f * (1f - range / 9f));
    }

    static bool CanHear(Unit actor, Vector3 source, Vector3 ear)
    {
        Vector3 delta = ear - source;
        if (delta.magnitude < .6f) return true;
        var from = source + delta.normalized * .09f;
        return !Physics.Linecast(from, ear, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
            hit.collider.transform.IsChildOf(actor.transform);
    }

    public void Impact(Unit local, float intensity)
    {
        if (Time.time < nextPulse || intensity < .065f) return;
        nextPulse = Time.time + .18f;
        pulseUntil = Time.time + .44f;
        impactEnvelope = Mathf.Clamp01(intensity * 2.4f);
        heardBy = local;
        float gain = OsowiecFieldSettings.ReducedEffects ? .26f : .44f;
        interior.PlayOneShot(OsowiecSoundBank.Clip("PressurePulse"),
            OsowiecEnvironmentAudio.EffectsVolume * gain * Mathf.Lerp(.45f, 1f, impactEnvelope));
    }

    public void Wipe()
    {
        interior.PlayOneShot(OsowiecSoundBank.Clip("MaskWipe"), OsowiecEnvironmentAudio.EffectsVolume * .56f);
    }

    void LateUpdate()
    {
        if (operation == null || operation.LocalViewUnit() == null)
        {
            if (interior.isPlaying) interior.Stop();
            heardBy = null;
            impactEnvelope = 0;
        }
    }

    public void Draw(Unit actor, bool maskOn, float pressure, float fog, float wipe)
    {
        if (Event.current.type != EventType.Repaint) return;
        var old = GUI.color;
        bool low = OsowiecFieldSettings.ReducedEffects;
        float pulse = actor == heardBy && !OsowiecFieldSettings.ReducedShake
            ? Mathf.Clamp01((pulseUntil - Time.time) / .44f) * impactEnvelope : 0f;
        float edge = Mathf.Clamp01(pressure * .75f + pulse * .20f) * (low ? .42f : 1f);
        if (edge > .004f)
        {
            GUI.color = new Color(1f, 1f, 1f, edge);
            GUI.DrawTexture(new Rect(0,0,Screen.width, Screen.height), vignette);
        }
        if (maskOn && fog > .002f)
        {
            GUI.color = new Color(1f,1f,1f, fog * (low ? .32f : .68f));
            float x = Mathf.Clamp01(wipe);
            GUI.DrawTextureWithTexCoords(new Rect(Screen.width*x,0,Screen.width*(1-x),Screen.height),
                mist, new Rect(x,0,1-x,1));
        }
        GUI.color = old;
    }

    static Texture2D CreateFilm(bool condensation)
    {
        const int width=320, height=176;
        var texture = new Texture2D(width,height,TextureFormat.RGBA32,false) {
            name = condensation ? "Moisture on mask glass" : "Close impact shadow",
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[width*height];
        for (int y=0; y<height; y++)
        for (int x=0; x<width; x++)
        {
            float u=(x+.5f)/width, v=(y+.5f)/height;
            if (condensation)
            {
                float dx = Mathf.Min(Mathf.Abs(u-.28f),Mathf.Abs(u-.72f))/.24f;
                float dy=(v-.51f)/.37f;
                float radius=Mathf.Sqrt(dx*dx+dy*dy);
                float noise=.5f*Mathf.PerlinNoise(u*16f+.55f,v*13f)+.35f*Mathf.PerlinNoise(u*49f,v*37f)
                    +.15f*Mathf.PerlinNoise(u*105f,v*88f);
                float bead=Mathf.Pow(Mathf.Clamp01((Mathf.PerlinNoise(u*142f,v*87f)-.56f)*3.4f),3);
                float lip=Mathf.Clamp01(1f-Mathf.InverseLerp(.94f,1.01f,radius));
                float fog=(.06f+.54f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.26f,.96f,radius))) *
                    (.50f+noise) + .18f*bead;
                pixels[y*width+x]=new Color(.69f,.72f,.69f,lip*fog);
            }
            else
            {
                float dx=(u-.5f)*1.9f, dy=(v-.51f)*1.74f;
                float r=Mathf.Sqrt(dx*dx+dy*dy);
                float a=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.26f,1.12f,r));
                pixels[y*width+x]=new Color(.42f,.045f,.027f,a);
            }
        }
        texture.SetPixels32(pixels); texture.Apply(false,true); return texture;
    }
}
