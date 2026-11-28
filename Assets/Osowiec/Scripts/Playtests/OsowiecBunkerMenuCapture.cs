using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Opt-in photo mode for generating the main menu artwork from the actual standalone renderer.
public sealed class OsowiecBunkerMenuCapture : MonoBehaviour
{
    public const int Width = 3840, Height = 2160;
    string destination;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-osowiec-menu-photo");
        if (Application.isEditor || at < 0 || at+1 >= args.Length) return;
        var host = new GameObject("Bunker menu photograph");
        host.AddComponent<OsowiecBunkerMenuCapture>().destination = Path.GetFullPath(args[at+1]);
    }

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;
    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Exception) Application.Quit(4);
    }

    IEnumerator Start()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Menu photography requires a graphics device.");
        Application.runInBackground = true;
        float deadline = Time.realtimeSinceStartup+35f;
        while (!CameraManager.Instance || !CameraManager.Instance.GetCurrentControlledUnit())
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("HQ avatar did not start");
            yield return null;
        }
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "OsowiecHQ") throw new InvalidOperationException("Enter HQ to photograph the bunker");
        GameManager.Instance.ResumeGame();
        var roots = scene.GetRootGameObjects();
        var actor = CameraManager.Instance.GetCurrentControlledUnit();
        var camera = CameraManager.Instance.GetActionCamera();
        var profiles = roots.SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
            .First(r => r.name == "HQ_Profiles");
        string localeArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-language=",StringComparison.OrdinalIgnoreCase));
        string requiredLocale = localeArg?.Substring("-language=".Length);
        var properties = new MaterialPropertyBlock();
        Texture print;
        do
        {
            yield return null;
            profiles.GetPropertyBlock(properties,0);
            print = properties.GetTexture("_BaseMap");
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Localized wall sheet did not finish loading: "+requiredLocale);
        } while (!print || (requiredLocale!=null && (LocalizationSettings.SelectedLocale?.Identifier.Code != requiredLocale ||
            print.name != (requiredLocale=="en" ? "Profiles-en" : "Profiles"))));
        Debug.Log($"Menu photo locale={LocalizationSettings.SelectedLocale?.Identifier.Code}, printed asset={print.name}");

        foreach (var canvas in roots.SelectMany(root => root.GetComponentsInChildren<Canvas>(true)))
            canvas.enabled = false;
        foreach (var staff in roots.SelectMany(root => root.GetComponentsInChildren<OsowiecStaffBody>(true)))
            staff.gameObject.SetActive(false);
        foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            renderer.forceRenderingOff = true;
        // Preserve the exact camera/render stack initialized by the normal first-person player.
        camera.transform.SetParent(null, true);
        camera.transform.SetPositionAndRotation(new Vector3(-2.42f,1.71f,-1.85f), Quaternion.Euler(4.5f,17f,0f));
        camera.fieldOfView = 52f; camera.aspect = (float)Width/Height;
        camera.useOcclusionCulling = false;
        var previous = RenderTexture.active;
        var target = new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var image = new Texture2D(Width,Height,TextureFormat.RGBA32,false,false);
        target.Create();
        camera.targetTexture = target;
        var end = new WaitForEndOfFrame();
        try
        {
            // Let real player frames populate GPU textures, shadow tiles and camera history.
            for (int frame=0; frame<16; frame++) yield return end;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,Width,Height),0,0,false);
            VerifyWallPrint(image);
            ShadeMenu(image);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination,image.EncodeToPNG());
            Debug.Log("Photographed the live command room at 3840 x 2160: "+destination);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            Destroy(target);
            Destroy(image);
        }
        Application.Quit(0);
    }

    public static void VerifyWallPrint(Texture2D image)
    {
        if (image.width != Width || image.height != Height)
            throw new InvalidOperationException("A native 3840 x 2160 photograph is required.");
        var pixels = image.GetPixelData<Color32>(0);
        int total=0, edges=0;
        static int Luma(Color32 p) => (54*p.r + 183*p.g + 19*p.b) >> 8;
        // Interior of the printed atlas on the far wall. A lit blank sheet can have
        // broad tonal variation, but does not have the fine contour lines or grid.
        for (int y=(int)(Height*.53f); y<(int)(Height*.665f); y+=3)
        for (int x=(int)(Width*.438f); x<(int)(Width*.572f); x+=3)
        {
            int i=y*Width+x, c=Luma(pixels[i]);
            if (Math.Abs(c-Luma(pixels[i+4]))>8 || Math.Abs(c-Luma(pixels[i+4*Width]))>8) edges++;
            total++;
        }
        if (edges < total*.012f)
            throw new InvalidOperationException($"The wall sheet has no resolved printed detail ({edges}/{total} samples).");
        Debug.Log($"Menu artwork printed ink samples: {edges}/{total}");
    }

    static void ShadeMenu(Texture2D image)
    {
        var pixels = image.GetPixelData<Color32>(0);
        var gain = new float[Width];
        for (int x=0; x<Width; x++)
            gain[x]=Mathf.SmoothStep(.34f,1f,Mathf.InverseLerp(.18f,.59f,(float)x/(Width-1)));
        for (int y=0; y<Height; y++)
        for (int x=0; x<Width; x++)
        {
            int i=y*Width+x;
            var p=pixels[i]; float shade=gain[x];
            pixels[i]=new Color32((byte)(p.r*shade),(byte)(p.g*shade),(byte)(p.b*shade),255);
        }
        image.Apply(false,false);
    }
}
