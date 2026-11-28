using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Optional read-only instrumentation for external keyboard/mouse playtests.
// Normal launches do not create an observer or open any files.
public sealed class OsowiecPlaytestObserver : MonoBehaviour
{
    [Serializable] class MenuControl
    {
        public string name;
        public float x,y;
        public bool enabled;
    }
    [Serializable] class Actor
    {
        public string name;
        public bool friendly, controlled;
        public Vector3 position, onScreen, aim;
        public float yaw, metres;
        public int hp, ammo;
    }
    [Serializable] class Sample
    {
        public string scene, quality;
        public float filter, cameraFov, handTravel, feedTravel, clockScale;
        public int pid;
        public bool deployment, fps, bot, finished, paused, mask, selected;
        public int objectives, held, width, height;
        public string reliefStage, reliefRoute, reliefBlocker;
        public bool reliefNeedsHelp, reliefStarted, reliefComplete;
        public bool reliefStocked;
        public float reliefEscortedMetres, reliefSupplyX, reliefSupplyZ;
        public int reliefDelivered;
        public float reliefMetres, reliefCartX, reliefCartZ, reliefObjectiveX, reliefObjectiveZ;
        public float plan, requested, time, fpsMedianMs, fpsP95Ms;
        public Vector3 eye, look;
        public Vector3[] route, mapMarks;
        public Actor[] actors;
        public MenuControl[] menu;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!Environment.GetCommandLineArgs().Contains("-osowiec-playtest")) return;
        var root = new GameObject("Read-only playtest observer");
        DontDestroyOnLoad(root);
        root.AddComponent<OsowiecPlaytestObserver>();
    }

    string output, markerFile;
    string focusLog;
    int processId;
    string staging;
    void Timeline(string label)
    {
        if (focusLog==null) return;
        try
        {
            File.AppendAllText(focusLog,FormattableString.Invariant(
                $"{DateTime.UtcNow:O}\t{Time.realtimeSinceStartupAsDouble:F4}\t{label}\t{Application.isFocused}\t{Time.timeScale}\t{SceneManager.GetActiveScene().name}\n"));
        } catch(IOException) { }
    }
    void OnApplicationFocus(bool focused) => Timeline(focused ? "focus-in" : "focus-out");
    void OnApplicationPause(bool paused) => Timeline(paused ? "application-pause" : "application-resume");
    BinaryWriter timing;
    int untilFlush;
    float nextWrite;
    readonly float[] frames = new float[300];
    int index, filled;
    void Awake()
    {
        Application.runInBackground = true;
        processId=System.Diagnostics.Process.GetCurrentProcess().Id;
        output = Path.Combine(Application.temporaryCachePath, "Osowiec-readout.json");
        staging=output+"."+processId+".tmp";
        focusLog = Path.Combine(Application.temporaryCachePath,
            $"Osowiec-focus-{System.Diagnostics.Process.GetCurrentProcess().Id}.tsv");
        Timeline("launch");
        markerFile = Path.Combine(Application.temporaryCachePath, "Osowiec-survey-point.json");
        try
        {
            string name = "Osowiec-frames-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".bin";
            var stream = new FileStream(Path.Combine(Application.temporaryCachePath,name),
                FileMode.Create,FileAccess.Write,FileShare.ReadWrite,32768);
            timing = new BinaryWriter(stream);
        }
        catch (IOException) { }
        Debug.Log("Playtest observer: " + output);
    }

    void LateUpdate()
    {
        frames[index] = Time.unscaledDeltaTime * 1000f;
        if (frames[index] > 250f) Timeline(FormattableString.Invariant($"long-frame-{frames[index]:F1}ms"));
        if (timing != null)
        {
            var operation=OsowiecOperation.Instance;
            // One record per displayed game frame: elapsed seconds, milliseconds,
            // current mission order (255 for HQ) and camera (1 for first person).
            timing.Write(Time.unscaledTime);
            timing.Write(frames[index]);
            timing.Write(operation == null ? (byte)255 : (byte)operation.CompletedOrders);
            timing.Write((byte)(CameraManager.Instance != null && CameraManager.Instance.IsActionMode() ? 1 : 0));
            if (++untilFlush >= 360) { timing.Flush(); untilFlush=0; }
        }
        index = (index + 1) % frames.Length;
        filled = Mathf.Min(frames.Length, filled+1);
        if (Time.unscaledTime < nextWrite) return;
        nextWrite = Time.unscaledTime + .14f / Mathf.Clamp(Time.timeScale,1f,4f);
        var cam = CameraManager.Instance;
        var game = GameManager.Instance;
        var op = OsowiecOperation.Instance;
        var planner = OsowiecPlanner.Instance;
        bool fps = cam != null && cam.IsActionMode();
        Camera observer = cam == null ? Camera.main : fps ? cam.GetActionCamera() : cam.GetTacticalCamera();
        if (observer == null || game == null) return;
        var active = fps ? cam.GetCurrentControlledUnit() : planner?.SelectedUnit;
        var roster = FindObjectsByType<Unit>(FindObjectsInactive.Exclude);
        var sorted = frames.Take(filled).OrderBy(n => n).ToArray();
        var sample = new Sample {
            scene = SceneManager.GetActiveScene().name, width = Screen.width, height = Screen.height,
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            deployment = game.IsArmyDeploymentPhase(), bot = game.IsBotTurn(), finished = game.IsGameOver(),
            paused = game.IsPaused(), fps = fps, selected = planner?.SelectedUnit != null,
            plan = planner != null ? planner.PlannedMetres : 0, requested=planner!=null?planner.RequestedMetres:0, time = Time.unscaledTime, clockScale=Time.timeScale,
            fpsMedianMs = sorted[filled/2], fpsP95Ms = sorted[Mathf.Min(filled-1, (int)(filled*.95f))],
            objectives = op != null ? op.CompletedOrders : 0, held = op != null ? op.TurnsHeld : 0,
            pid=processId, handTravel=OsowiecWeaponView.HandTravel, feedTravel=OsowiecWeaponView.FeedTravel,
            filter=op != null ? op.RemainingFilter(active) : 22f, cameraFov=observer.fieldOfView,
            mask = op != null && op.MaskOn(active), eye = observer.transform.position, look = observer.transform.forward,
            actors = roster.Select(u => new Actor {
                name = u.GetUnitDisplayName(), controlled = u == active, friendly = u.owner == Player.Player1,
                position = u.transform.position, yaw = u.transform.eulerAngles.y, hp = u.GetHealth(),
                metres = u.GetRemainingMoveMeters(), ammo = u.GetEquippedWeapon() != null ? u.GetEquippedWeapon().AmmoInMag : 0,
                onScreen = observer.WorldToViewportPoint(u.transform.position + Vector3.up),
                aim = observer.WorldToViewportPoint(u.transform.position + Vector3.up * 1.2f)
            }).ToArray()
        };
        if (sample.paused || sample.deployment || sample.finished || sample.scene=="OsowiecHQ")
        {
            sample.menu=FindObjectsByType<UnityEngine.UI.Selectable>(FindObjectsSortMode.None)
                .Where(el=>el && el.IsActive() && el.GetComponentInParent<Canvas>()!=null)
                .Select(el=>{
                    var rect=(RectTransform)el.transform;
                    var screen=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
                    return new MenuControl {name=el.name,x=screen.x/Screen.width,y=1-screen.y/Screen.height,enabled=el.IsInteractable()};
                }).ToArray();
        }
        if (op != null && ChessGrid.Instance != null)
        {
            var relief = op.relief;
            if (relief != null)
            {
                sample.reliefStage = relief.StageName;
                sample.reliefBlocker = relief.RoadObstruction;
                sample.reliefStocked = relief.Stocked;
                sample.reliefEscortedMetres = relief.EscortedMetres;
                sample.reliefSupplyX = relief.SupplyPost.x;
                sample.reliefSupplyZ = relief.SupplyPost.z;
                sample.reliefRoute = relief.RouteName;
                sample.reliefStarted = relief.Started;
                sample.reliefComplete = relief.Complete;
                sample.reliefNeedsHelp = relief.NeedsHelp;
                sample.reliefDelivered = relief.Delivered;
                sample.reliefMetres = relief.DistanceRemaining;
                sample.reliefCartX = relief.CartPosition.x;
                sample.reliefCartZ = relief.CartPosition.z;
                sample.reliefObjectiveX = relief.CurrentObjective.x;
                sample.reliefObjectiveZ = relief.CurrentObjective.z;
            }
            Vector3 marker = op.NextObjective;
            try { if (File.Exists(markerFile)) marker = JsonUtility.FromJson<Vector3>(File.ReadAllText(markerFile)); }
            catch (IOException) { }
            var subject = active != null ? active : roster.FirstOrDefault(u => u.owner == Player.Player1);
            var nav = new NavMeshPath();
            if (subject != null && NavMesh.SamplePosition(subject.transform.position, out var a, 2, NavMesh.AllAreas)
                && NavMesh.SamplePosition(marker, out var b, 2, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,nav))
                sample.route = nav.corners;
            var deployment=FindFirstObjectByType<OsowiecDeploymentSectors>();
            var starts=OsowiecDeploymentSectors.Cells[deployment ? deployment.Sector : 0];
            sample.mapMarks = starts.Select(c=>ChessGrid.Instance.GridToWorldPosition(c.x,c.y)).Append(marker).Select(observer.WorldToViewportPoint).ToArray();
        }
        try
        {
            File.WriteAllText(staging,JsonUtility.ToJson(sample));
            if (File.Exists(output)) File.Replace(staging,output,null);
            else File.Move(staging,output);
        }
        catch (IOException) { }
    }
    void OnDestroy() => timing?.Dispose();
}
