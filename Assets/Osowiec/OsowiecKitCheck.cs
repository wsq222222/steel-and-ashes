using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Opt-in player regression. Only the flagged player receives the synthetic input devices.
public sealed class OsowiecKitCheck : MonoBehaviour
{
    string output;
    Keyboard keyboard;
    Mouse mouse;
    Unit actor,target;
    Camera eye;
    int ammo;
    bool pictures;
    readonly System.Text.StringBuilder report=new();
    OsowiecFieldKit Kit => OsowiecFieldKit.Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args=Environment.GetCommandLineArgs();
        int i=Array.IndexOf(args,"-osowiec-kit-check");
        if(Application.isEditor || i<0 || i+1>=args.Length) return;
        var go=new GameObject("Field kit regression");
        DontDestroyOnLoad(go);
        go.AddComponent<OsowiecKitCheck>().output=args[i+1];
    }

    void OnEnable() => Application.logMessageReceived+=OnMessage;
    void OnDisable() => Application.logMessageReceived-=OnMessage;
    void OnMessage(string message,string trace,LogType kind)
    {
        if(kind!=LogType.Exception) return;
        if(!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output,"FAIL.txt"),report+"\n"+message+"\n"+trace);
        Application.Quit(2);
    }

    void Check(bool ok,string label)
    {
        if(!ok) throw new InvalidOperationException("Field kit check: "+label);
        report.AppendLine("PASS "+label);
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        Application.runInBackground=true;
        Time.maximumDeltaTime=.08f; // A PNG write must not skip the entire flight on a slow capture host.
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        mouse=InputSystem.AddDevice<Mouse>("Kit pointer"); mouse.MakeCurrent();
        keyboard=InputSystem.AddDevice<Keyboard>("Kit keyboard"); keyboard.MakeCurrent();
        pictures=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        report.AppendLine("Windows player "+Application.buildGUID);
        yield return Deploy(0);
        var initial=actor.transform.rotation;
        yield return Open(Key.F1);
        yield return Point(Vector2.left);
        var blade=OsowiecWeaponView.VisibleProp(actor,OsowiecKitItem.Bayonet);
        Check(OsowiecFieldKit.Held(actor)==OsowiecKitItem.Bayonet && blade && blade.activeInHierarchy,"Hammer previews the actual bayonet mesh");
        Check(Quaternion.Angle(initial,actor.transform.rotation)<.15f,"wheel pointer did not turn the actor");
        yield return Capture("01-bayonet-wheel");
        yield return ReleaseKey();
        Check(!OsowiecFieldKit.WheelOpen && Mathf.Approximately(Time.timeScale,1) && blade.activeInHierarchy,"release equips the bayonet and restores normal time");
        yield return Capture("02-bayonet");
        yield return Open(Key.G); yield return Point(Vector2.down);
        var smoke=OsowiecWeaponView.VisibleProp(actor,OsowiecKitItem.Gas);
        Check(smoke && smoke.activeInHierarchy && !OsowiecWeaponView.VisibleMuzzle(actor),"Hammer previews the green gas canister");
        yield return MouseButton(MouseButtonName.Right);
        Check(!OsowiecFieldKit.WheelOpen && OsowiecFieldKit.Held(actor)==OsowiecKitItem.Bayonet,"cancel restores previous equipment");
        yield return ReleaseKey();
        yield return Open(Key.G); yield return Point(Vector2.right);
        yield return Capture("03-grenade-wheel");
        yield return MouseButton(MouseButtonName.Left);
        yield return ReleaseKey();
        Check(OsowiecFieldKit.Held(actor)==OsowiecKitItem.Grenade && Kit.throwing.Impacts==0 && Kit.throwing.InFlight==null,"click inside the wheel confirms without throwing");
        Check(actor.GetEquippedWeapon().AmmoInMag==ammo,"cycling and confirming did not fire the rifle");
        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left).WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState());
        yield return null; yield return null;
        Check(OsowiecFieldKit.Held(actor)==OsowiecKitItem.Rifle && OsowiecFieldKit.Remaining(actor,OsowiecKitItem.Grenade)==1 && !OsowiecFieldKit.Busy(actor),"cancel stows the prop and preserves the charge and attack");
        Check(OsowiecFieldKit.Remaining(actor,OsowiecKitItem.Gas)==1 && OsowiecFieldKit.Remaining(actor,OsowiecKitItem.Smoke)==0,"Hammer carries a gas canister plus the heavy grenade");
        yield return KeyTap(Key.H);
        yield return Throw(OsowiecKitItem.Grenade,"04");
        int exposedHealth=target.GetHealth();
        Kit.Impact(OsowiecKitItem.Grenade,actor,target.transform.position+Vector3.up*.52f);
        yield return new WaitForSeconds(.34f);
        Check(target.GetHealth()<exposedHealth,"an exposed opponent receives impact damage");
        var gasProbe=target.transform.position+Vector3.right*12;
        Kit.Impact(OsowiecKitItem.Gas,actor,gasProbe);
        Check(OsowiecFieldKit.TryGasCloud(gasProbe,out var gasCenter) && OsowiecOperation.GasAt(gasProbe) &&
            OsowiecFieldKit.Obscured(gasCenter-Vector3.right*4,gasCenter+Vector3.right*4),
            "Hammer gas obscures sight and enters the filter simulation");
        Check(OsowiecFieldKit.Remaining(target,OsowiecKitItem.Grenade)==1 &&
            Kit.SpendThrowable(target,OsowiecKitItem.Grenade) && !Kit.SpendThrowable(target,OsowiecKitItem.Grenade),
            "both sides have independent single-use grenade stock");
        Vector3 chest=target.transform.position+Vector3.up*.77f;
        Vector3 side=target.transform.right;
        int near=OsowiecFieldKit.BlastDamage(chest+side*.65f,target);
        int far=OsowiecFieldKit.BlastDamage(chest+side*2.5f,target);
        Check(near>far && near<=16 && far>0,"damage falls off with distance: "+near+" / "+far);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetPositionAndRotation(chest+side*1.2f,Quaternion.LookRotation(side));
        wall.transform.localScale=new Vector3(2,2,.15f);
        Physics.SyncTransforms();
        Check(OsowiecFieldKit.BlastDamage(chest+side*2.5f,target)==0,"solid cover blocks the grenade");
        Destroy(wall);
        CameraManager.Instance.SwitchToTacticalMode();
        yield return null;
        Check(OsowiecFieldKit.Remaining(actor,OsowiecKitItem.Grenade)==0,"ending the action does not replenish a grenade");
        yield return Deploy(2);
        yield return Open(Key.G);
        yield return Point(new Vector2(-1,-.6f));
        yield return ReleaseKey();
        Check(OsowiecFieldKit.Held(actor)==OsowiecKitItem.Smoke,"three-sector scout wheel equips smoke");
        yield return Throw(OsowiecKitItem.Smoke,"05");
        var cloud=Kit.throwing.LastImpact+Vector3.up*.5f;
        Check(OsowiecFieldKit.Obscured(cloud-Vector3.right*4,cloud+Vector3.right*4),"the deployed smoke actually obscures sight");
        yield return Deploy(1);
        Vector3 low=eye.transform.position;
        yield return KeyTap(Key.Q);
        Check(OsowiecFieldKit.ScopeOpen && eye.fieldOfView<40 && eye.transform.position.y>low.y+.2f,"periscope raises the viewpoint and changes magnification");
        var sight=target.transform.position+Vector3.up*1.22f-eye.transform.position;
        actor.transform.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(sight,Vector3.up));
        float pitch=-Mathf.Asin(sight.normalized.y)*Mathf.Rad2Deg;
        actor.ClampActionLookPitchAbsolute(pitch,pitch);
        yield return null; yield return null;
        Check(Kit.ScopeTarget==target,"periscope previews the visible opponent");
        yield return Capture("06-periscope-target");
        yield return MouseButton(MouseButtonName.Left);
        Check(Kit.Marked==target && FogWarIntelTracker.Instance.HasIntel(target),"click marks the opponent for the tactical map");
        yield return KeyTap(Key.Q);
        Check(!OsowiecFieldKit.ScopeOpen && Mathf.Abs(eye.fieldOfView-OsowiecFieldSettings.FieldOfView)<.1f &&
            eye.transform.localPosition.sqrMagnitude<.001f,"folding the periscope restores the normal FPS camera");
        Check(!OsowiecFieldKit.Busy(actor) && OsowiecFieldKit.IsIdentified(target),"marked target and rifle action remain available");
        yield return Deploy(2);
        float beforeDash=actor.GetRemainingMoveMeters();
        yield return KeyTap(Key.Q);
        Check(Mathf.Abs(actor.GetRemainingMoveMeters()-beforeDash-4f)<.01f,"scout gains four playable metres");
        yield return KeyTap(Key.Q);
        Check(Mathf.Abs(actor.GetRemainingMoveMeters()-beforeDash-4f)<.01f,"scout cannot spend the same action twice");
        yield return Deploy(0);
        var foot=actor.transform.position;
        bool marchLeap=OsowiecMarch.Free(actor);
        float paidBefore=actor.FieldTurn.Breath(1000f)*1000f;
        float beforeLeap=actor.GetRemainingMoveMeters();
        yield return KeyTap(Key.Q);
        yield return new WaitForSeconds(.8f);
        float travelled=Vector3.ProjectOnPlane(actor.transform.position-foot,Vector3.up).magnitude;
        float cost=beforeLeap-actor.GetRemainingMoveMeters();
        float paid=actor.FieldTurn.Breath(1000f)*1000f-paidBefore;
        Check(travelled>.7f && paid<=travelled+.25f && Mathf.Abs(cost-paid)<.25f && (marchLeap || Mathf.Abs(cost-travelled)<.25f),
            "Hammer moves the capsule and spends only the combat part of the jump, travel "+travelled.ToString("F2")+" / cost "+cost.ToString("F2")+" / charged movement "+paid.ToString("F2")+" m");
        yield return Deploy(0);
        Move(target,actor.transform.position+actor.transform.forward*1.35f,-actor.transform.forward);
        yield return null; Physics.SyncTransforms();
        var part=target.GetComponentsInChildren<OsowiecHitPart>().First(p=>p.area==OsowiecHitArea.Torso);
        Vector3 aim=part.GetComponent<Collider>().bounds.center-eye.transform.position;
        actor.transform.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(aim,Vector3.up));
        float tilt=-Mathf.Asin(aim.normalized.y)*Mathf.Rad2Deg;
        actor.ClampActionLookPitchAbsolute(tilt,tilt);
        yield return KeyTap(Key.V);
        int originalHp=target.GetHealth();
        yield return MouseButton(MouseButtonName.Left);
        Check(target.GetHealth()<originalHp && actor.GetEquippedWeapon().AmmoInMag==ammo,"equipped bayonet contacts the visible opponent without firing a round");
        File.WriteAllText(Path.Combine(output,"KitCheck.txt"),report.ToString());
        Debug.Log(report.ToString());
        InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
        Application.Quit(0);
    }

    IEnumerator Deploy(int slot)
    {
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<8;i++) yield return null;
        var deployment=FindFirstObjectByType<ArmyDeploymentController>();
        Check(deployment.TryPlaceOfferAtGridCell(slot,OsowiecDeploymentSectors.Cells[0][0]),"deploy role "+slot);
        FindFirstObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<6;i++) yield return null;
        var units=FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID);
        actor=units.First(u=>u.owner==Player.Player1);
        target=units.First(u=>u.owner==Player.Player2 && u.GetEquippedWeapon() && u.chessType==ChessUnitType.Pawn);
        foreach(var u in units) if(u.owner==Player.Player2 && u!=target) u.gameObject.SetActive(false);
        // Reuse the visible, unobstructed trench fixture from the field HUD regression.
        Move(target,new Vector3(-45,.11f,-16),Vector3.right);
        Move(actor,new Vector3(-38,.11f,-16),Vector3.left);
        Physics.SyncTransforms();
        var planner=OsowiecPlanner.Instance;
        planner.Select(actor); planner.Preview(actor.transform.position+Vector3.left*2.4f); planner.ConfirmOrder();
        yield return null; yield return null;
        Check(CameraManager.Instance.IsActionMode() && CameraManager.Instance.GetCurrentControlledUnit()==actor,"normal FPS order "+slot);
        eye=CameraManager.Instance.GetActionCamera();
        ammo=actor.GetEquippedWeapon().AmmoInMag;
    }

    static void Move(Unit unit,Vector3 at,Vector3 direction)
    {
        var cc=unit.GetComponent<CharacterController>(); cc.enabled=false;
        unit.transform.SetPositionAndRotation(at,Quaternion.LookRotation(direction)); cc.enabled=true;
    }
    IEnumerator Open(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));
        yield return null; yield return null;
        Check(OsowiecFieldKit.WheelOpen && Time.timeScale>.01f && Time.timeScale<.3f,"wheel slows field time via "+key);
    }
    IEnumerator Point(Vector2 direction)
    {
        InputSystem.QueueDeltaStateEvent(mouse.delta,direction*120);
        yield return null; yield return new WaitForSecondsRealtime(.25f);
    }
    IEnumerator ReleaseKey()
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        yield return null; yield return null;
    }
    IEnumerator KeyTap(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));
        yield return null; yield return null;
        yield return ReleaseKey();
    }
    enum MouseButtonName { Left, Right }
    IEnumerator MouseButton(MouseButtonName button)
    {
        var b=button==MouseButtonName.Left?UnityEngine.InputSystem.LowLevel.MouseButton.Left:UnityEngine.InputSystem.LowLevel.MouseButton.Right;
        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(b));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState());
        yield return null;
    }
    IEnumerator Throw(OsowiecKitItem item,string prefix)
    {
        actor.ClampActionLookPitchAbsolute(10,10);
        yield return new WaitForSecondsRealtime(.35f);
        var minimum=Kit.throwing.Landing;
        int before=Kit.throwing.Impacts;
        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
        yield return null; yield return null;
        Check(!Kit.throwing.InFlight && OsowiecFieldKit.Remaining(actor,item)==1,"holding LMB keeps the prop in hand");
        yield return new WaitForSeconds(1f);
        var expected=Kit.throwing.Landing;
        Check(Vector3.Distance(minimum,expected)>.35f,"force changes the predicted landing");
        yield return Capture(prefix+"-aim");
        InputSystem.QueueStateEvent(mouse,new MouseState());
        yield return null; yield return null;
        var prop=Kit.throwing.InFlight;
        Check(prop && prop.activeInHierarchy,"released "+item+" exists in world space");
        var first=prop.transform.position;
        yield return new WaitForSeconds(.09f);
        yield return Capture(prefix+"-flight");
        Check(!prop || Vector3.Distance(prop.transform.position,first)>.025f,"visible prop travelled away from the hand");
        float deadline=Time.realtimeSinceStartup+18;
        while(Kit.throwing.Impacts==before && Time.realtimeSinceStartup<deadline) yield return null;
        Check(Kit.throwing.Impacts==before+1 && Vector3.Distance(Kit.throwing.LastImpact,expected)<.38f,
            "predicted endpoint matches "+item+" impact, error "+Vector3.Distance(Kit.throwing.LastImpact,expected).ToString("F3")+" m");
        var emitter=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
            .Any(p=>p.name=="Field smoke" && p.particleCount>0 && Vector3.Distance(p.transform.position,expected)<1.5f);
        Check(emitter,"impact emitted live "+item+" particles at the landing point");
        if(pictures && AudioSettings.outputSampleRate>0)
        {
            bool voice=FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Any(a=>a.name=="Surface impact" && a.isPlaying && Vector3.Distance(a.transform.position,expected)<.6f);
            Check(voice,"a positional effects voice is playing at the "+item+" impact");
        }
        yield return Capture(prefix+"-impact");
        Check(OsowiecFieldKit.Remaining(actor,item)==0 && actor.GetEquippedWeapon().AmmoInMag==ammo,"single charge spent, rifle ammunition preserved");
    }
    static bool HasPixels(Texture2D image)
    {
        if(!image) return false;
        float light=0,dark=1;
        for(int x=1;x<10;x++)
        for(int y=1;y<8;y++)
        {
            float value=image.GetPixel(image.width*x/10,image.height*y/8).grayscale;
            light=Mathf.Max(light,value); dark=Mathf.Min(dark,value);
        }
        return light-dark>=.08f;
    }

    IEnumerator Capture(string name)
    {
        if(!pictures) yield break;
        yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();
        if(!HasPixels(image))
        {
            if(image) Destroy(image);
            // Hidden Windows players can lack a swap chain. Render the active FPS camera offscreen.
            var active=RenderTexture.active;
            var previous=eye.targetTexture;
            var texture=RenderTexture.GetTemporary(960,540,24,RenderTextureFormat.ARGB32);
            try
            {
                eye.targetTexture=texture;
                eye.Render();
                RenderTexture.active=texture;
                image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);
                image.Apply();
                name+="-camera";
            }
            finally
            {
                eye.targetTexture=previous;
                RenderTexture.active=active;
                RenderTexture.ReleaseTemporary(texture);
            }
        }
        try
        {
            if(!HasPixels(image)) throw new InvalidOperationException("Empty graphical player frame: "+name);
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        }
        finally { Destroy(image); }
    }
}

