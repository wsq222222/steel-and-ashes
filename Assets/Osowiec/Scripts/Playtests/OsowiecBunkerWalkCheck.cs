using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Opt-in standalone walkthrough. Input moves the actual hub avatar along the approach.
public sealed class OsowiecBunkerWalkCheck : MonoBehaviour
{
    string output;
    Keyboard keyboard;
    Unit actor;
    Camera eye;
    bool wetStep, woodStep;
    const string GramophonePref = "Osowiec.GramophoneOn";
    bool restoreHorn, hadHorn;
    int savedHorn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-osowiec-bunker-walk");
        if (Application.isEditor || at < 0 || at + 1 >= args.Length) return;
        var host = new GameObject("Bunker walkthrough");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecBunkerWalkCheck>().output = args[at + 1];
    }

    void OnEnable() => Application.logMessageReceived += Observe;
    void OnDisable()
    {
        Application.logMessageReceived -= Observe;
        if (restoreHorn)
        {
            if (hadHorn) PlayerPrefs.SetInt(GramophonePref, savedHorn);
            else PlayerPrefs.DeleteKey(GramophonePref);
            PlayerPrefs.Save();
        }
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
    }

    void Observe(string message, string stack, LogType kind)
    {
        wetStep |= message.Contains("unit=HQ surface=Wet stone");
        woodStep |= message.Contains("unit=HQ surface=Dugout");
        if (kind == LogType.Exception) Application.Quit(3);
    }

    static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("Bunker walkthrough: " + reason);
    }

    static void Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var events = EventSystem.current;
        var transform = (RectTransform)button.transform;
        var click = new PointerEventData(events) {
            position = RectTransformUtility.WorldToScreenPoint(null, transform.TransformPoint(transform.rect.center)),
            button = PointerEventData.InputButton.Left
        };
        var hits = new List<RaycastResult>();
        events.RaycastAll(click, hits);
        var hit = hits.FirstOrDefault().gameObject;
        Require(hit && hit.GetComponentInParent<Button>() == button && button.IsInteractable(),
            "Click on " + button.name + " intercepted by " + (hit ? hit.name : "empty space"));
        ExecuteEvents.ExecuteHierarchy(hit, click, ExecuteEvents.pointerClickHandler);
    }

    IEnumerator Snap(string name)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        Require(image && image.width == Screen.width, "screen readback " + name);
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        Destroy(image);
    }

    void LookAt(Vector3 target)
    {
        var dir = target - eye.transform.position;
        var flat = Vector3.ProjectOnPlane(dir, Vector3.up);
        actor.transform.rotation = Quaternion.LookRotation(flat);
        float pitch = -Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg;
        actor.ApplyActionLookPitchDelta(actor.GetActionLookPitchDegrees() - pitch);
    }

    IEnumerator Walk(Vector2 target)
    {
        float deadline = Time.realtimeSinceStartup + 12;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        while (true)
        {
            var foot = actor.transform.position;
            var dir = new Vector3(target.x - foot.x, 0, target.y - foot.z);
            if (dir.magnitude < .21f) break;
            Require(Time.realtimeSinceStartup < deadline, $"route blocked at {foot:F2}, target {target}");
            actor.transform.rotation = Quaternion.LookRotation(dir);
            yield return null;
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        Debug.Log($"Bunker walkthrough: walked to {actor.transform.position:F2}");
    }

    IEnumerator Use()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        for (int i = 0; i < 15; ++i) yield return null;
        yield return OsowiecMapCheck.ApplyCaptureSize();
        Require(SceneManager.GetActiveScene().name == "OsowiecHQ", "boot scene");
        yield return OsowiecBunkerLocaleCheck.CheckHeadquartersArt();
        var menu = GameObject.Find("Continue");
        Require(menu && menu.GetComponent<Button>(), "entry menu");
        Click(menu.GetComponent<Button>());
        yield return null;
        actor = CameraManager.Instance.GetCurrentControlledUnit();
        eye = CameraManager.Instance.GetActionCamera();
        keyboard = InputSystem.AddDevice<Keyboard>("Bunker walk keyboard");
        keyboard.MakeCurrent();
        var start = actor.transform.position;
        Require(start.z < -7.6f, $"spawn before steps {start}");
        LookAt(new Vector3(0,.76f,.85f));
        yield return Snap("01-entry");
        var filter = GameObject.Find("Rain outside the entrance").GetComponent<AudioLowPassFilter>();
        float entryCutoff = filter.cutoffFrequency;
        yield return Walk(new Vector2(-.19f,-3.0f));
        var lamp = GameObject.Find("Gramophone lantern").GetComponent<Light>();
        LookAt(lamp.transform.position);
        float distance = Vector3.Distance(eye.transform.position, lamp.transform.position);
        Require(lamp.enabled && lamp.intensity > 0 && distance > 5, "gramophone light from passage");
        yield return Snap("02-lamp");
        yield return Walk(new Vector2(1.94f,-.30f));
        yield return Walk(new Vector2(1.95f,2.02f));
        LookAt(lamp.transform.position);
        Require(lamp.enabled && lamp.intensity > 0, "gramophone light on approach");
        bool initial = OsowiecHeadquartersAudio.GramophoneOn;
        hadHorn = PlayerPrefs.HasKey(GramophonePref);
        savedHorn = PlayerPrefs.GetInt(GramophonePref,1);
        restoreHorn = true;
        var horn = GameObject.Find("Gramophone horn").GetComponent<AudioSource>();
        yield return Use();
        Require(horn.isPlaying == !initial && OsowiecHeadquartersAudio.GramophoneOn == !initial, "first E on gramophone");
        yield return Use();
        Require(horn.isPlaying == initial && OsowiecHeadquartersAudio.GramophoneOn == initial, "second E on gramophone");
        yield return Walk(new Vector2(1.94f,-.38f));
        yield return Walk(new Vector2(.22f,-.35f));
        var end = actor.transform.position;
        LookAt(new Vector3(.03f,.86f,1.13f));
        yield return Snap("03-desk");
        Require(start.y-end.y > .65f && Mathf.Abs(end.y) < .4f, $"descent from {start} to {end}");
        Require(wetStep && woodStep, "step sound on stone and planks; pass -osowiec-audio-trace");
        Require(filter.cutoffFrequency < entryCutoff*.6f, "rain remains exposed inside");
        var briefing = FindObjectsByType<MissionSelectUI>(FindObjectsSortMode.None).Single(m => m.isActiveAndEnabled);
        yield return Use();
        Require(briefing.IsOpen, "desk E opens order and survey");
        var deploy = GameObject.Find("Deploy").GetComponent<Button>();
        Click(deploy);
        for (int i = 0; i < 15; ++i) yield return null;
        Require(SceneManager.GetActiveScene().name == "Osowiec", "order opens deployment");
        var paper = FindFirstObjectByType<OsowiecPaperPlacement>();
        var map = paper.transform.Find("Deployment survey/Survey desk/Plan of the operation");
        var positions = map.GetComponentsInChildren<OsowiecPaperSlot>().Where(s => s.name.StartsWith("Position ")).ToArray();
        Require(positions.Length == 6, "six deployment buttons on map");
        var army = FindFirstObjectByType<ArmyDeploymentController>();
        foreach (int i in new[] { 0, 3 })
        {
            var card = paper.GetComponentsInChildren<OsowiecPaperDrag>().Single(s => s.offer == i/3);
            Click(card.GetComponent<Button>());
            var pos = positions.Single(s => s.index == i);
            Click(pos.GetComponent<Button>());
            yield return null;
            Require(army.DeployedAt(OsowiecDeploymentSectors.Cells[i/3][0]), "deployment position " + i);
        }
        yield return Snap("04-deployment");
        yield return OsowiecBunkerLocaleCheck.Check();
        Click(GameObject.Find("Begin operation").GetComponent<Button>());
        for (int i=0; i<12; i++) yield return null;
        Require(!GameManager.Instance.IsArmyDeploymentPhase() && OsowiecOperation.Instance, "operation started");
        string result = $"PASS {Application.buildGUID} {Screen.width}x{Screen.height}. Start {start:F2}; desk {end:F2}; " +
            $"walked both surfaces, rain low-pass attenuated, gramophone toggled twice, lamp viewed at {distance:F2} m; " +
            "desk E, order, both placement sectors, RU/EN localisation and operation start.";
        Debug.Log(result);
        File.WriteAllText(Path.Combine(output,"walkthrough.txt"), result);
        Application.Quit(0);
    }
}
