using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Standalone camera survey. This object exists only when the explicit player flag is present.
public sealed class OsowiecFieldVisualCheck : MonoBehaviour
{
    const string Flag="-osowiec-field-visuals";
    static string output;
    Camera eye;
    RenderTexture target;
    readonly StringBuilder report=new();
    readonly Color key=new(1,0,1,1);
    int width,height;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args=Environment.GetCommandLineArgs();int n=Array.IndexOf(args,Flag);
        if(n<0||n+1>=args.Length||FindAnyObjectByType<OsowiecFieldVisualCheck>())return;
        output=Path.GetFullPath(args[n+1]);Directory.CreateDirectory(output);
        var host=new GameObject("Osowiec field camera survey");DontDestroyOnLoad(host);
        host.AddComponent<OsowiecFieldVisualCheck>();
    }

    void OnEnable()=>Application.logMessageReceived+=Fail;
    void OnDisable()=>Application.logMessageReceived-=Fail;
    void Fail(string message,string trace,LogType type)
    {
        if(type!=LogType.Exception)return;
        File.WriteAllText(Path.Combine(output,"field-review.txt"),report+"\nFAIL "+message+"\n"+trace);
        Application.Quit(2);
    }
    static void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}

    void Look(Vector3 from,Vector3 at,float fov)
    {
        eye.transform.SetPositionAndRotation(from,Quaternion.LookRotation(at-from,Vector3.up));
        eye.fieldOfView=fov;
    }

    Texture2D Frame(string label)
    {
        var old=RenderTexture.active;
        var im=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            eye.Render();RenderTexture.active=target;
            im.ReadPixels(new Rect(0,0,width,height),0,0,false);im.Apply(false);
            if(label!=null)File.WriteAllBytes(Path.Combine(output,label+".png"),im.EncodeToPNG());
            return im;
        }
        finally{RenderTexture.active=old;}
    }
    void Photo(string label)
    {
        var im=Frame(label);Destroy(im);
        report.AppendLine($"{label}.png eye={eye.transform.position:F2} forward={eye.transform.forward:F3}");
    }
    void RuinPhoto(MeshCollider ruin)
    {
        var b=ruin.bounds;
        var visible=new List<Vector3>();
        for(float z=b.min.z;z<=b.max.z;z+=.13f)
        for(float x=b.min.x;x<=b.max.x;x+=.13f)
        {
            var ray=new Ray(new Vector3(x,b.max.y+16,z),Vector3.down);
            if(Physics.Raycast(ray,out var hit,32,~0,QueryTriggerInteraction.Ignore) && hit.collider==ruin)
                visible.Add(hit.point);
        }
        Require(visible.Count>10,"Ruined brickwork is buried or missing from the field.");
        var top=visible.OrderByDescending(p=>p.y).Take(Mathf.Max(6,visible.Count/2)).ToArray();
        var aim=top.Aggregate(Vector3.zero,(sum,p)=>sum+p)/top.Length-Vector3.up*.16f;
        int best=-1;Vector3 position=default;
        var renderer=ruin.GetComponent<Renderer>();
        var preview=RenderTexture.GetTemporary(240,135,24,RenderTextureFormat.ARGB32);
        var readback=new Texture2D(240,135,TextureFormat.RGB24,false);
        var prior=RenderTexture.active;
        int Pixels(Vector3 from)
        {
            Look(from,aim,49);
            Color32[] Sample(bool hidden)
            {
                renderer.forceRenderingOff=hidden;eye.Render();RenderTexture.active=preview;
                readback.ReadPixels(new Rect(0,0,240,135),0,0,false);readback.Apply(false);
                return readback.GetPixels32();
            }
            var on=Sample(false);var off=Sample(true);renderer.forceRenderingOff=false;
            int count=0;
            for(int k=0;k<on.Length;k++)
                if(Math.Abs(on[k].r-off[k].r)+Math.Abs(on[k].g-off[k].g)+Math.Abs(on[k].b-off[k].b)>10)count++;
            return count;
        }
        try
        {
            eye.targetTexture=preview;
            for(int n=0;n<12;n++)
            {
                float a=(n+.5f)*Mathf.PI/6;
                var candidate=aim+new Vector3(Mathf.Sin(a)*3.5f,2.3f,Mathf.Cos(a)*3.5f);
                if(Physics.Raycast(candidate+Vector3.up*22,Vector3.down,out var floor,42,~0,QueryTriggerInteraction.Ignore))
                    candidate.y=Mathf.Max(candidate.y,floor.point.y+1.6f);
                int score=Pixels(candidate);
                if(score>best){best=score;position=candidate;}
            }
        }
        finally
        {
            renderer.forceRenderingOff=false;eye.targetTexture=target;RenderTexture.active=prior;
            RenderTexture.ReleaseTemporary(preview);Destroy(readback);
        }
        Require(best>180,"Ruined wall is occluded in the review.");
        Look(position,aim,49);Photo("06-brick-ruin");
        report.AppendLine($"Brickwork exposes {visible.Count} overhead samples; {best} visible pixels in 240x135 renderer-on/off capture.");
    }
    Color32[] OnlyRiver(Renderer[] water)
    {
        int[] old=water.Select(r=>r.gameObject.layer).ToArray();
        var data=eye.GetUniversalAdditionalCameraData();
        var clear=eye.clearFlags;var bg=eye.backgroundColor;int mask=eye.cullingMask;
        bool post=data.renderPostProcessing;
        try
        {
            foreach(var r in water)r.gameObject.layer=30;
            eye.cullingMask=1<<30;eye.clearFlags=CameraClearFlags.SolidColor;eye.backgroundColor=key;
            data.renderPostProcessing=false;
            Shader.SetGlobalFloat("_OsowiecReviewSuppressRainRings",1);
            var im=Frame(null);var pixels=im.GetPixels32();Destroy(im);return pixels;
        }
        finally
        {
            Shader.SetGlobalFloat("_OsowiecReviewSuppressRainRings",0);
            for(int i=0;i<water.Length;i++)water[i].gameObject.layer=old[i];
            eye.cullingMask=mask;eye.clearFlags=clear;eye.backgroundColor=bg;data.renderPostProcessing=post;
        }
    }
    void RiverDelta(Color32[] a,Color32[] b,float dt,int sample)
    {
        int count=0,moved=0;long sum=0;
        var surface=new Plane(Vector3.up,new Vector3(0,-.39f,0));
        int minX=width,maxX=0,minY=height,maxY=0;
        for(int y=0;y<height;y+=2)
        for(int x=0;x<width;x+=2)
        {
            var ray=eye.ViewportPointToRay(new Vector3((x+.5f)/width,(y+.5f)/height,0));
            if(!surface.Raycast(ray,out float d))continue;
            var p=ray.GetPoint(d);
            if(p.x< -34.5f||p.x> -30.7f||p.z<8.9f||p.z>12.6f)continue;
            var c=a[y*width+x];var e=b[y*width+x];
            if(c.r>220 && c.b>220 && c.g<30 || e.r>220 && e.b>220 && e.g<30)continue;
            int rgb=Math.Abs(c.r-e.r)+Math.Abs(c.g-e.g)+Math.Abs(c.b-e.b);
            sum+=rgb;count++;if(rgb>=3)moved++;
            minX=Math.Min(x,minX);maxX=Math.Max(x,maxX);minY=Math.Min(y,minY);maxY=Math.Max(y,maxY);
        }
        double mean=sum/(Math.Max(count,1)*3.0),fraction=(double)moved/Math.Max(1,count);
        report.AppendLine(FormattableString.Invariant($"water pair {sample}: dt={dt:F3}s, world x=[-34.5,-30.7] z=[8.9,12.6], pixel rectangle ({minX},{minY})-({maxX},{maxY}), bottom-left origin, samples={count}, mean abs RGB/255={mean:F3}, fraction with RGB sum difference >=3: {fraction:P2}"));
        Require(count>180 && mean>.25 && fraction>.12,"No measurable river motion in the isolated channel patch.");
    }

    IEnumerator Start()
    {
        Application.runInBackground=true;
        GameManager.SetGameMode(GameMode.PlayerVsBot);SceneManager.LoadScene("Osowiec");
        for(int i=0;i<8;i++)yield return null;
        width=Screen.width;height=Screen.height;
        var host=new GameObject("Field review camera");eye=host.AddComponent<Camera>();
        eye.enabled=false;eye.clearFlags=CameraClearFlags.Skybox;eye.allowHDR=true;eye.nearClipPlane=.07f;
        eye.farClipPlane=350;eye.aspect=(float)width/height;
        var data=eye.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
        data.requiresDepthTexture=true;data.requiresColorTexture=true;
        target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        eye.targetTexture=target;
        var water=FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r=>r.name.StartsWith("River_Biebrza_",StringComparison.Ordinal)).Cast<Renderer>().ToArray();
        Require(water.Length>0,"Missing main river mesh.");
        Require(!FindObjectsByType<Renderer>(FindObjectsSortMode.None).Any(r=>r.gameObject.layer==30),"Isolated survey layer already in use.");
        var placement=FindAnyObjectByType<ArmyDeploymentController>();var paper=FindAnyObjectByType<OsowiecPaperPlacement>();
        Require(placement && paper && placement.TryPlaceOfferAtGridCell(0,OsowiecDeploymentSectors.Cells[0][0]),"Could not deploy test soldier.");
        paper.Confirm();yield return null;
        report.AppendLine($"Standalone field review {width}x{height}; isolated water only, postprocessing and rain rings disabled for numerical samples.");
        Look(new Vector3(-38.4f,5.7f,1.2f),new Vector3(-33.2f,-.39f,10.8f),49);
        Photo("01-river");var first=OnlyRiver(water);float stamp=Time.time;
        yield return new WaitForSeconds(.62f);
        Photo("02-river");var next=OnlyRiver(water);RiverDelta(first,next,Time.time-stamp,1);first=next;stamp=Time.time;
        yield return new WaitForSeconds(.64f);
        Photo("03-river");next=OnlyRiver(water);RiverDelta(first,next,Time.time-stamp,2);
        var events=FindAnyObjectByType<OsowiecGroundEvents>();
        var streak=events.transform.Find("Distant passing shell · horizon trail").GetComponent<LineRenderer>();
        float deadline=Time.realtimeSinceStartup+80;
        while(Time.realtimeSinceStartup<deadline)
        {
            yield return null;
            if(!streak.enabled || Vector3.Distance(streak.GetPosition(0),streak.GetPosition(4))<2)continue;
            var spot=(streak.GetPosition(0)+streak.GetPosition(4))*.5f;
            Look(spot+new Vector3(-6,1.6f,-17),spot-Vector3.up*.8f,49);
            var pixelA=eye.WorldToViewportPoint(streak.GetPosition(0));var pixelB=eye.WorldToViewportPoint(streak.GetPosition(4));
            Photo("04-incoming");
            report.AppendLine($"Live LineRenderer endpoints {streak.GetPosition(0):F2} to {streak.GetPosition(4):F2}; viewport {pixelA:F3} to {pixelB:F3}.");
            break;
        }
        Require(File.Exists(Path.Combine(output,"04-incoming.png")),"No live cosmetic flight appeared.");
        Look(new Vector3(-8,11,4),new Vector3(-3.4f,.65f,14.8f),42);Photo("05-gas-bank");
        var ruin=events.transform.Find("Damaged brick wall at the eastern approach").GetComponent<MeshCollider>();
        RuinPhoto(ruin);
        var roof=events.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Battery north pavilion roof");
        var crest=roof.bounds.center;Look(crest+new Vector3(-6,7,9),crest,46);Photo("07-earth-roof");
        var pole=new Vector3(-31,0,-13);
        var hits=Physics.RaycastAll(pole+Vector3.up*20,Vector3.down,45,~0,QueryTriggerInteraction.Ignore);
        pole.y=hits.Where(h=>h.collider.name.StartsWith("Biebrza ground ",StringComparison.Ordinal)).Select(h=>h.point.y).First()+4.6f;
        Look(pole+new Vector3(-3.1f,1.7f,-1.8f),pole-Vector3.up*.45f,49);Photo("08-signal-lines");
        report.AppendLine("PASS");File.WriteAllText(Path.Combine(output,"field-review.txt"),report.ToString());
        eye.targetTexture=null;RenderTexture.ReleaseTemporary(target);Destroy(host);
        Application.Quit(0);
    }
}
