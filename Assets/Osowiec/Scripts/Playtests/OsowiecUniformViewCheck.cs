using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Runs in a graphical player only, with the ordinary HQ camera, controller, and locomotion.
public sealed class OsowiecUniformViewCheck : MonoBehaviour
{
    string folder;
    Camera eye;
    Unit actor;
    OsowiecStaffBody uniform;
    Keyboard keyboard;
    float fov;
    SkinnedMeshRenderer skin;
    bool sawBothBoots;
    readonly WaitForEndOfFrame endOfFrame = new();

    internal static bool Install(string[] args)
    {
        int i = Array.IndexOf(args, "-osowiec-uniform-view-check");
        if (Application.isEditor || i < 0 || i + 1 >= args.Length) return false;
        new GameObject("HQ first-person view check").AddComponent<OsowiecUniformViewCheck>().folder = args[i + 1];
        return true;
    }

    void OnEnable() => Application.logMessageReceived += Observe;
    void OnDisable()
    {
        Application.logMessageReceived -= Observe;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
    }

    void Observe(string message, string stack, LogType type)
    {
        if (type == LogType.Exception)
        {
            File.AppendAllText(Path.Combine(folder, "View.txt"), "FAIL " + message + Environment.NewLine);
            Application.Quit(2);
        }
    }

    void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    void Log(string text) => File.AppendAllText(Path.Combine(folder, "View.txt"), text + Environment.NewLine);
    void SetFov(float degrees)
    {
        fov = degrees;
        OsowiecFieldSettings.FieldOfView = degrees;
    }

    void Look(float pitch) => actor.ApplyActionLookPitchDelta(actor.GetActionLookPitchDegrees() - pitch);
    IEnumerator Walk(float requestedDistance)
    {
        Vector3 from = actor.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        for (int frame = 0; frame < 220; frame++)
        {
            yield return null;
            if (Vector3.ProjectOnPlane(actor.transform.position - from, Vector3.up).magnitude >= requestedDistance)
                yield break;
        }
        throw new InvalidOperationException("HQ walk input did not move the player " + requestedDistance + " m");
    }

    IEnumerator Photo(string name)
    {
        yield return endOfFrame;
        Check(Mathf.Abs(eye.fieldOfView - fov) < .01f, "FOV override failed");
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG()); }
        finally { Destroy(image); }
        var left = eye.WorldToViewportPoint(uniform.leftHand.position);
        var right = eye.WorldToViewportPoint(uniform.rightHand.position);
        bool down = actor.GetActionLookPitchDegrees() >= 70f;
        if (down)
        {
            foreach (var p in new[] {left, right}) Check(p.z > eye.nearClipPlane && p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1, name + " wrist outside camera");
            InspectVisibleSkin(name);
        }
        Log(FormattableString.Invariant($"{name} pitch={actor.GetActionLookPitchDegrees():F1} FOV={fov:F0} eye={eye.transform.position:F2} left={left:F3} right={right:F3}"));
    }

    // Cast a small screen-space grid against the final posed surface. Each sample counts
    // only its closest triangle, so a projected wrist or boot behind the coat cannot pass.
    void InspectVisibleSkin(string name)
    {
        const int columns = 128, rows = 72;
        var pose = new Mesh();
        var probe = new GameObject("Posed HQ skin probe") { layer = 2 };
        bool oldBackfaces = Physics.queriesHitBackfaces;
        try
        {
            skin.BakeMesh(pose);
            probe.transform.SetParent(skin.transform, false);
            var surface = probe.AddComponent<MeshCollider>();
            surface.sharedMesh = pose;
            Physics.SyncTransforms();
            Physics.queriesHitBackfaces = true;
            var triangles = pose.triangles;
            var vertices = pose.vertices;
            var toEye = eye.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = toEye.MultiplyPoint3x4(vertices[i]);
            float near = eye.nearClipPlane;
            float halfHeight = near * Mathf.Tan(eye.fieldOfView * .5f * Mathf.Deg2Rad);
            bool CutsVisiblePlane(Vector3 a, Vector3 b)
            {
                if ((a.z < near) == (b.z < near)) return false;
                var p = Vector3.LerpUnclamped(a, b, (near - a.z) / (b.z - a.z));
                return Mathf.Abs(p.x) <= halfHeight * eye.aspect && Mathf.Abs(p.y) <= halfHeight;
            }
            int cutTriangles = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                if (CutsVisiblePlane(a, b) || CutsVisiblePlane(b, c) || CutsVisiblePlane(c, a)) cutTriangles++;
            }
            var weights = skin.sharedMesh.boneWeights;
            var bones = skin.bones;
            int left = 0, right = 0, leftBoot = 0, rightBoot = 0, coat = 0;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                var ray = eye.ViewportPointToRay(new Vector3((x + .5f) / columns, (y + .5f) / rows));
                if (!surface.Raycast(ray, out var hit, 3f)) continue;
                int corner = hit.barycentricCoordinate.y > hit.barycentricCoordinate.x ? 1 : 0;
                if (hit.barycentricCoordinate.z > hit.barycentricCoordinate[corner]) corner = 2;
                var strongest = bones[weights[triangles[3 * hit.triangleIndex + corner]].boneIndex0];
                if (strongest == uniform.leftHand) left++;
                else if (strongest == uniform.rightHand) right++;
                else if (strongest.name == "LFoot") leftBoot++;
                else if (strongest.name == "RFoot") rightBoot++;
                else if (strongest.name == "Chest" || strongest.name == "Pelvis" || strongest.name == "Spine") coat++;
            }
            Log($"{name} first surface samples {columns}x{rows}: left glove={left}, right glove={right}, LFoot={leftBoot}, RFoot={rightBoot}, coat={coat}");
            Log(FormattableString.Invariant($"{name} near plane={near:F3} m; cut skin triangles={cutTriangles}"));
            Check(cutTriangles == 0, name + " visible near plane cuts " + cutTriangles + " skin triangles");
            Check(left > 3 && right > 3 && coat > 3, name + " gloves or coat occluded");
            sawBothBoots |= leftBoot > 3 && rightBoot > 3;
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
            probe.SetActive(false);
            Destroy(probe);
            Destroy(pose);
        }
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        Time.captureFramerate = 60;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "View.txt"), $"Windows player {Application.buildGUID}, {Screen.width}x{Screen.height}\n");
        for (int frame = 0; frame < 12; frame++) yield return null;
        GameManager.Instance.ResumeGame();
        yield return null;
        var manager = CameraManager.Instance;
        Check(manager.IsHubExploreMode(), "Expected playable headquarters");
        actor = manager.GetCurrentControlledUnit();
        eye = manager.GetActionCamera();
        uniform = FindFirstObjectByType<OsowiecStaffBody>();
        Check(uniform && !uniform.transform.Find("Сложенный план форта"), "Staff body missing or carrying a plan");
        skin = uniform.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.enabled && !r.forceRenderingOff &&
            r.bones.Contains(uniform.leftHand) && r.bones.Contains(uniform.rightHand));
        var controller = actor.GetComponent<CharacterController>();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        yield return null;
        SetFov(60);
        Look(19);
        yield return null;
        yield return Photo("01-forward");
        Look(72);
        yield return null;
        yield return Photo("02-idle-72");
        Look(180);
        float limit = actor.GetActionLookPitchDegrees();
        Check(limit >= 72 && limit < 90, "Unexpected playable pitch range");
        foreach (int angle in new[] {50, 60, 95})
        {
            SetFov(angle);
            yield return null;
            yield return Photo("03-idle-max-fov" + angle);
        }
        SetFov(60);
        yield return Walk(.7f);
        yield return Photo("04-walk-max");
        yield return Walk(.45f);
        yield return Photo("05-walk-max");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        // Face the rear bulkhead from the spawn aisle and stop on its physical collider.
        actor.transform.rotation = Quaternion.LookRotation(Vector3.back);
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        RaycastHit hit = default;
        for (int frame = 0; frame < 260; frame++)
        {
            yield return null;
            if (Physics.Raycast(eye.transform.position, Vector3.back, out hit, controller.radius + .16f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) break;
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Check(hit.collider && !hit.collider.transform.IsChildOf(actor.transform), "No physical contact with rear wall");
        Check(hit.collider.gameObject.name.Contains("Bulkhead"), "Wrong wall collider: " + hit.collider.name);
        Log(FormattableString.Invariant($"rear wall={hit.collider.name} eye clearance={hit.distance:F3} controller radius={controller.radius:F3}"));
        for (int frame = 0; frame < 24; frame++) yield return null;
        foreach (int angle in new[] {50, 95})
        {
            SetFov(angle);
            yield return null;
            yield return Photo("06-wall-max-fov" + angle);
        }
        Check(sawBothBoots, "Both boots must be exposed together when looking down");
        Log("PASS");
        Application.Quit(0);
    }
}
