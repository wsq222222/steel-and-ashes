using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class OsowiecVideoOptions : MonoBehaviour
{
    internal enum Option { VSync, Cap, RenderScale, ShadowMap, ShadowDistance, AntiAliasing, Textures, Exposure, Relief }
    static readonly int[][] Values = {
        new[]{0,1}, new[]{30,45,60,75,90,120,144,165,240,-1}, new[]{60,70,80,90,100,110,120,130},
        new[]{512,1024,2048,4096}, new[]{0,20,35,50,70,100}, new[]{0,1,2}, new[]{0,1,2}, new[]{-4,-3,-2,-1,0,1,2,3,4}, new[]{0,1}
    };
    internal static readonly bool RunCheck=Environment.GetCommandLineArgs().Contains("-osowiec-video-check");
    static readonly string Prefix=RunCheck ? "Osowiec.TestVideo." : "Osowiec.Video.";
    string Key(Option option)=>Prefix+option;
    internal static bool HasSavedDisplay=>PlayerPrefs.HasKey(Prefix+"Width");
    public static bool DisplayOverridden=>Environment.GetCommandLineArgs().Any(a=>
        a.Equals("-screen-width",StringComparison.OrdinalIgnoreCase) ||
        a.Equals("-screen-height",StringComparison.OrdinalIgnoreCase) ||
        a.Equals("-screen-fullscreen",StringComparison.OrdinalIgnoreCase));
    readonly int[] current=new int[Values.Length];
    readonly int[] defaults=new int[Values.Length];
    internal OsowiecFieldSettings controls;
    internal OsowiecVideoJournal journal;
    RenderPipelineAsset previous;
    UniversalRenderPipelineAsset pipeline;
    int previousVSync,previousFPS,previousMips;
    Volume exposureVolume;
    ColorAdjustments grading;
    bool initialized;
    readonly Dictionary<UniversalAdditionalCameraData, (AntialiasingMode aa,AntialiasingQuality quality,bool post)> cameras=new();
    [Serializable] internal struct DisplayChoice
    {
        public int width,height; public FullScreenMode mode;
        public bool Same(DisplayChoice other)=>width==other.width && height==other.height && mode==other.mode;
        public static DisplayChoice Actual()=>new(){width=Screen.width,height=Screen.height,mode=Screen.fullScreenMode};
    }
    DisplayChoice before,pending;
    internal DisplayChoice Pending=>pending;
    internal List<Vector2Int> Sizes {get;}=new();
    internal float ConfirmUntil {get;private set;}
    internal bool Confirming=>ConfirmUntil>0;
    bool resetDisplay;

    static int Nearest(int[] options,int value)=>options.OrderBy(v=>Math.Abs(v-value)).First();
    public void Initialize(OsowiecFieldSettings settings)
    {
        if(initialized)return;
        initialized=true; controls=settings;
        previous=QualitySettings.renderPipeline;
        var asset=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if(asset)
        {
            pipeline=Instantiate(asset);pipeline.name=asset.name+" (настройки игрока)";
            pipeline.hideFlags=HideFlags.DontSave;
            QualitySettings.renderPipeline=pipeline;
        }
        previousVSync=QualitySettings.vSyncCount;previousFPS=Application.targetFrameRate;previousMips=QualitySettings.globalTextureMipmapLimit;
        int[] baseline={previousVSync,previousFPS<=0 ? -1:previousFPS, Mathf.RoundToInt((pipeline?pipeline.renderScale:1)*100),
            pipeline?pipeline.mainLightShadowmapResolution:2048, Mathf.RoundToInt(pipeline?pipeline.shadowDistance:50),
            0, previousMips, 0, 1};
        for(int i=0;i<current.Length;i++)
        {
            defaults[i]=Nearest(Values[i],baseline[i]);
            current[i]=Nearest(Values[i],PlayerPrefs.GetInt(Key((Option)i),defaults[i]));
        }
        if(!DisplayOverridden && HasSavedDisplay)
        {
            var saved=new DisplayChoice{width=PlayerPrefs.GetInt(Prefix+"Width"),height=PlayerPrefs.GetInt(Prefix+"Height"),
                mode=(FullScreenMode)PlayerPrefs.GetInt(Prefix+"Mode")};
            if(Valid(saved)) SetDisplay(saved);
        }
        var host=new GameObject("Яркость полевого журнала");
        host.transform.SetParent(transform,false);
        exposureVolume=host.AddComponent<Volume>();exposureVolume.isGlobal=true;exposureVolume.priority=100;
        exposureVolume.sharedProfile=ScriptableObject.CreateInstance<VolumeProfile>();
        exposureVolume.sharedProfile.hideFlags=HideFlags.DontSave;
        grading=exposureVolume.sharedProfile.Add<ColorAdjustments>();
        Apply();
        journal=gameObject.AddComponent<OsowiecVideoJournal>();
        RenderPipelineManager.beginCameraRendering+=CameraRender;
        if(RunCheck && gameObject.scene.name=="OsowiecHQ")gameObject.AddComponent<OsowiecVideoCheck>();
    }

    internal int Get(Option opt)=>current[(int)opt];
    internal void Step(Option opt,int sign)
    {
        int i=(int)opt; int[] values=Values[i];
        int next=Mathf.Clamp(Array.IndexOf(values,current[i])+sign,0,values.Length-1);
        current[i]=values[next]; PlayerPrefs.SetInt(Key(opt),current[i]); PlayerPrefs.Save();Apply();
    }
    internal bool CanStep(Option opt,int sign)
    {
        int i=(int)opt;
        int n=Array.IndexOf(Values[i],current[i])+sign;
        return n>=0 && n<Values[i].Length && (opt!=Option.Cap || Get(Option.VSync)==0);
    }

    void Apply()
    {
        Shader.SetGlobalFloat("_OsowiecFlatMaterials",Get(Option.Relief)==1?0:1);
        QualitySettings.vSyncCount=Get(Option.VSync);
        Application.targetFrameRate=Get(Option.VSync)!=0 ? -1:Get(Option.Cap);
        if(QualitySettings.globalTextureMipmapLimit!=Get(Option.Textures)) QualitySettings.globalTextureMipmapLimit=Get(Option.Textures);
        if(pipeline)
        {
            pipeline.renderScale=Get(Option.RenderScale)/100f;
            pipeline.mainLightShadowmapResolution=Get(Option.ShadowMap);
            pipeline.shadowDistance=Get(Option.ShadowDistance);
        }
        if(grading)
        {
            exposureVolume.weight=Get(Option.Exposure)==0 ? 0:1;
            grading.postExposure.Override((gameObject.scene.name=="Osowiec"?.08f:0)+Get(Option.Exposure)*.25f);
        }
        foreach(var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            PrepareCamera(cam);
    }
    void CameraRender(ScriptableRenderContext _,Camera cam)
    {
        if(cam && cam.gameObject.scene==gameObject.scene && cam.cameraType==CameraType.Game &&
            cam.TryGetComponent<UniversalAdditionalCameraData>(out var data) && !cameras.ContainsKey(data)) PrepareCamera(cam);
    }
    void PrepareCamera(Camera cam)
    {
        if(!cam || cam.gameObject.scene!=gameObject.scene || cam.cameraType!=CameraType.Game)return;
        var data=cam.GetUniversalAdditionalCameraData();
        if(data.renderType!=CameraRenderType.Base)return;
        if(!cameras.ContainsKey(data)) cameras.Add(data,(data.antialiasing,data.antialiasingQuality,data.renderPostProcessing));
        data.antialiasing=Get(Option.AntiAliasing) switch {1=>AntialiasingMode.FastApproximateAntialiasing,
            2=>AntialiasingMode.SubpixelMorphologicalAntiAliasing,_=>AntialiasingMode.None};
        data.antialiasingQuality=AntialiasingQuality.High;
        if(Get(Option.AntiAliasing)!=0 || Get(Option.Exposure)!=0) data.renderPostProcessing=true;
        else data.renderPostProcessing=cameras[data].post || gameObject.scene.name=="Osowiec";
    }

    static bool Valid(DisplayChoice d)=>d.width>=640 && d.height>=360 && d.width<=16384 && d.height<=16384 &&
        (d.mode==FullScreenMode.Windowed || d.mode==FullScreenMode.FullScreenWindow || d.mode==FullScreenMode.ExclusiveFullScreen);
    static void SetDisplay(DisplayChoice d)=>Screen.SetResolution(d.width,d.height,d.mode);
    public void Show(Transform page)
    {
        if(!initialized)return;
        if(!Confirming)pending=DisplayChoice.Actual();
        Sizes.Clear();
        var found=new HashSet<Vector2Int>{new(pending.width,pending.height)};
        foreach(var r in Screen.resolutions) if(r.width>=960 && r.height>=540)found.Add(new(r.width,r.height));
        int maxW=Mathf.Max(Display.main.systemWidth,pending.width), maxH=Mathf.Max(Display.main.systemHeight,pending.height);
        foreach(var size in new[]{new Vector2Int(960,540),new Vector2Int(1280,720),new Vector2Int(1366,768),new Vector2Int(1600,900),new Vector2Int(1920,1080),new Vector2Int(2560,1440)})
            if(size.x<=maxW && size.y<=maxH)found.Add(size);
        Sizes.AddRange(found.OrderBy(v=>v.x).ThenBy(v=>v.y));
        journal.Open(this,page);
    }

    internal static readonly FullScreenMode[] Modes={FullScreenMode.Windowed,FullScreenMode.FullScreenWindow,FullScreenMode.ExclusiveFullScreen};
    internal void StepDisplay(bool mode,int direction)
    {
        if(Confirming)return;
        if(mode)pending.mode=Modes[Mathf.Clamp(Array.IndexOf(Modes,pending.mode)+direction,0,Modes.Length-1)];
        else
        {
            var target=Sizes[Mathf.Clamp(Sizes.IndexOf(new(pending.width,pending.height))+direction,0,Sizes.Count-1)];
            pending.width=target.x;pending.height=target.y;
        }
    }
    internal void PreviewDisplay()
    {
        if(Confirming || !Valid(pending) || DisplayOverridden)return;
        before=DisplayChoice.Actual();
        SetDisplay(pending); ConfirmUntil=Time.realtimeSinceStartup+12f;
    }
    internal void ConfirmDisplay()
    {
        if(!Confirming)return;
        var actual=DisplayChoice.Actual();
        if(resetDisplay)ForgetDisplay();
        else
        {
            PlayerPrefs.SetInt(Prefix+"Width",actual.width);PlayerPrefs.SetInt(Prefix+"Height",actual.height);
            PlayerPrefs.SetInt(Prefix+"Mode",(int)actual.mode);
            if(!RunCheck)PlayerPrefs.SetInt(OsowiecFieldSettings.OldDisplayKey,Screen.fullScreen?1:0);
        }
        resetDisplay=false;
        PlayerPrefs.Save();ConfirmUntil=0;pending=actual;
    }
    internal void RevertDisplay()
    {
        if(!Confirming)return;
        ConfirmUntil=0;resetDisplay=false;SetDisplay(before);pending=before;
    }
    static void ForgetDisplay()
    {
        foreach(string k in new[]{"Width","Height","Mode"})PlayerPrefs.DeleteKey(Prefix+k);
        if(!RunCheck)PlayerPrefs.DeleteKey(OsowiecFieldSettings.OldDisplayKey);
    }
    internal void ResetAll()
    {
        RevertDisplay();
        bool restoreDesktop=HasSavedDisplay && !DisplayOverridden;
        for(int i=0;i<current.Length;i++) {PlayerPrefs.DeleteKey(Key((Option)i));current[i]=defaults[i];}
        pending=DisplayChoice.Actual();
        if(restoreDesktop)
        {
            pending=new DisplayChoice{width=Display.main.systemWidth,height=Display.main.systemHeight,mode=FullScreenMode.FullScreenWindow};
            if(pending.Same(DisplayChoice.Actual()))ForgetDisplay();
            else {PreviewDisplay();resetDisplay=Confirming;}
        }
        PlayerPrefs.Save();
        controls.FovControl.value=60;controls.ScaleControl.value=1; controls.EffectsControl.isOn=false;controls.ShakeControl.isOn=false;
        Apply();
    }
    void Update()
    {
        if(Confirming && (Time.realtimeSinceStartup>=ConfirmUntil || !journal || !journal.Visible))
        {RevertDisplay();if(journal && journal.Visible)journal.Refresh();}
    }
    void OnDestroy()
    {
        if(!initialized)return;
        RevertDisplay();RenderPipelineManager.beginCameraRendering-=CameraRender;
        if(QualitySettings.renderPipeline==pipeline) QualitySettings.renderPipeline=previous;
        if(pipeline)Destroy(pipeline);
        if(exposureVolume && exposureVolume.sharedProfile)Destroy(exposureVolume.sharedProfile);
        foreach(var entry in cameras)
        {
            if(!entry.Key)continue;
            entry.Key.antialiasing=entry.Value.aa;entry.Key.antialiasingQuality=entry.Value.quality;
            entry.Key.renderPostProcessing=entry.Value.post;
        }
        Shader.SetGlobalFloat("_OsowiecFlatMaterials",0);
        QualitySettings.vSyncCount=previousVSync; Application.targetFrameRate=previousFPS;
        QualitySettings.globalTextureMipmapLimit=previousMips;
    }
}
