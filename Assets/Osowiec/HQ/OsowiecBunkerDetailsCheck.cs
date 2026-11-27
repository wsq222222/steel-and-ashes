using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runs only in a graphical standalone player with -osowiec-bunker-details <folder>.
public sealed class OsowiecBunkerDetailsCheck : MonoBehaviour
{
    const string Preference = "Osowiec.GramophoneOn";
    readonly List<string> results = new();
    Camera eye;
    string output;
    bool restorePreference, hadPreference, failed;
    int originalPreference;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-osowiec-bunker-details");
        if (Application.isEditor || i < 0 || i + 1 >= args.Length) return;
        new GameObject("Bunker detail inspection").AddComponent<OsowiecBunkerDetailsCheck>().output =
            Path.GetFullPath(args[i + 1]);
    }

    void OnEnable() => Application.logMessageReceived += OnMessage;
    void OnDisable()
    {
        Application.logMessageReceived -= OnMessage;
        RestorePreference();
    }

    void RestorePreference()
    {
        if (!restorePreference) return;
        if (hadPreference) PlayerPrefs.SetInt(Preference, originalPreference);
        else PlayerPrefs.DeleteKey(Preference);
        PlayerPrefs.Save();
        restorePreference = false;
    }

    void OnMessage(string message, string stack, LogType type)
    {
        if (type != LogType.Exception || failed) return;
        failed = true;
        RestorePreference();
        if (Directory.Exists(output))
            File.WriteAllText(Path.Combine(output, "details.txt"), string.Join("\n", results) + "\nFAIL " + message);
        Application.Quit(5);
    }

    static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException("Bunker details: " + detail);
    }

    void Pose(Vector3 position, Vector3 aim)
    {
        eye.transform.SetPositionAndRotation(position, Quaternion.LookRotation(aim - position, Vector3.up));
    }

    IEnumerator Photo(string name)
    {
        for (int i = 0; i < 3; ++i) yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        Require(image && image.width == Screen.width && image.height == Screen.height, "capture " + name);
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        Destroy(image);
    }

    // If an opening exposes the background, its pixels follow both test colors.
    // Comparing opposite chroma also excludes changes from light flicker and exposure.
    IEnumerator CheckEnclosure(string label)
    {
        var flags = eye.clearFlags;
        var color = eye.backgroundColor;
        var target = eye.targetTexture;
        var active = RenderTexture.active;
        int width = Mathf.Min(1200, Screen.width);
        int height = Mathf.RoundToInt(width / eye.aspect);
        var buffer = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            eye.targetTexture = buffer;
            eye.clearFlags = CameraClearFlags.SolidColor;
            Color32[] first = null;
            for (int pass = 0; pass < 2; pass++)
            {
                eye.backgroundColor = pass == 0 ? Color.magenta : Color.green;
                for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
                RenderTexture.active = buffer;
                frame.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                RenderTexture.active = active;
                var pixels = frame.GetPixels32();
                if (pass == 0) { first = pixels; continue; }
                int exposed = 0;
                for (int j = 0; j < pixels.Length; j++)
                {
                    var a = first[j];
                    var b = pixels[j];
                    if (a.r - a.g > 32 && a.b - a.g > 32 && b.g - b.r > 32 && b.g - b.b > 32 &&
                        a.r - b.r > 42 && b.g - a.g > 42) exposed++;
                }
                results.Add($"{label}: {exposed} background pixels / {pixels.Length}");
                if (exposed != 0)
                {
                    File.WriteAllBytes(Path.Combine(output, label + "-leak.png"), frame.EncodeToPNG());
                    Require(false, $"background visible in {label}, {exposed} pixels");
                }
            }
        }
        finally
        {
            eye.clearFlags = flags;
            eye.backgroundColor = color;
            eye.targetTexture = target;
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(buffer);
            Destroy(frame);
        }
    }

    IEnumerator MeasurePlatter(Transform platter, Transform witness, AudioSource music, bool playing)
    {
        if (OsowiecHeadquartersAudio.GramophoneOn != playing) OsowiecHeadquartersAudio.ToggleGramophone();
        yield return new WaitForEndOfFrame();
        Require(music.clip && music.isPlaying == playing && OsowiecHeadquartersAudio.GramophoneOn == playing,
            "gramophone audio switch did not reach " + playing);
        var last = platter.localRotation;
        var axis = Vector3.up;
        var start = Vector3.ProjectOnPlane(witness.position - platter.position, axis);
        Require(start.magnitude > .002f, "record witness must be off the spindle axis");
        float angle = 0f, witnessTravel = 0f;
        float deadline = Time.realtimeSinceStartup + .60f;
        int frames = 0;
        do
        {
            yield return new WaitForEndOfFrame();
            Require(music.isPlaying == playing, "audio state changed while measuring the disc");
            angle += Quaternion.Angle(last, platter.localRotation);
            last = platter.localRotation;
            var offset = Vector3.ProjectOnPlane(witness.position - platter.position, axis);
            witnessTravel = Mathf.Max(witnessTravel, (offset - start).magnitude);
            frames++;
        } while (frames < 8 || Time.realtimeSinceStartup < deadline);
        results.Add($"music={playing}, platter travel={angle:F2} degrees, label displacement={witnessTravel:F4} m, {frames} frames");
        Require(playing ? angle > 40f && witnessTravel > start.magnitude * .55f :
            angle < .05f && witnessTravel < .0001f, "record motion disagrees with playback: " + results.Last());
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "details.txt"), "RUNNING " + Application.buildGUID);
        Application.runInBackground = true;
        Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "a graphical player is required");
        float timeout = Time.realtimeSinceStartup + 35f;
        while (!CameraManager.Instance || !CameraManager.Instance.GetCurrentControlledUnit() ||
               !GameObject.Find("Continue") || !GameObject.Find("Gramophone horn"))
        {
            Require(Time.realtimeSinceStartup < timeout, "headquarters startup timeout");
            yield return null;
        }
        Require(SceneManager.GetActiveScene().name == "OsowiecHQ", "expected HQ as startup scene");
        yield return OsowiecMapCheck.ApplyCaptureSize();
        GameObject.Find("Continue").GetComponent<Button>().onClick.Invoke();
        for (int i = 0; i < 6; ++i) yield return null;
        var room = GameObject.Find("Osowiec command bunker");
        Require(room, "command bunker missing");
        eye = CameraManager.Instance.GetActionCamera();
        Require(eye && eye.isActiveAndEnabled, "live first-person camera missing");
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var canvas in roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true))) canvas.enabled = false;
        foreach (var staff in roots.SelectMany(r => r.GetComponentsInChildren<OsowiecStaffBody>(true))) staff.gameObject.SetActive(false);
        var actor = CameraManager.Instance.GetCurrentControlledUnit();
        foreach (var r in actor.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
        eye.transform.SetParent(null, true);

        var stand = GameObject.Find("FieldGramophone");
        Require(stand, "gramophone missing");
        var parts = stand.GetComponentsInChildren<Transform>(true);
        var platter = parts.SingleOrDefault(p => p.name == "RecordPlatter");
        var witness = parts.SingleOrDefault(p => p.name == "RecordWitness");
        Require(platter && witness && witness.IsChildOf(platter), "record centre and visible label witness");
        var music = GameObject.Find("Gramophone horn").GetComponent<AudioSource>();
        hadPreference = PlayerPrefs.HasKey(Preference);
        originalPreference = PlayerPrefs.GetInt(Preference, 1);
        restorePreference = true;
        yield return MeasurePlatter(platter, witness, music, true);
        yield return MeasurePlatter(platter, witness, music, false);

        // Eye-level viewpoints in player coordinates. Include the rear of the gate,
        // the transition into the room and the opposite shoulder of the vault.
        Pose(new(-.22f,2.51f,-7.63f), new(1.15f,2.74f,-8.54f));
        yield return Photo("01-gate");
        yield return CheckEnclosure("gate");
        // The reported flicker was at the reveal where the pale room wall
        // returns into the darker approach, not at the far end of the vault.
        Pose(new(.33f,1.56f,-1.34f), new(1.19f,1.54f,-2.70f));
        yield return Photo("02-corner");
        yield return CheckEnclosure("portal-reveal");
        Pose(new(-1.10f,1.61f,-.98f), new(-3.23f,2.33f,-2.53f));
        yield return CheckEnclosure("southwest-vault");
        Pose(new(-2.16f,1.59f,.52f), new(-3.74f,2.01f,1.65f));
        yield return CheckEnclosure("signal-alcove");
        Pose(new(2.05f,1.61f,1.38f), new(3.27f,2.33f,3.50f));
        yield return CheckEnclosure("northeast-vault");
        Pose(new(1.51f,1.56f,-1.97f), new(3.21f,1.39f,-1.24f));
        yield return Photo("03-coat");
        Pose(stand.transform.position + new Vector3(-.61f,1.51f,-.74f),
            platter.position + new Vector3(.04f,.15f,.03f));
        yield return Photo("04-gramophone");
        RestorePreference();
        string report = "PASS " + Application.buildGUID + $" {Screen.width}x{Screen.height}\n" + string.Join("\n", results);
        File.WriteAllText(Path.Combine(output, "details.txt"), report);
        Debug.Log(report);
        Application.Quit(0);
    }
}
