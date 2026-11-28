using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Option=OsowiecVideoOptions.Option;

// Invoked only in the Windows player launched with -osowiec-video-check <output directory>.
public sealed class OsowiecVideoCheck : MonoBehaviour
{
    OsowiecVideoOptions options;
    string folder;
    string pass;
    string LogFile=>Path.Combine(folder,"Graphics-"+pass+".txt");
    void Check(bool condition,string observation)
    {
        if(!condition)throw new InvalidOperationException("Graphics: "+observation);
        File.AppendAllText(LogFile,observation+Environment.NewLine);
    }
    Button FindButton(string id)=>GetComponentsInChildren<Button>(true).Single(b=>b.gameObject.activeInHierarchy && b.name==id);
    void Click(string id)
    {
        var button=FindButton(id);
        if(!button.interactable)throw new InvalidOperationException("Disabled "+id);
        button.onClick.Invoke();
    }
    void Set(Option key,int wanted)
    {
        for(int i=0;i<12 && options.Get(key)!=wanted;i++)
        {
            bool increase=key==Option.Cap && options.Get(key)==-1 ? false : options.Get(key)<wanted;
            Click(key+(increase?" next":" prev"));
        }
        Check(options.Get(key)==wanted,key+" = "+wanted);
    }
    void Snapshot(string file)
    {
        var canvas=options.journal.GetComponentInParent<Canvas>().rootCanvas;
        var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;var distance=canvas.planeDistance;
        int uiLayer=canvas.gameObject.layer;
        var host=new GameObject("Graphics journal capture");
        var capture=host.AddComponent<Camera>();capture.enabled=false;
        capture.clearFlags=CameraClearFlags.SolidColor;capture.backgroundColor=Color.black;
        capture.cullingMask=1<<uiLayer;capture.orthographic=true;
        capture.transform.position=new Vector3(0,20000,0);capture.farClipPlane=4;
        var target=RenderTexture.GetTemporary(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32);
        var old=RenderTexture.active;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            capture.targetTexture=target;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=capture;canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases();
            capture.Render(); RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            var center=image.GetPixel(target.width/2,target.height/2);
            Check(center.maxColorComponent>.04f,"Journal page captured: "+file);
            File.WriteAllBytes(Path.Combine(folder,file+".png"),image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=distance;
            capture.targetTexture=null;RenderTexture.active=old;
            Destroy(image);RenderTexture.ReleaseTemporary(target);Destroy(host);
        }
    }
    IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs();
        folder=args[Array.IndexOf(args,"-osowiec-video-check")+1];
        bool load=args.Contains("-osowiec-video-reload");
        pass=load?"reload":"write";
        Directory.CreateDirectory(folder);
        File.WriteAllText(LogFile,"build="+Application.buildGUID+Environment.NewLine);
        var routine=Run(load);
        while(true)
        {
            bool more;
            try{more=routine.MoveNext();}
            catch(Exception e)
            {
                File.AppendAllText(LogFile,"FAIL "+e); Debug.LogError(e); Application.Quit(2);yield break;
            }
            if(!more)break;
            yield return routine.Current;
        }
        File.AppendAllText(LogFile,"PASS"+Environment.NewLine);
        Debug.Log("PASS graphics "+pass);Application.Quit(0);
    }
    IEnumerator Run(bool load)
    {
        Application.runInBackground=true;
        options=GetComponent<OsowiecVideoOptions>();
        for(int i=0;i<8;i++)yield return null;
        yield return new WaitForSecondsRealtime(.5f);
        var start=OsowiecVideoOptions.DisplayChoice.Actual();
        string Actual(){var d=OsowiecVideoOptions.DisplayChoice.Actual();return $"{d.width}x{d.height} {d.mode}";}
        File.AppendAllText(LogFile,"Start "+Actual()+Environment.NewLine);
        if(!GameManager.Instance.IsPaused())GameManager.Instance.PauseGame();
        Click("Settings"); Click("Video tab");yield return null;
        if(!load)Click("Reset video");
        if(!load)
        {
            Set(Option.VSync,0);Set(Option.Cap,75);
            Click("Leaf 1");
            Set(Option.RenderScale,80);Set(Option.ShadowMap,1024);Set(Option.ShadowDistance,35);
            Set(Option.AntiAliasing,1);Set(Option.Textures,1);Set(Option.Relief,0);
            yield return new WaitForEndOfFrame();Snapshot("02-detail");
            Click("Leaf 2");Set(Option.Exposure,2);
            Click("Fov next");yield return new WaitForEndOfFrame();Snapshot("03-camera");
            Click("Leaf 0");yield return new WaitForEndOfFrame();Snapshot("01-display");
        }
        var pipeline=(UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
        var camera=FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.enabled && c.gameObject.scene==gameObject.scene && c.GetUniversalAdditionalCameraData().renderType==CameraRenderType.Base);
        var volume=GetComponentsInChildren<Volume>(true).Single(v=>v.gameObject.name=="Яркость полевого журнала");
        Check(OsowiecFieldSettings.FieldOfView==65,"FOV 65");
        Check(Mathf.Abs(pipeline.renderScale-.8f)<.001f && pipeline.mainLightShadowmapResolution==1024 &&
              Mathf.Abs(pipeline.shadowDistance-35)<.01f,"URP render scale 0.8, main shadow atlas 1024, distance 35");
        Check(camera.GetUniversalAdditionalCameraData().antialiasing==AntialiasingMode.FastApproximateAntialiasing && camera.GetUniversalAdditionalCameraData().renderPostProcessing,"Camera uses FXAA and post processing");
        Check(volume.weight>0 && volume.sharedProfile.TryGet(out ColorAdjustments color) && Mathf.Abs(color.postExposure.value-.5f)<.01f,"Exposure +0.5 EV");
        Check(QualitySettings.vSyncCount==0 && Application.targetFrameRate==75 && QualitySettings.globalTextureMipmapLimit==1 &&
            Shader.GetGlobalFloat("_OsowiecFlatMaterials")>.99f,"75 FPS, vSync off, texture mip limit 1, reduced terrain relief");
        if(OsowiecVideoOptions.DisplayOverridden)
        {
            Check(start.Same(OsowiecVideoOptions.DisplayChoice.Actual()) && !FindButton("Apply display").interactable,"Explicit window override preserved: "+Screen.width+" x "+Screen.height);
        }
        else if(!load)
        {
            if(start.mode!=FullScreenMode.Windowed)
            {
                for(int i=0;i<3 && options.Pending.mode!=FullScreenMode.Windowed;i++)Click("Mode prev");
            }
            if(FindButton("Resolution prev").interactable)Click("Resolution prev");else Click("Resolution next");
            start=OsowiecVideoOptions.DisplayChoice.Actual();
            File.AppendAllText(LogFile,"Before preview "+Actual()+Environment.NewLine);
            Click("Apply display");
            yield return new WaitForSecondsRealtime(.6f);
            Check(options.Confirming && !start.Same(OsowiecVideoOptions.DisplayChoice.Actual()),"Preview actually changes native window "+Screen.width+"x"+Screen.height);
            Click("Revert display"); yield return new WaitForSecondsRealtime(.6f);
            Check(!options.Confirming && start.Same(OsowiecVideoOptions.DisplayChoice.Actual()),"Reverted to "+Actual()+", expected "+start.width+"x"+start.height+" "+start.mode);
        }
        Click("Close settings");yield return null;
        Check(!options.journal.Visible,"Journal returns to pause/start page");
        Click("Settings");Click("Video tab");Click("Leaf 1");
        Check(options.Get(Option.RenderScale)==80,"Values survive closing and reopening");
        if(load)
        {
            Click("Reset video");yield return null;
            Check(!PlayerPrefs.HasKey("Osowiec.TestVideo.RenderScale") && !PlayerPrefs.HasKey("Osowiec.TestVideo.Textures") &&
                OsowiecFieldSettings.FieldOfView==60 && Mathf.Approximately(Shader.GetGlobalFloat("_OsowiecFlatMaterials"),0) &&
                options.Get(Option.Exposure)==0 && camera.GetUniversalAdditionalCameraData().antialiasing==AntialiasingMode.None,"Reset restores camera, materials and persisted defaults");
        }
        Click("Close settings");
    }
}
