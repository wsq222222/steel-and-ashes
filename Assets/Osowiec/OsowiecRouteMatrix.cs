using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Loads independent fixtures, then steers the normal FPS capsule to its confirmed map marker.
[DefaultExecutionOrder(510)]
public sealed class OsowiecRouteMatrix : MonoBehaviour
{
    int run,stage,firstFrame,corner;
    GameManager previous;
    Unit hound;
    Gamepad pad;
    InputAction move;
    Vector3[] corners;
    Vector3 marker;
    float walked,requested,planned,deadline,startHeight;
    Vector3 last;
    string exception;
    string trackOutput;
    readonly List<Vector3> mudPrints = new(), boardPrints = new();
    readonly HashSet<bool> soles = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(Application.isEditor || !Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-osowiec-route-matrix")) return;
        var host=new GameObject("Order endpoint regression");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecRouteMatrix>();
    }

    void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        int at=Array.IndexOf(args,"-osowiec-track-frames");
        if(at>=0 && at+1<args.Length)
        {
            trackOutput=args[at+1];
            Directory.CreateDirectory(trackOutput);
        }
        Application.targetFrameRate=60;
        pad=InputSystem.AddDevice<Gamepad>();
        move=new InputAction("March to order",InputActionType.Value,"<Gamepad>/leftStick");
        move.performed+=Move; move.canceled+=Move; move.Enable();
        Application.logMessageReceived+=Log;
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        ResetScene();
    }

    void Move(InputAction.CallbackContext evt) { if(hound) hound.OnMove(evt); }
    void Log(string message,string stack,LogType type) { if(type==LogType.Exception) exception=message; }
    void OnDestroy()
    {
        Application.logMessageReceived-=Log;
        move?.Dispose();
        if(pad!=null && pad.added) InputSystem.RemoveDevice(pad);
    }

    void ResetScene()
    {
        stage=0; deadline=Time.realtimeSinceStartup+45; hound=null;
        SceneManager.LoadScene("Osowiec");
    }

    static bool Walkable(Vector3 point,out Vector3 floor)
    {
        bool found=NavMesh.SamplePosition(point,out var hit,2,NavMesh.AllAreas);
        floor=hit.position;
        return found;
    }

    static Vector3[] PathBetween(Vector3 start,Vector3 end)
    {
        var path=new NavMeshPath();
        if(!Walkable(start,out var a) || !Walkable(end,out var b) ||
            !NavMesh.CalculatePath(a,b,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete) return null;
        return path.corners;
    }

    static (Vector3,Vector3) Slope()
    {
        // Pick a sloping part of baked, traversable terrain within the entrance area.
        for(int x=-44;x<=-22;x+=2)
        for(int z=-22;z<=-8;z+=2)
        foreach(var delta in new[]{new Vector3(4,0,0),new Vector3(0,0,4)})
        {
            var candidate=new Vector3(x,1,z);
            var path=PathBetween(candidate,candidate+delta);
            if(path==null || path.Length<2) continue;
            float rise=Mathf.Abs(path[^1].y-path[0].y);
            float direct=Vector3.Distance(path[0],path[^1]);
            if(rise>.42f && rise<1.25f && direct>3 && LengthOf(path)<direct*1.5f)
                return (path[0],path[^1]);
        }
        throw new InvalidOperationException("No traversable slope found in entrance area");
    }

    static Vector3 AtDistance(Vector3[] path,float distance)
    {
        for(int i=1;i<path.Length;i++)
        {
            float length=Vector3.Distance(path[i-1],path[i]);
            if(distance<=length) return Vector3.MoveTowards(path[i-1],path[i],distance);
            distance-=length;
        }
        return path[^1];
    }

    static float LengthOf(Vector3[] route)
    {
        float total=0;
        for(int i=1;i<route.Length;i++)total+=(route[i]-route[i-1]).magnitude;
        return total;
    }

    static Vector3 TrenchDestination(Vector3 start,Vector3[] route)
    {
        // The navigation solver can select a different side of a corner when a
        // point on the full route becomes a destination. Query that path directly.
        const float desired=28.5f;
        float along=desired;
        for(int i=0;i<10;i++){
            Vector3 target=AtDistance(route,along);
            var proposal=PathBetween(start,target);
            if(proposal==null)break;
            float error=desired-LengthOf(proposal);
            if(Mathf.Abs(error)<.08f)return target;
            along+=error;
        }
        // At a corridor split the distance can jump across the requested value.
        // Survey nearby endpoints inside the same patch of navigable trench.
        Vector3 centre=AtDistance(route,desired);
        for(int ring=1;ring<=20;ring++)for(int ix=-ring;ix<=ring;ix++)for(int iz=-ring;iz<=ring;iz++){
            if(Mathf.Max(Mathf.Abs(ix),Mathf.Abs(iz))!=ring)continue;
            var trial=centre+new Vector3(ix*.12f,0,iz*.12f);
            if(!NavMesh.SamplePosition(trial,out var snapped,.36f,NavMesh.AllAreas) ||
                Vector3.ProjectOnPlane(snapped.position-trial,Vector3.up).magnitude>.08f)continue;
            var proposal=PathBetween(start,snapped.position);
            if(proposal!=null && Mathf.Abs(LengthOf(proposal)-desired)<.07f)return snapped.position;
        }
        throw new InvalidOperationException("No surveyed 28.5 m trench order on the baked navigation surface");
    }

    static void VerifyTrench(Vector3[] route)
    {
        int samples=0;
        for(int i=1;i<route.Length;i++){
            var a=route[i-1];var b=route[i];var delta=b-a;
            if(delta.sqrMagnitude<.00001f)continue;
            int steps=Mathf.CeilToInt(delta.magnitude/.20f);
            Vector3 previousFloor=default;
            for(int j=0;j<=steps;j++){
                var p=Vector3.Lerp(a,b,(float)j/steps);
                // Path corners interpolate elevations across a bend. Sample the actual
                // floor below each XZ coordinate before sweeping against the walls.
                if(!NavMesh.SamplePosition(p,out var nav,2f,NavMesh.AllAreas) ||
                    Vector3.ProjectOnPlane(nav.position-p,Vector3.up).magnitude>.08f)
                    throw new InvalidOperationException($"Trench route leaves the navigation surface at {p}; nearest {nav.position}");
                if(j>0){
                    Vector3 move=nav.position-previousFloor;
                    foreach(float height in new[]{.32f,1.0f})
                    foreach(var hit in Physics.SphereCastAll(previousFloor+Vector3.up*height,.045f,move.normalized,
                        move.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){
                        if(hit.normal.y<.55f && !hit.collider.GetComponentInParent<Unit>())
                            throw new InvalidOperationException($"Trench stroke crosses {hit.collider.name} at {hit.point} on leg {i}");
                    }
                }
                previousFloor=nav.position;
            }
            samples+=steps+1;
        }
        Debug.Log($"[Osowiec route matrix] trench stroke clear over {route.Length-1} navigation legs, {samples} floor samples and wall sweeps at two heights between samples");
    }

    void PrepareOrder()
    {
        Vector3 start=hound.transform.position,end=OsowiecOperation.Telephone;
        if(run==1) { start=new Vector3(2,0,-12); end=new Vector3(9,0,-12); }
        if(run==2) (start,end)=Slope();
        var path=PathBetween(start,end);
        if(path==null || path.Length<2) throw new InvalidOperationException("Fixture is unreachable");
        if(run!=0)
        {
            var cc=hound.GetComponent<CharacterController>();
            cc.enabled=false; hound.transform.position=path[0]+Vector3.up*.02f; cc.enabled=true;
        }
        var planner=OsowiecPlanner.Instance;
        planner.Select(hound);
        var target=run==0?TrenchDestination(hound.transform.position,path):path[^1];
        var reference=PathBetween(hound.transform.position,target);
        if(reference==null)throw new InvalidOperationException("Independent navigation order failed");
        if(run==0)VerifyTrench(reference);
        float expectedRequested=LengthOf(reference);
        float budget=OsowiecPlanner.MoveBudget(hound);
        bool free=OsowiecMarch.Free(hound);
        float expectedReach=!free && expectedRequested>budget?budget-.18f:expectedRequested;
        Vector3 expectedStop=AtDistance(reference,expectedReach);
        planner.Preview(target);
        requested=planner.RequestedMetres; planned=planner.PlannedMetres;
        if(run==0 && (Mathf.Abs(requested-28.5f)>.35f || Mathf.Abs(planned-expectedReach)>.03f))
            throw new InvalidOperationException($"Unexpected split {requested:F2}/{planned:F2}");
        marker=planner.OrderEndpoint;
        corners=planner.ReachableCorners;
        if(corners==null || corners.Length<2 || Vector3.Distance(corners[^1],marker)>.001f)
            throw new InvalidOperationException("Order endpoint is missing");
        if(Mathf.Abs(requested-expectedRequested)>.025f || Mathf.Abs(planned-expectedReach)>.025f ||
           Mathf.Abs(LengthOf(corners)-expectedReach)>.025f || Vector3.Distance(expectedStop,marker)>.04f)
            throw new InvalidOperationException($"Independent path {expectedRequested:F3}/{expectedReach:F3} to {expectedStop} disagrees with order {requested:F3}/{planned:F3} to {marker}");
        Debug.Log($"[Osowiec route matrix] independent fixture={run} length={expectedRequested:F3} reachable={expectedReach:F3} endpointError={Vector3.Distance(expectedStop,marker):F3}");
        planner.ConfirmOrder();
        corner=1; walked=0; last=hound.transform.position;
        startHeight=last.y;
        stage=2;
    }

    void LateUpdate()
    {
        try { Tick(); }
        catch(Exception e)
        {
            Debug.LogError($"[Osowiec route matrix] FAIL fixture={run} {e.Message}");
            enabled=false; Application.Quit(2);
        }
    }

    void Tick()
    {
        if(exception!=null) throw new InvalidOperationException(exception);
        if(Time.realtimeSinceStartup>deadline) throw new TimeoutException($"stage={stage}, position={last}, endpoint={marker}, walked={walked:F2}");
        if(stage==0)
        {
            var game=GameManager.Instance;
            if(!game || game==previous || !game.IsArmyDeploymentPhase()) return;
            var deploy=FindFirstObjectByType<ArmyDeploymentController>();
            if(!deploy || deploy.GetPointsRemaining()!=12) return;
            var valid=OsowiecDeploymentSectors.Cells[0][0];
            if(!deploy.TryPlaceOfferAtGridCell(2,valid)) throw new InvalidOperationException("Scout deployment failed");
            hound=FindObjectsByType<Unit>(FindObjectsSortMode.None).Single(u=>u.owner==Player.Player1);
            hound.GetComponent<OsowiecBootContact>().ImpressionPlanted+=RecordImpression;
            FindFirstObjectByType<OsowiecPaperPlacement>().Confirm();
            if(game.IsArmyDeploymentPhase()) throw new InvalidOperationException("Deployment confirmation refused.");
            previous=game; firstFrame=Time.frameCount+3; stage=1; return;
        }
        if(stage==1) { if(Time.frameCount>=firstFrame) PrepareOrder(); return; }
        if(!hound.IsControlled()) return;
        var p=hound.transform.position;
        walked+=Vector3.ProjectOnPlane(p-last,Vector3.up).magnitude; last=p;
        var d=Vector3.ProjectOnPlane(corners[corner]-p,Vector3.up);
        if(d.magnitude<.14f && corner<corners.Length-1) { corner++; return; }
        float error=Vector3.ProjectOnPlane(p-marker,Vector3.up).magnitude;
        if(corner==corners.Length-1 && (error<.16f || hound.GetRemainingMoveMeters()<.03f))
        {
            InputState.Change(pad,new GamepadState());
            if(error>.40f || walked<2f) throw new InvalidOperationException($"stop misses marker by {error:F2} m");
            if(run==2 && Mathf.Abs(p.y-startHeight)<.25f)
                throw new InvalidOperationException($"Slope capsule changed altitude by only {p.y-startHeight:F2} m");
            var tracks=FindFirstObjectByType<OsowiecWetTracks>();
            if(run==0)
            {
                if(!tracks || boardPrints.Count<2 || mudPrints.Count<2 || soles.Count!=2)
                    throw new InvalidOperationException($"Player left {boardPrints.Count} timber and {mudPrints.Count} earth prints, {soles.Count} shoes.");
                var footprint=mudPrints[mudPrints.Count/2];
                float lowest=0,highest=0;
                for(int z=-9;z<=9;z++) for(int x=-9;x<=9;x++)
                {
                    float h=tracks.SoilHeightAt(footprint+new Vector3(x*.025f,0,z*.025f));
                    lowest=Mathf.Min(lowest,h);highest=Mathf.Max(highest,h);
                }
                if(lowest>-.028f || highest<.005f)
                    throw new InvalidOperationException($"Mud height field missing heel or rim: {lowest:F4}/{highest:F4} m.");
                Debug.Log($"[Osowiec tracks] soil height field at {footprint}: heel={lowest:F4}m rim={highest:F4}m");
                Debug.Log($"[Osowiec tracks] genuine player steps timber={boardPrints.Count} mud={mudPrints.Count} distinct shoes={soles.Count}");
                if(trackOutput!=null)
                {
                    CapturePrint(tracks,boardPrints[1],"timber");
                    CapturePrint(tracks,footprint,"mud");
                    CheckRenderedDepth(tracks,footprint);
                    CheckGalleryBank(tracks);
                    CaptureTrail(tracks);
                }
            }
            Debug.Log($"[Osowiec tracks] route={run} boot contacts={tracks.PlantedBoots}");
            Debug.Log($"[Osowiec route matrix] PASS fixture={run} requested={requested:F2} planned={planned:F2} walked={walked:F2} stop_error={error:F3}");
            if(++run==3) { enabled=false; Application.Quit(0); } else ResetScene();
            return;
        }
        Vector3 local=hound.transform.InverseTransformDirection(d.normalized);
        InputState.Change(pad,new GamepadState {leftStick=new Vector2(local.x,local.z)});
    }

    void RecordImpression(RaycastHit hit,bool left)
    {
        if(run!=0)return;
        var surface=hit.collider.GetComponentInParent<OsowiecAcousticSurface>();
        if(surface && surface.surface==OsowiecGroundSurface.Timber)boardPrints.Add(hit.point);
        else if(OsowiecWetTracks.IsSoil(hit))mudPrints.Add(hit.point);
        soles.Add(left);
    }

    // Read the production DepthOnly pass on the actual walked triangle. The depth
    // pair distinguishes a physical excavation from a change in albedo or normals.
    void CheckGalleryBank(OsowiecWetTracks tracks)
    {
        var bank=GameObject.Find("Battery gallery earthwork").GetComponent<MeshCollider>();
        var mesh=bank.sharedMesh;
        var vertices=mesh.vertices;
        var indices=mesh.triangles;
        for(int i=0;i<indices.Length;i+=3)
        {
            Vector3 centre=bank.transform.TransformPoint((vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3f);
            Vector3 above=new(centre.x,bank.bounds.max.y+.25f,centre.z);
            if(!bank.Raycast(new Ray(above,Vector3.down),out var hit,bank.bounds.size.y+.5f) ||
                hit.normal.y<.75f || !OsowiecWetTracks.IsSoil(hit)) continue;
            // There is a survey tile beneath this second surface at the same map address.
            Vector3 ball=hit.point+Vector3.forward*.075f;
            float lower=tracks.SoilHeightAt(ball);
            if(!OsowiecWetTracks.Boot(hit,Vector3.forward,true) ||
                tracks.SoilHeightAt(ball,true)>-.020f || tracks.SoilHeightAt(ball)!=lower)
                throw new InvalidOperationException("Gallery bank height escaped its own surface.");
            CheckRenderedDepth(tracks,hit.point,bank.GetComponent<Renderer>(),"gallery");
            return;
        }
        throw new InvalidOperationException("No gallery earthwork crown triangle received the test boot.");
    }

    void CheckRenderedDepth(OsowiecWetTracks tracks,Vector3 target,Renderer floor=null,string substrate="mud")
    {
        const int side=112;
        if(!floor)
        {
            var ray=new Ray(target+Vector3.up*.30f,Vector3.down);
            var hits=Physics.RaycastAll(ray,.65f,~0,QueryTriggerInteraction.Ignore);
            floor=hits.Where(OsowiecWetTracks.IsSoil).OrderBy(hit=>hit.distance).First().collider.GetComponent<Renderer>();
        }
        var host=new GameObject("Mud depth survey");
        host.transform.position=target+Vector3.up*1.1f;
        host.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
        var camera=host.AddComponent<Camera>();
        camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.27f;camera.aspect=1;
        camera.nearClipPlane=.05f;camera.farClipPlane=2.1f;
        var rt=RenderTexture.GetTemporary(side,side,24,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);
        var sample=new Texture2D(side,side,TextureFormat.RFloat,false,true);
        var cmd=new CommandBuffer {name="Measure rendered boot cavity"};
        var previous=RenderTexture.active;
        bool visible=tracks.ImpressionsVisible;
        int eye=Shader.PropertyToID("_WorldSpaceCameraPos");
        Vector4 previousEye=Shader.GetGlobalVector(eye);
        try
        {
            float[] bare=null;
            foreach(bool marked in new[]{false,true})
            {
                tracks.ShowImpressions(marked);
                cmd.Clear();
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true,true,Color.clear);
                // The same API as URP's ScriptableRenderer converts the camera matrix
                // and depth clear to the active GPU convention when executing the draw.
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
                cmd.SetGlobalVector(eye,host.transform.position);
                var materials=floor.sharedMaterials;
                for(int sub=0;sub<materials.Length;sub++)
                {
                    var mat=materials[sub];int pass=mat.FindPass("DepthOnly");
                    if(pass<0) throw new InvalidOperationException("Missing depth pass for surveyed earth.");
                    cmd.DrawRenderer(floor,mat,sub,pass);
                }
                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active=rt;
                sample.ReadPixels(new Rect(0,0,side,side),0,0);sample.Apply();
                var data=sample.GetPixelData<float>(0);
                if(!marked) {bare=data.ToArray();continue;}
                float depression=0,rim=0;
                int sunken=0;
                for(int i=0;i<bare.Length;i++)
                {
                    if(bare[i]==0 || data[i]==0) continue;
                    float h=(data[i]-bare[i])*(camera.farClipPlane-camera.nearClipPlane)*(SystemInfo.usesReversedZBuffer?1:-1);
                    depression=Mathf.Min(depression,h);rim=Mathf.Max(rim,h);
                    if(h<-.015f) sunken++;
                }
                string result=$"GPU depth at {target}: depression={depression:F4}m rim={rim:F4}m pixels_over_15mm={sunken}.";
                if(depression>-.020f || rim<.002f || sunken<75)
                    throw new InvalidOperationException(result);
                File.WriteAllText(Path.Combine(trackOutput,substrate+"-depth.txt"),$"PASS {Application.buildGUID}. {result}");
                Debug.Log("[Osowiec tracks] "+result);
            }
        }
        finally
        {
            tracks.ShowImpressions(visible);
            Shader.SetGlobalVector(eye,previousEye);
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(rt);cmd.Release();Destroy(sample);Destroy(host);
        }
    }

    void CaptureTrail(OsowiecWetTracks tracks)
    {
        var host=new GameObject("Inspect a sequence of planted boots");
        var camera=host.AddComponent<Camera>();
        camera.enabled=false;camera.cullingMask=~(1<<5);camera.nearClipPlane=.04f;
        bool visible=tracks.ImpressionsVisible;
        var original=new Vector3(-30.64f,0,-10.49f);
        var prior=RenderTexture.active;
        const int width=480,height=300;
        var probe=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var frame=new Texture2D(width,height,TextureFormat.RGB24,false);
        void Render(Texture2D photo,RenderTexture target)
        {
            camera.aspect=(float)target.width/target.height;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            photo.ReadPixels(new Rect(0,0,target.width,target.height),0,0);photo.Apply();
        }
        void Save(string file,int w,int h)
        {
            var large=RenderTexture.GetTemporary(w,h,24,RenderTextureFormat.ARGB32);
            var photo=new Texture2D(w,h,TextureFormat.RGB24,false);
            try
            {
                Render(photo,large);
                File.WriteAllBytes(Path.Combine(trackOutput,file+".png"),photo.EncodeToPNG());
            }
            finally {camera.targetTexture=null;RenderTexture.active=prior;RenderTexture.ReleaseTemporary(large);Destroy(photo);}
        }
        try
        {
            var candidates=Enumerable.Range(1,mudPrints.Count-2).OrderBy(i=>(mudPrints[i]-original).sqrMagnitude);
            int chosen=-1,best=0;
            Vector3 eye=default,look=default;
            foreach(int i in candidates)
            {
                var a=mudPrints[i-1];var b=mudPrints[i];var c=mudPrints[i+1];
                var ab=b-a;var bc=c-b;
                if(Mathf.Min(ab.magnitude,bc.magnitude)<.22f || Mathf.Max(ab.magnitude,bc.magnitude)>1.4f ||
                    Vector3.Dot(ab.normalized,bc.normalized)<.25f) continue;
                Vector3 centre=(a+b+c)/3f;
                var axis=Vector3.ProjectOnPlane(c-a,Vector3.up).normalized;
                var side=Vector3.Cross(Vector3.up,axis);
                if(Vector3.Dot(side,new Vector3(.36f,0,-.6f))>0)side=-side;
                foreach(var shift in new[]{new Vector3(.60f,1.70f,-.28f),new(.20f,1.75f,.43f),new(.96f,1.45f,.20f)})
                {
                    var position=centre+side*shift.x+axis*shift.z+Vector3.up*shift.y;
                    host.transform.SetPositionAndRotation(position,Quaternion.LookRotation(centre-position));
                    camera.fieldOfView=54;camera.targetTexture=probe;camera.aspect=(float)width/height;
                    var screens=new[]{camera.WorldToViewportPoint(a),camera.WorldToViewportPoint(b),camera.WorldToViewportPoint(c)};
                    if(screens.Any(p=>p.z<=0 || p.x<.08f || p.x>.92f || p.y<.10f || p.y>.90f))continue;
                    tracks.ShowImpressions(false);Render(frame,probe);var bare=frame.GetPixels32();
                    tracks.ShowImpressions(true);Render(frame,probe);var planted=frame.GetPixels32();
                    int weakest=int.MaxValue;
                    foreach(var p in screens)
                    {
                        int changed=0,cx=Mathf.RoundToInt(p.x*width),cy=Mathf.RoundToInt(p.y*height);
                        for(int y=cy-12;y<=cy+12;y++)
                        for(int x=cx-12;x<=cx+12;x++)
                        {
                            int n=y*width+x;var before=bare[n];var after=planted[n];
                            if(Mathf.Abs(before.r-after.r)+Mathf.Abs(before.g-after.g)+Mathf.Abs(before.b-after.b)>18)changed++;
                        }
                        weakest=Mathf.Min(weakest,changed);
                    }
                    // The colour pair, measured at each separate sole, rejects ornamental
                    // posts, stones and duckboards even when those props have no collider.
                    if(weakest>best){best=weakest;chosen=i;eye=position;look=centre;}
                    if(best>=65)break;
                }
                if(best>=65)break;
            }
            if(chosen<0 || best<15) throw new InvalidOperationException("No view resolved all three actual mud steps: "+best);
            host.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(look-eye));camera.fieldOfView=54;
            tracks.ShowImpressions(true);Save("mud-trail",1280,800);
            tracks.ShowImpressions(false);Save("mud-trail-ground",1280,800);
            var fixedEye=original+new Vector3(.36f,.42f,-.60f);
            host.transform.SetPositionAndRotation(fixedEye,Quaternion.LookRotation(original-fixedEye));camera.fieldOfView=55;
            Save("mud-bed-at-duckboards",640,430);
            Debug.Log($"[Osowiec tracks] three successive plants {mudPrints[chosen-1]}, {mudPrints[chosen]}, {mudPrints[chosen+1]}, weakest visible patch={best} pixels at {width}x{height}.");
        }
        finally
        {
            tracks.ShowImpressions(visible);camera.targetTexture=null;RenderTexture.active=prior;
            RenderTexture.ReleaseTemporary(probe);Destroy(frame);Destroy(host);
        }
    }

    void CapturePrint(OsowiecWetTracks tracks,Vector3 target,string substrate)
    {
        var host=new GameObject("Inspect actual player footsteps");
        host.transform.position=target+new Vector3(.35f,1.52f,-.64f);
        host.transform.rotation=Quaternion.LookRotation(target-host.transform.position);
        var camera=host.AddComponent<Camera>();
        camera.enabled=false;camera.cullingMask=~(1<<5);camera.fieldOfView=45;camera.nearClipPlane=.04f;
        var rt=RenderTexture.GetTemporary(640,430,24,RenderTextureFormat.ARGB32);
        var prior=RenderTexture.active;
        var frame=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        var visible=tracks.ImpressionsVisible;
        try
        {
            camera.targetTexture=rt;
            Color32[] bare=null;
            foreach(bool printed in new[]{false,true})
            {
                tracks.ShowImpressions(printed);
                camera.Render();RenderTexture.active=rt;
                frame.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);frame.Apply();
                File.WriteAllBytes(Path.Combine(trackOutput,substrate+(printed?"-steps.png":"-ground.png")),frame.EncodeToPNG());
                var pixels=frame.GetPixels32();
                if(!printed){bare=pixels;continue;}
                int changed=0;
                // Central 120 x 120 pixels enclose the selected planted boot.
                for(int y=rt.height/2-60;y<rt.height/2+60;y++)
                for(int x=rt.width/2-60;x<rt.width/2+60;x++)
                {
                    int i=y*rt.width+x;
                    int difference=Mathf.Abs(pixels[i].r-bare[i].r)+Mathf.Abs(pixels[i].g-bare[i].g)+Mathf.Abs(pixels[i].b-bare[i].b);
                    if(difference>24)changed++;
                }
                if(changed<100)throw new InvalidOperationException($"{substrate} actual print invisible: {changed} pixels at {target}");
                Debug.Log($"[Osowiec tracks] visible {substrate} target={target} changed_pixels={changed} camera_height=1.52m");
            }
            if(substrate=="mud")
            {
                host.transform.position=target+new Vector3(.36f,.42f,-.60f);
                host.transform.rotation=Quaternion.LookRotation(target-host.transform.position);
                camera.fieldOfView=55;
                camera.Render();RenderTexture.active=rt;
                frame.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);frame.Apply();
                File.WriteAllBytes(Path.Combine(trackOutput,"mud-low-angle.png"),frame.EncodeToPNG());
            }
        }
        finally
        {
            tracks.ShowImpressions(visible);camera.targetTexture=null;
            RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);Destroy(frame);Destroy(host);
        }
    }
}
