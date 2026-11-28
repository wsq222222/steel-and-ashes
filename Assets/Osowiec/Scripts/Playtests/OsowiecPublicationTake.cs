using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

// Opt-in production footage from the standalone client. All input belongs to a virtual
// keyboard inside this process. Neither the desktop nor its audio is sampled.
public sealed class OsowiecPublicationTake : MonoBehaviour
{
    const int Fps=24;
    public string folder;
    int frames,channels;
    long audioFrames;
    float audioPeak;
    bool recording, offline;
    Camera shot,gameCamera;
    RenderTexture target;
    Texture2D pixels;
    Stream pcm;
    Keyboard keys;
    Unit actor;
    NativeArray<float> samples;
    float[] floats;
    byte[] bytes;
    readonly WaitForEndOfFrame endFrame=new();
    readonly RaycastHit[] sight=new RaycastHit[24];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(Application.isEditor || FindAnyObjectByType<OsowiecPublicationTake>())return;
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-osowiec-publication-take");
        if(i<0 || i+1>=args.Length)return;
        var host=new GameObject("Publication camera");
        DontDestroyOnLoad(host);host.AddComponent<OsowiecPublicationTake>().folder=Path.GetFullPath(args[i+1]);
    }

    void Mark(string label)
    {
        File.AppendAllText(Path.Combine(folder,"shots.tsv"),FormattableString.Invariant($"{frames}\t{(offline?frames/(float)Fps:Time.time):F3}\t{label}\n"));
        Debug.Log("[Publication] "+label);
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(folder);
        if(Directory.EnumerateFileSystemEntries(folder).Any())throw new IOException("Take directory must be empty");
        Application.runInBackground=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=Fps;
        yield return OsowiecMapCheck.ApplyCaptureSize();
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<12;i++)yield return null;
        var deployment=FindAnyObjectByType<ArmyDeploymentController>();
        if(!deployment.TryPlaceOfferAtGridCell(2,OsowiecDeploymentSectors.Cells[0][0]))throw new InvalidOperationException("No publication observer.");
        actor=FindObjectsByType<Unit>().Single(u=>u.owner==Player.Player1);
        FindAnyObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<5;i++)yield return null;
        Vector3 approach=new(-13.05f,0,-4.5f);
        Place(new Vector3(-14.1f,0,-8.85f),approach);
        var planner=OsowiecPlanner.Instance;planner.Select(actor);planner.ConfirmOrder();
        for(int i=0;i<7;i++)yield return null;
        if(!actor.IsControlled())throw new InvalidOperationException("FPS observer not controlled.");
        keys=InputSystem.AddDevice<Keyboard>("Publication keyboard");InputSystem.SetDeviceUsage(keys,"Publication");keys.MakeCurrent();
        Time.timeScale=1;
        offline=!Environment.GetCommandLineArgs().Contains("-osowiec-external-record");
        if(offline)
        {
            Time.captureFramerate=Fps;
            channels=AudioSettings.speakerMode switch {AudioSpeakerMode.Mono=>1,AudioSpeakerMode.Quad=>4,AudioSpeakerMode.Surround=>5,AudioSpeakerMode.Mode5point1=>6,AudioSpeakerMode.Mode7point1=>8,_=>2};
            if(!AudioRenderer.Start())throw new InvalidOperationException("Native audio capture unavailable.");
            pcm=File.Create(Path.Combine(folder,"game.f32le"));
            pixels=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            Directory.CreateDirectory(Path.Combine(folder,"images"));
        }
        gameCamera=CameraManager.Instance.GetActionCamera();
        recording=true;StartCoroutine(Capture());
        Mark("FPS start");yield return new WaitForSeconds(2);
        using(var movement=new InputAction(type:InputActionType.Value))
        {
            movement.AddCompositeBinding("2DVector").With("Up","<Keyboard>{Publication}/w");
            movement.performed+=actor.OnMove;movement.canceled+=actor.OnMove;movement.Enable();
            var origin=actor.transform.position;
            InputSystem.QueueStateEvent(keys,new KeyboardState(Key.W));
            float stopAt=Time.time+4.25f;
            while(Time.time<stopAt && Vector3.ProjectOnPlane(actor.transform.position-approach,Vector3.up).sqrMagnitude>.22f)
                yield return null;
            InputSystem.QueueStateEvent(keys,new KeyboardState());yield return null;yield return null;
            Mark("FPS walk "+Vector3.Distance(origin,actor.transform.position).ToString("F2")+" m");
        }
        if(Vector3.Distance(actor.transform.position,OsowiecOperation.Telephone)>3.10f)
            Place(approach,new Vector3(-15,1.22f,-4.8f));
        var phone=new Vector3(-15,1.23f,-4.8f);
        var initialYaw=actor.transform.rotation;
        var desired=Quaternion.LookRotation(Vector3.ProjectOnPlane(phone-actor.transform.position,Vector3.up));
        float turn=0;
        while(turn<1){turn+=Time.deltaTime*.62f; actor.transform.rotation=Quaternion.Slerp(initialYaw,desired,Mathf.SmoothStep(0,1,turn));actor.ClampActionLookPitchAbsolute(7,7);yield return null;}
        Mark("handset");yield return new WaitForSeconds(4);
        yield return Press(Key.E);Mark("switchboard");yield return new WaitForSeconds(6);
        yield return Press(Key.Digit2);Mark("HQ response");yield return new WaitForSeconds(5);
        if(OsowiecOperation.Instance.NextObjective==OsowiecOperation.Telephone)throw new InvalidOperationException("HQ did not answer.");
        EnableShot();
        Place(new Vector3(-14.1f,0,-8.85f),phone);
        Mark("handset and terminals");
        yield return Pan(new Vector3(-13.86f,1.50f,-4.50f),new Vector3(-13.88f,1.36f,-4.62f),phone,7,47);
        Mark("line hookup");
        yield return Pan(new Vector3(-13.45f,2.2f,-6.6f),new Vector3(-13.5f,2.8f,-7.6f),new Vector3(-15.9f,2.5f,-5.45f),7,51);

        var column=FindAnyObjectByType<OsowiecReliefColumn>();
        // Stage the documented starting point of a relief journey, then use the real
        // dispatch, normal road traversal, wheel repair and admission state machines.
        column.Begin();
        Place(column.SupplyPost+Vector3.right*.5f,column.CartPosition);
        column.Interact(actor);
        // The station can accept a dispatch before the barrier carving update finishes.
        // Wait for the live escort target, then stock the cart in its moving phase.
        float dispatchDeadline=Time.realtimeSinceStartup+4f;
        bool departed=column.TryGetEscortTarget(out _);
        while(!departed && Time.realtimeSinceStartup<dispatchDeadline)
        {
            yield return null;
            departed=column.TryGetEscortTarget(out _);
        }
        if(departed){column.Interact(actor);yield return null;}
        Mark(departed?"cart departure":"cart still at loading bay");
        var ward=FindAnyObjectByType<OsowiecWesternWard>();
        float journey=Time.time,nextNote=journey;
        string last="";
        while(departed && column.Delivered==0 && Time.time-journey<165f)
        {
            string state=column.StageName;
            if(state!=last){last=state;Mark("cart "+state);}
            var cart=column.GetComponentInChildren<OsowiecCartMotion>();
            var p=column.CartPosition;
            Vector3 direction=cart?cart.transform.forward:Vector3.forward;
            direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            Vector3 side=Vector3.Cross(Vector3.up,direction);
            float angle=Mathf.Sin((Time.time-journey)*.18f);
            var aim=p+Vector3.up*.82f;
            var eye=aim-direction*(2.4f+angle*.35f)+side*(2.25f+angle*.35f)+Vector3.up*(1.28f+.43f*angle);
            Lens(ClearEye(aim,eye,cart?cart.transform:null),aim,55);
            if(column.Intercepted)
            {
                Place(p+side*1.9f,p);column.Interact(actor);
            }
            if(state=="Help" || state=="Clearance")
            {
                yield return new WaitForSeconds(3);
                if(!column.Stocked && state=="Help"){Place(column.SupplyPost+Vector3.right*.55f,p);column.Interact(actor);}
                Place(state=="Clearance"?OsowiecOperation.Telephone+Vector3.right:column.CartPosition+side*1.8f,p);
                column.Interact(actor);
                Mark(state=="Help"?"wheel assistance":"admission confirmed");
            }
            if(Time.time>nextNote){Mark("cart coordinate "+p.ToString("F2"));nextNote=Time.time+10;}
            yield return null;
        }
        Mark("cart delivered "+column.Delivered);
        // Start a single intake with the ward's public delivery-event interface when
        // no wagon arrived. The receipt explicitly distinguishes a staged incoming
        // casualty from a completed journey. Animation, physics and sound run normally.
        bool seeded=column.Delivered==0;
        var landing=new Vector3(-48.94f,.58f,-15.66f);
        Lens(landing+new Vector3(-2.25f,1.05f,-2.22f),landing,60);
        if(seeded)ward.Tick(0f,ward.Admitted+1,true,OsowiecOperation.Instance.FieldRoster);
        Mark(seeded?"ward staged arrival at landing":"ward receives delivered passenger");
        float intakeEnd=Time.time+55, intakeNote=Time.time;
        while(ward.Admitted==0 && Time.time<intakeEnd)
        {
            var p=ward.stretcher.gameObject.activeInHierarchy?ward.stretcher.position:landing;
            var focus=p+Vector3.up*.25f;
            float travel=Mathf.Clamp01((p.z+15.66f)/5.9f);
            var eye=focus+Vector3.Lerp(new Vector3(-2.28f,.98f,-2.32f),new Vector3(-2.16f,1.25f,-2.05f),travel);
            Lens(eye,focus,64);
            if(Time.time>=intakeNote){Mark("litter "+p.ToString("F2"));intakeNote=Time.time+2;}
            yield return null;
        }
        Mark("ward occupied beds "+ward.Admitted);
        if(ward.Admitted==0 && !departed)throw new InvalidOperationException("Neither a real departure nor a ward transfer was recorded.");
        var berth=new Vector3(-48.68f,.48f,-8.99f);
        yield return Pan(berth+new Vector3(-1.61f,1.02f,-2.23f),berth+new Vector3(-1.54f,.87f,-2.02f),berth,9,57);
        Mark("bedside care");
        int remaining=Mathf.Max(16,187-Mathf.FloorToInt(frames/(float)Fps));
        float finish=Time.time+remaining;
        while(Time.time<finish)
        {
            var p=ward.people[0].position;
            float oscillate=Mathf.Sin(Time.time*.12f);
            Lens(p+new Vector3(1.05f+.23f*oscillate,1.30f,-2.55f),p+new Vector3(.29f,.83f,0),53);
            yield return null;
        }
        Mark("complete");recording=false;yield return endFrame;yield return null;
        if(offline && (audioPeak<.00005f || audioFrames<1000))throw new InvalidOperationException("Silent recorded game.");
        File.WriteAllText(Path.Combine(folder,"complete.txt"),FormattableString.Invariant($"Build {Application.buildGUID}\nFPS {Fps}\nFrames {frames}\nSampleRate {AudioSettings.outputSampleRate}\nChannels {channels}\nPeak {audioPeak:F6}\nPCM frames {audioFrames}\nCart delivered {column.Delivered}\nWard staged arrival {seeded}\nWard admitted {ward.Admitted}\n"));
        Application.Quit();
    }

    void Place(Vector3 near,Vector3 look)
    {
        if(!NavMesh.SamplePosition(near,out var ground,2,NavMesh.AllAreas))throw new InvalidOperationException("No ground "+near);
        var body=actor.GetComponent<CharacterController>();if(body)body.enabled=false;
        Vector3 heading=Vector3.ProjectOnPlane(look-ground.position,Vector3.up);
        actor.transform.SetPositionAndRotation(ground.position+Vector3.up*.045f,heading.sqrMagnitude>.001f?Quaternion.LookRotation(heading):actor.transform.rotation);
        if(body)body.enabled=true;actor.RefreshGridPositionFromWorld();Physics.SyncTransforms();
    }

    IEnumerator Press(Key key)
    {
        keys.MakeCurrent();InputSystem.QueueStateEvent(keys,new KeyboardState(key));
        yield return null;yield return null;InputSystem.QueueStateEvent(keys,new KeyboardState());yield return null;yield return null;
    }

    Vector3 ClearEye(Vector3 aim,Vector3 eye,Transform cart)
    {
        var dir=eye-aim;float length=dir.magnitude;
        int n=Physics.RaycastNonAlloc(aim,dir/length,sight,length,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++)
            if(sight[i].distance>.65f && !sight[i].collider.GetComponentInParent<Animator>() &&
               (!cart||!sight[i].transform.IsChildOf(cart)))length=Mathf.Min(length,sight[i].distance-.09f);
        return aim+dir.normalized*Mathf.Max(.72f,length);
    }

    void EnableShot()
    {
        foreach(var camera in Camera.allCameras)if(camera.CompareTag("MainCamera"))camera.tag="Untagged";
        foreach(var listener in FindObjectsByType<AudioListener>())listener.enabled=false;
        var host=new GameObject("Publication tracking shot"){tag="MainCamera"};
        shot=host.AddComponent<Camera>();shot.CopyFrom(gameCamera);shot.nearClipPlane=.05f;shot.depth=120;
        shot.cullingMask=gameCamera.cullingMask&~(1<<5);
        var data=shot.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
        host.AddComponent<AudioListener>();gameCamera.enabled=false;
        if(offline){target=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32);target.Create();shot.targetTexture=target;}
    }

    void Lens(Vector3 eye,Vector3 aim,float fov){shot.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye));shot.fieldOfView=fov;}

    IEnumerator Pan(Vector3 from,Vector3 to,Vector3 aim,float duration,float fov)
    {
        float elapsed=0;
        while(elapsed<duration){Lens(Vector3.Lerp(from,to,Mathf.SmoothStep(0,1,elapsed/duration)),aim,fov);yield return null;elapsed+=Time.deltaTime;}
    }

    IEnumerator Capture()
    {
        while(recording)
        {
            yield return endFrame;
            if(!recording)break;
            if(offline)
            {
                var previous=RenderTexture.active;
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0,false);
                RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(folder,"images",frames.ToString("D5")+".jpg"),pixels.EncodeToJPG(89));
                int length=AudioRenderer.GetSampleCountForCaptureFrame()*channels;
                if(length>0)
                {
                    if(!samples.IsCreated || samples.Length!=length)
                    {
                        if(samples.IsCreated)samples.Dispose();
                        samples=new NativeArray<float>(length,Allocator.Persistent);
                        floats=new float[length];bytes=new byte[length*sizeof(float)];
                    }
                    if(!AudioRenderer.Render(samples))throw new InvalidOperationException("Game mixer render failed.");
                    samples.CopyTo(floats);
                    for(int i=0;i<length;i++)audioPeak=Mathf.Max(audioPeak,Mathf.Abs(floats[i]));
                    Buffer.BlockCopy(floats,0,bytes,0,bytes.Length);pcm.Write(bytes,0,bytes.Length);
                    audioFrames+=length/channels;
                }
            }
            frames++;
        }
        if(offline){pcm?.Dispose();pcm=null;AudioRenderer.Stop();}
    }

    void OnEnable(){Application.logMessageReceived+=OnLog;}
    void OnDisable(){Application.logMessageReceived-=OnLog;}
    void OnLog(string message,string trace,LogType kind)
    {
        if(kind!=LogType.Exception || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))return;
        File.WriteAllText(Path.Combine(folder,"failure.txt"),message+"\n"+trace);Application.Quit(5);
    }
    void OnDestroy()
    {
        pcm?.Dispose();if(samples.IsCreated)samples.Dispose();if(keys!=null && keys.added)InputSystem.RemoveDevice(keys);
        if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);
    }
}




