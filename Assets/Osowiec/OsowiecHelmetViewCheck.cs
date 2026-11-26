using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Close-ups of the same worn shell, loose rigid body, and skinned head used in the mission.
public sealed class OsowiecHelmetViewCheck : MonoBehaviour
{
    string folder;

    internal static bool Install(string[] args)
    {
        int i = Array.IndexOf(args, "-osowiec-helmet-view");
        if (Application.isEditor || i < 0 || i + 1 >= args.Length) return false;
        var host = new GameObject("Helmet portrait check");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecHelmetViewCheck>().folder = args[i + 1];
        return true;
    }

    void OnEnable() => Application.logMessageReceived += Observe;
    void OnDisable() => Application.logMessageReceived -= Observe;

    void Observe(string message, string trace, LogType type)
    {
        if (type == LogType.Exception) Application.Quit(2);
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(folder);
        Application.runInBackground = true;
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        while (!OsowiecOperation.Instance || !GameManager.Instance.IsArmyDeploymentPhase()) yield return null;
        var deployment = FindFirstObjectByType<ArmyDeploymentController>();
        if (!deployment.TryGetOffer(1, out var offer)) throw new InvalidOperationException("Missing scout");
        var unit = Instantiate(offer.unitPrefab, new Vector3(10,120,10), Quaternion.identity).GetComponent<Unit>();
        unit.gameObject.SetActive(true);
        unit.GetComponent<CharacterController>().enabled = false;
        var skin = unit.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Uniform_and_equipment");
        var head = skin.bones.Single(b => b && b.name == "Head");
        var shell = unit.GetComponentsInChildren<OsowiecHitPart>().Single(p => p.area == OsowiecHitArea.Helmet);
        var presentation = unit.GetComponent<TacticalUnitPresentation>() ?? unit.gameObject.AddComponent<TacticalUnitPresentation>();
        var anim = unit.GetComponentInChildren<Animator>();
        anim.Play("Base Layer.Idle", 0, .4f);
        anim.Update(.001f);
        anim.enabled = false;
        unit.GetComponent<OsowiecCastMotion>().enabled = false;
        unit.enabled = false;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
        var view = new GameObject("Head and loose shell").AddComponent<Camera>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(.16f,.19f,.19f);
        view.fieldOfView = 31;
        view.nearClipPlane = .04f;
        view.farClipPlane = 8;
        view.transform.position = head.position + new Vector3(-.41f,.24f,1.70f);
        view.transform.LookAt(head.position + new Vector3(.08f,.016f,0));
        RenderSettings.fog = false;
        var light = new GameObject("Portrait light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.transform.rotation = Quaternion.Euler(43,159,0);
        presentation.SetBodyVisualVisible(false);
        presentation.SetBodyVisualVisible(true);
        yield return Photo("01-на-бойце.png");
        Time.timeScale = 1;
        shell.DropHelmet(unit, Vector3.right);
        var loose = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Single(b => b.name == "Сбитая каска");
        for (int i = 0; i < 9; i++) yield return new WaitForFixedUpdate();
        loose.isKinematic = true;
        presentation.SetBodyVisualVisible(false);
        presentation.SetBodyVisualVisible(true);
        shell.DropHelmet(unit, Vector3.right);
        yield return Photo("02-после-попадания.png");
        Debug.Log("PASS field helmet portrait pair " + Application.buildGUID);
        Application.Quit(0);
    }

    IEnumerator Photo(string name)
    {
        yield return new WaitForEndOfFrame();
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG()); }
        finally { Destroy(pixels); }
    }
}
