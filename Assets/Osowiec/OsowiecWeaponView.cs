using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class OsowiecWeaponView : MonoBehaviour
{
    public GameObject model;
    public GameObject hands;
    public AnimationClip reloadClip;
    static OsowiecWeaponView current;
    Transform pivot, tip, support, cassette;
    PlayableGraph graph;
    AnimationClipPlayable reload;
    public static float HandTravel => current && current.support ? current.support.localPosition.magnitude : 0;
    public static float FeedTravel => current && current.cassette ? current.cassette.localPosition.magnitude : 0;
    Unit previousUnit;
    Weapon previousWeapon, mountedWeapon;
    GameObject currentModel, rifleBody;
    readonly GameObject[] kitModels=new GameObject[5];
    Transform kitGrip;
    OsowiecKitItem shown;
    float changeLift, reseatTime, reseatStart;
    OsowiecKitItem stowedItem;
    readonly Transform[] rings=new Transform[5];
    readonly Vector3[] ringRest=new Vector3[5];
    Renderer supportGlove;
    public static void StowEquipment(Unit actor,OsowiecKitItem item,float strength)
    {
        if(!current || current.previousUnit!=actor) return;
        current.stowedItem=item;
        current.reseatTime=.32f;
        current.reseatStart=Mathf.Max(.36f,strength);
    }
    public static Vector3? ThrowOrigin(Unit unit) =>
        current && current.previousUnit==unit && current.kitGrip ? current.kitGrip.position : (Vector3?)null;
    internal static GameObject VisibleProp(Unit unit,OsowiecKitItem item) =>
        current && current.previousUnit==unit ? current.kitModels[(int)item] : null;
    int previousAmmo,previousHealth;
    bool previousMask;
    Vector3 previousFoot;
    float kick,speed,stepPhase,breathTime,equipTime,hurtTime,aimBlend;
    Vector2 sway;
    bool posed;

    public static void UsedEquipment(Unit actor)
    {
        if(current && current.previousUnit==actor)current.equipTime=.56f;
    }

    void OnEnable() => current = this;
    void OnDisable()
    {
        if (current == this) current = null;
        if (pivot) pivot.gameObject.SetActive(false);
    }

    public static Transform VisibleMuzzle(Unit unit) =>
        current && current.previousUnit == unit && current.tip && current.tip.gameObject.activeInHierarchy
        ? current.tip : null;

    void LateUpdate()
    {
        var cameras = CameraManager.Instance;
        var actor = cameras != null ? cameras.GetCurrentControlledUnit() : null;
        var weapon = actor != null ? actor.GetEquippedWeapon() : null;
        if (!model || !weapon || actor.owner != Player.Player1 || !cameras.IsActionMode() || OsowiecFieldKit.ScopeOpen)
        {
            if (pivot) pivot.gameObject.SetActive(false);
            previousUnit = null;
            return;
        }
        if (!pivot || mountedWeapon != weapon)
        {
            var chosen = weapon.Config && weapon.Config.firstPersonPrefab ? weapon.Config.firstPersonPrefab : model;
            if (!pivot || currentModel != chosen || mountedWeapon != weapon)
            {
                ClearView();
                CreateView(cameras.GetActionCamera().transform, chosen, actor);
                currentModel = chosen;
            }
            mountedWeapon = weapon;
        }
        if (!pivot || !tip) return;
        pivot.gameObject.SetActive(true);
        var held=OsowiecFieldKit.Held(actor);
        if(actor!=previousUnit || held!=OsowiecKitItem.Rifle || Mouse.current?.leftButton.wasPressedThisFrame==true)
            reseatTime=0;
        if(reseatTime>0)
        {
            held=stowedItem;
            reseatTime=Mathf.Max(0,reseatTime-Time.deltaTime);
        }
        ApplyItem(held);
        if(GameManager.Instance!=null && GameManager.Instance.IsPaused())return;
        float dt = Time.deltaTime;
        if(dt<=0)return;
        bool change=actor!=previousUnit || weapon!=previousWeapon;
        bool mask=OsowiecOperation.Instance!=null && OsowiecOperation.Instance.MaskOn(actor);
        float motion=0;
        if(change)
        {
            speed=kick=hurtTime=aimBlend=0;
            equipTime=.32f;stepPhase=breathTime=0;sway=Vector2.zero;
        }
        else
        {
            Vector3 travelled=actor.transform.position-previousFoot;
            travelled.y=0;
            // Follow displacement after collision resolution. Camera switches and teleports cannot create a running pose.
            if(travelled.sqrMagnitude<1.3f*1.3f)motion=Mathf.Min(7f,travelled.magnitude/dt);
            if(weapon.AmmoInMag<previousAmmo)kick=OsowiecFieldSettings.ReducedShake ? .018f : .069f;
            if(actor.GetHealth()<previousHealth)hurtTime=.45f;
            if(mask!=previousMask)equipTime=.56f;
        }
        previousFoot=actor.transform.position;
        previousUnit = actor;
        previousWeapon=weapon;
        previousMask=mask;
        previousHealth=actor.GetHealth();
        previousAmmo = weapon.AmmoInMag;
        kick = Mathf.MoveTowards(kick,0,dt*.66f);
        speed=Mathf.Lerp(speed,motion,1-Mathf.Exp(-dt*10));
        stepPhase=(stepPhase+dt*speed*4.9f)%(2*Mathf.PI);
        breathTime=(breathTime+dt) % (2*Mathf.PI/.95f);
        hurtTime=Mathf.Max(0,hurtTime-dt); equipTime=Mathf.Max(0,equipTime-dt);
        var mouse = Mouse.current;
        bool aim = held==OsowiecKitItem.Rifle && !OsowiecFieldKit.WheelOpen && mouse != null && mouse.rightButton.isPressed && !weapon.IsReloading && !mask;
        aimBlend=Mathf.MoveTowards(aimBlend,aim?1:0,dt*6.5f);
        var delta = mouse != null && !OsowiecFieldKit.WheelOpen ? mouse.delta.ReadValue() : Vector2.zero;
        sway = Vector2.Lerp(sway,Vector2.ClampMagnitude(delta,20)*.0012f,1-Mathf.Exp(-dt*12));
        var lens=cameras.GetActionCamera();
        float fov=OsowiecFieldSettings.FieldOfView;
        if (!Mathf.Approximately(lens.fieldOfView,fov)) lens.fieldOfView=fov;
        float frame = Mathf.Clamp(Mathf.Tan(fov*Mathf.Deg2Rad*.5f)/.65f,.7f,1.35f);
        Vector3 grip=Vector3.Lerp(new Vector3(.205f,-.184f,.485f),new Vector3(.035f,-.16f,.46f),aimBlend);
        float motionScale=(OsowiecFieldSettings.ReducedShake ? .3f : 1f)*(1f-.83f*aimBlend)*(weapon.IsReloading ? .32f : 1f);
        float stride=Mathf.InverseLerp(.06f,2.20f,speed)*motionScale;
        float run=Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.7f,4.35f,speed))*stride;
        float shift=Mathf.Sin(stepPhase)*stride;
        float bob=(1-Mathf.Cos(2*stepPhase))*stride*.5f;
        float breath=Mathf.Sin(breathTime*.95f)*.0024f*motionScale*(1-Mathf.Clamp01(speed*.5f));
        float flinch=Mathf.Sin(Mathf.PI*Mathf.Clamp01(hurtTime/.45f))*motionScale;
        float handling=Mathf.Sin(Mathf.PI*Mathf.Clamp01(equipTime/.56f))*(OsowiecFieldSettings.ReducedShake ? .64f : 1f);
        pivot.localPosition=new Vector3(grip.x*frame-sway.x+.009f*shift-.032f*run,
            grip.y*frame-sway.y+breath+.011f*bob-.037f*run-.079f*handling-.028f*flinch,
            grip.z-kick-.022f*run-.033f*handling);
        pivot.localRotation=Quaternion.Euler(4-kick*45+7*run+9*handling-6*flinch,
            -8f*(1-aimBlend)-8*run+2*handling,1.3f*shift+9*run+6*handling+8*flinch);
        float pressure=OsowiecOperation.Instance ? OsowiecOperation.Instance.Pressure(actor) : 0;
        float tension=pressure*(OsowiecFieldSettings.ReducedShake || OsowiecFieldSettings.ReducedEffects ? .28f : 1f);
        pivot.localPosition+=new Vector3(-.012f,-.043f,-.024f)*tension;
        pivot.localRotation*=Quaternion.Euler(4f*tension,0,(3f+Mathf.Sin(breathTime*10f)*.8f)*tension);
        changeLift=Mathf.MoveTowards(changeLift,0,Time.unscaledDeltaTime*5.4f);
        if(OsowiecFieldKit.Throwable(held))
        {
            pivot.localPosition+=new Vector3(.001f,.078f,-.025f);
            float windup = reseatTime>0 ? reseatStart*Mathf.SmoothStep(0,1,reseatTime/.32f) : OsowiecFieldKit.ThrowCharge(actor);
            var ring=rings[(int)held];
            if(ring)
            {
                float lift=Mathf.SmoothStep(0,1,Mathf.Min(1,windup*3));
                ring.localPosition=ringRest[(int)held]+new Vector3(-.088f,.023f,-.016f)*lift;
                ring.localRotation=Quaternion.Euler(0,0,24*lift);
                if(support && supportGlove)
                {
                    bool gripping=lift>.001f || reseatTime>0;
                    support.gameObject.SetActive(gripping);
                    if(gripping)
                    {
                        support.localPosition=Vector3.zero;
                        support.position+=ring.position-supportGlove.bounds.center+kitGrip.TransformVector(new Vector3(-.028f,-.022f,0));
                    }
                }
            }
            pivot.localPosition += new Vector3(.05f,.065f,-.11f) * windup;
            pivot.localRotation*=Quaternion.Euler(-9-27*windup,-25+14*windup,-19-8*windup);
        }
        if(held==OsowiecKitItem.Bayonet)
        {
            pivot.localPosition+=new Vector3(.080f,-.047f,-.105f);
            float thrust=Mathf.Sin(Mathf.PI*Mathf.Clamp01(equipTime/.56f));
            pivot.localPosition+=new Vector3(0,.02f,thrust*.13f);
            pivot.localRotation*=Quaternion.Euler(-17,-28,-14);
        }
        pivot.localPosition+=Vector3.down*(changeLift*changeLift*.105f);
        if (graph.IsValid() && (weapon.IsReloading || posed))
        {
            reload.SetTime(weapon.IsReloading ? weapon.ReloadPhase * reloadClip.length : 0);
            graph.Evaluate(0);
        }
        posed = weapon.IsReloading;
    }

    void CreateView(Transform camera, GameObject modelPrefab, Unit actor)
    {
        pivot = new GameObject("First-person rifle presentation").transform;
        pivot.SetParent(camera,false);
        var carriage = new GameObject("Presentation").transform;
        carriage.SetParent(pivot,false);
        var visual = rifleBody = Instantiate(modelPrefab,carriage);
        if (hands) Instantiate(hands,visual.transform,false);
        var nodes = visual.GetComponentsInChildren<Transform>();
        tip = nodes.FirstOrDefault(node => node.name == "FieldRifleMuzzle");
        var grip = nodes.FirstOrDefault(node => node.name == "FieldRifleGrip");
        if (!tip || !grip) { Destroy(pivot.gameObject); enabled=false; return; }
        var axis = Vector3.ProjectOnPlane(tip.position-grip.position,pivot.up);
        visual.transform.rotation = Quaternion.FromToRotation(axis,pivot.forward)*visual.transform.rotation;
        visual.transform.position += pivot.position-grip.position;
        void Mount(string name, string part)
        {
            var mesh=nodes.FirstOrDefault(node=>node.name==part);
            if (!mesh) return;
            var mount=new GameObject(name).transform;
            mount.SetParent(carriage,false);
            mesh.SetParent(mount,true);
        }
        Mount("Support","FieldGloveL");
        Mount("Brace","FieldGloveR");
        Mount("Cassette","FieldFeed");
        support=carriage.Find("Support");
        supportGlove=support ? support.GetComponentInChildren<Renderer>() : null;
        cassette=carriage.Find("Cassette");
        kitGrip=new GameObject("Equipment grip").transform;
        kitGrip.SetParent(carriage,false);
        kitGrip.localPosition=new Vector3(-.009f,.017f,.048f);
        foreach(var item in new[]{OsowiecKitItem.Grenade,OsowiecKitItem.Smoke,OsowiecKitItem.Gas,OsowiecKitItem.Bayonet})
        {
            var prop=OsowiecKitProps.Create(item,item==OsowiecKitItem.Bayonet?carriage:kitGrip,actor);
            if(!prop) continue;
            if(item==OsowiecKitItem.Bayonet)
            {
                prop.transform.localPosition=carriage.InverseTransformPoint(tip.position)+new Vector3(.024f,-.025f,-.040f);
                prop.transform.localRotation=Quaternion.Euler(0,0,67);
                prop.transform.localScale=Vector3.one*1.40f;
            }
            kitModels[(int)item]=prop;
            var ring=prop.transform.Find("Ring");
            rings[(int)item]=ring;
            if(ring) ringRest[(int)item]=ring.localPosition;
            prop.SetActive(false);
        }
        shown=OsowiecKitItem.Rifle;
        if (!reloadClip) return;
        var animator=pivot.gameObject.AddComponent<Animator>();
        animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        graph=PlayableGraph.Create("Osowiec first-person reload");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        reload=AnimationClipPlayable.Create(graph,reloadClip);
        reload.SetSpeed(0);
        var output=AnimationPlayableOutput.Create(graph,"Held carbine",animator);
        output.SetSourcePlayable(reload);
        graph.Play();
    }

    void ApplyItem(OsowiecKitItem item)
    {
        if(item==shown) return;
        if(kitModels[(int)shown]) kitModels[(int)shown].SetActive(false);
        bool rifle=!OsowiecFieldKit.Throwable(item);
        rifleBody.SetActive(rifle);
        if(cassette) cassette.gameObject.SetActive(rifle);
        if(support) { support.localPosition=Vector3.zero; support.gameObject.SetActive(rifle); }
        if(kitModels[(int)item]) kitModels[(int)item].SetActive(true);
        shown=item;
        changeLift=1;
    }

    void ClearView()
    {
        if(graph.IsValid()) graph.Destroy();
        if (pivot)
        {
            pivot.gameObject.SetActive(false);
            Destroy(pivot.gameObject);
        }
        pivot = tip = support = cassette = kitGrip = null;
        System.Array.Clear(kitModels,0,kitModels.Length);
        reseatTime=0; supportGlove=null;
        System.Array.Clear(rings,0,rings.Length);
        posed = false;
    }

    void OnDestroy() => ClearView();
}
