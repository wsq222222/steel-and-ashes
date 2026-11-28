using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public static class OsowiecSoundBank
{
    private const string Root = "OsowiecSound/";
    private static readonly Dictionary<string, AudioClip> Clips = new();
    private static readonly Dictionary<string, AudioMixerGroup> Buses = new();
    private static readonly Dictionary<string, int> LastVariant = new();
    private static AudioMixer mixer;
    private static bool mixerLoaded;
    private static System.Random variants = new();

    public static bool InOperation
    {
        get
        {
            string name = SceneManager.GetActiveScene().name;
            return name == "Osowiec" || name == "OsowiecHQ";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Clips.Clear();
        Buses.Clear();
        LastVariant.Clear();
        mixer = null;
        mixerLoaded = false;
        variants = new System.Random(81916);
    }

    public static AudioClip Clip(string name)
    {
        if (!Clips.TryGetValue(name, out var clip))
        {
            clip = Resources.Load<AudioClip>(Root + name);
            Clips.Add(name, clip);
            if (clip == null) Debug.LogError($"Missing Osowiec audio master: {name}");
        }
        return clip;
    }

    public static AudioClip Variant(string name, int count)
    {
        int index;
        if (LastVariant.TryGetValue(name, out int previous) && count > 1)
        {
            index = variants.Next(count - 1);
            if (index >= previous) index++;
        }
        else index = variants.Next(count);
        LastVariant[name] = index;
        return Clip(name + (index + 1));
    }

    public static AudioMixer Mixer
    {
        get
        {
            if (!mixerLoaded)
            {
                mixerLoaded = true;
                mixer = Resources.Load<AudioMixer>(Root + "FieldMix");
                if (mixer == null) Debug.LogError("Missing FieldMix audio mixer");
            }
            return mixer;
        }
    }

    public static AudioMixerGroup Bus(string name)
    {
        if (!Buses.TryGetValue(name, out var group))
        {
            var matches = Mixer != null ? Mixer.FindMatchingGroups("Master/" + name) : null;
            group = matches != null && matches.Length > 0 ? matches[0] : null;
            if (group == null) Debug.LogError($"Missing Osowiec mixer bus: {name}");
            Buses[name] = group;
        }
        return group;
    }

    public static void Space(bool underRoof, float seconds)
    {
        if (Mixer != null) Mixer.FindSnapshot(underRoof ? "Casemate" : "Open marsh")?.TransitionTo(seconds);
    }

    public static AudioSource Source(Transform parent, string label, string bus, float spatial, int priority)
    {
        var node = new GameObject(label);
        node.transform.SetParent(parent, false);
        var source = node.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = Bus(bus);
        source.spatialBlend = spatial;
        source.priority = priority;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 2.5f;
        source.maxDistance = 34f;
        return source;
    }
}
