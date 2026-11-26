using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// Per-activation equipment, shared by the HUD, bot perception and FPS weapons.
[DefaultExecutionOrder(-100)]
public sealed class OsowiecFieldKit : MonoBehaviour
{
    public static OsowiecFieldKit Instance { get; private set; }
    public static bool Busy(Unit u) => Instance && Instance.actor == u &&
        (Instance.spent || Instance.chargingLeap || Instance.scope || Instance.wheel.Open || Instance.selected != OsowiecKitItem.Rifle || Instance.blockFireFrame==Time.frameCount);
    public static bool WheelOpen => Instance && Instance.wheel.Open;
    public static bool BlocksLook(Unit u) => Instance && Instance.actor==u && (WheelOpen || Instance.blockLookFrame==Time.frameCount);
    public static OsowiecKitItem Held(Unit u)
    {
        if(!Instance || Instance.actor!=u) return OsowiecKitItem.Rifle;
        var hovered=Instance.wheel.Hover;
        return WheelOpen && Remaining(u,hovered)!=0 ? hovered : Instance.selected;
    }
    internal static bool Throwable(OsowiecKitItem item) => item is OsowiecKitItem.Grenade or OsowiecKitItem.Smoke or OsowiecKitItem.Gas;
    public static int Remaining(Unit u,OsowiecKitItem item) =>
        !Throwable(item) ? -1 : !u || (item==OsowiecKitItem.Gas && u.chessType!=ChessUnitType.Pawn) ||
        (item==OsowiecKitItem.Smoke && u.chessType==ChessUnitType.Pawn) || Instance && Instance.used.Contains((u,item)) ? 0 : 1;
    internal readonly OsowiecKitThrow throwing=new();
    internal Unit ScopeTarget { get; private set; }
    internal Unit Marked => tagged && taggedUntil>round ? tagged : null;
    readonly OsowiecKitWheel wheel=new();
    readonly HashSet<(Unit,OsowiecKitItem)> used=new();
    OsowiecKitItem selected;
    int blockFireFrame=-1,blockLookFrame=-1;
    Vector3 scopeRest;
    float scopeLift;
    bool wheelOnF1, chargingThrow, chargingLeap, validLeap, zoomed, spentOnMarch;
    float chargeAt;
    readonly Vector3[] leapPath = new Vector3[17];
    readonly RaycastHit[] leapHits = new RaycastHit[24];
    Vector3 taggedPosition;
    readonly Dictionary<Unit,int> nextSkillTurn = new();
    bool Cooling => actor && nextSkillTurn.TryGetValue(actor,out int ready) && ready>round;
    float Charge => Mathf.Clamp01((Time.time-chargeAt)/.85f);
    public static float ThrowCharge(Unit u) => Instance && Instance.actor==u && Instance.chargingThrow ? Instance.Charge : 0f;
    public static bool IsIdentified(Unit u) => u && Instance && Instance.Marked==u && u.GetHealth()>0;
    public static void BeginCombat(Unit u)
    {
        if (!Instance || Instance.actor!=u || !Instance.spentOnMarch) return;
        Instance.spent=Instance.stopWalking=Instance.spentOnMarch=false;
    }
    void SpendAttack()
    {
        spentOnMarch=OsowiecMarch.Free(actor);
        spent=stopWalking=true;
    }
    void FinishSkill() => nextSkillTurn[actor]=round+1;
    void CancelCharge() { chargingLeap=false; chargingThrow=false; }
    bool CancelPressed => OsowiecKitWheel.CancelPressed;

    float feedbackUntil;
    public static string Feedback(Unit u) => Instance && Instance.actor==u && Time.time<Instance.feedbackUntil ? Instance.note : null;
    Coroutine jump;
    internal bool SpendThrowable(Unit unit,OsowiecKitItem item) => unit && Throwable(item) && Remaining(unit,item)>0 && used.Add((unit,item));
    public static bool HoldPosition(Unit u) => Instance != null && Instance.actor == u &&
        (Instance.stopWalking || Instance.scope || Instance.leaping || Instance.chargingLeap);
    public static string Status(Unit u) => Instance && Instance.actor==u ? Instance.note : "";
    public static bool ScopeOpen => Instance != null && Instance.scope;
    internal const float CloudRadius=3.5f;
    readonly List<Vector3> smoke = new();
    readonly List<bool> gasKind = new();
    readonly List<int> smokeExpiry = new();
    readonly List<ParticleSystem> smokeClouds = new();
    Material particleMaterial;
    Unit actor, tagged;
    bool spent, stopWalking, scope, leaping, previousBot;
    int round, taggedUntil;
    float originalFov;
    string note;
    Camera lens;
    public static bool Obscured(Vector3 a, Vector3 b)
    {
        if (Instance == null) return false;
        Vector3 line = b-a;
        float distance = line.magnitude;
        if (distance < 2) return false;
        if (OsowiecOperation.GasSightDepth(a,b) >= 2.25f)
            return true;
        for(int i=0;i<Instance.smoke.Count;i++)
        {
            if(Instance.gasKind[i]) continue;
            Vector3 c=Instance.smoke[i];
            float t = Mathf.Clamp01(Vector3.Dot(c-a,line)/line.sqrMagnitude);
            if ((a+line*t-c).sqrMagnitude < CloudRadius*CloudRadius) return true;
        }
        return false;
    }

    internal static float GasCloudSightDepth(Vector3 from,Vector3 to)
    {
        if(!Instance || Instance.smoke.Count==0) return 0;
        Vector3 delta=to-from;
        float length=delta.magnitude;
        if(length<.001f) return 0;
        Vector3 forward=delta/length;
        float deepest=0;
        for(int i=0;i<Instance.smoke.Count;i++)
        {
            if(!Instance.gasKind[i]) continue;
            Vector3 offset=Instance.smoke[i]-from;
            float axis=Vector3.Dot(offset,forward);
            float chord=CloudRadius*CloudRadius-(offset-forward*axis).sqrMagnitude;
            if(chord<=0) continue;
            float half=Mathf.Sqrt(chord);
            deepest=Mathf.Max(deepest,Mathf.Min(length,axis+half)-Mathf.Max(0,axis-half));
        }
        return deepest;
    }

    public static bool TryGasCloud(Vector3 point,out Vector3 center)
    {
        if(Instance)
            for(int i=0;i<Instance.smoke.Count;i++)
                if(Instance.gasKind[i] && (point-Instance.smoke[i]).sqrMagnitude< CloudRadius*CloudRadius)
                { center=Instance.smoke[i]; return true; }
        center=default;
        return false;
    }
    void Awake()
    {
        Instance=this;
        var shader=Shader.Find("Steel and Ashes/Osowiec Weather Particle");
        if (shader!=null) particleMaterial=new Material(shader);
    }
    void OnDestroy()
    {
        wheel.Dispose();
        CloseScope();
        if (Instance==this) Instance=null;
        if (particleMaterial!=null) Destroy(particleMaterial);
    }
    void CloseScope()
    {
        if (scope && lens!=null)
        {
            lens.fieldOfView=originalFov;
            if (lens.transform.parent && actor && lens.transform.IsChildOf(actor.transform)) lens.transform.localPosition=scopeRest;
        }
        scope=false; zoomed=false;
        ScopeTarget=null;
    }
    void OnDisable() { wheel.Close(); CancelCharge(); CloseScope(); }
    void OnApplicationFocus(bool focus) { if(!focus) { wheel.Close(); CancelCharge(); } }
    public static void Fired(Unit u)
    {
        if (Instance==null || Instance.actor!=u) return;
        Instance.SpendAttack();
        Instance.note="Выстрел произведён. Tab — передать ход.";
    }
    void Update()
    {
        var game=GameManager.Instance;
        var cameras=CameraManager.Instance;
        if (game==null || cameras==null || game.IsPaused()) { wheel.Close(); CancelCharge(); return; }
        if (game.IsBotTurn() && !previousBot) round++;
        previousBot=game.IsBotTurn();
        for(int i=smoke.Count-1;i>=0;i--)
            if (smokeExpiry[i]<=round)
            {
                if (smokeClouds[i]!=null)
                {
                    smokeClouds[i].Stop(true,ParticleSystemStopBehavior.StopEmitting);
                    Destroy(smokeClouds[i].gameObject,2);
                }
                gasKind.RemoveAt(i); smoke.RemoveAt(i); smokeExpiry.RemoveAt(i); smokeClouds.RemoveAt(i);
            }
        Unit next = !game.IsGameOver() && !game.IsArmyDeploymentPhase() && !game.IsBotTurn() &&
            cameras.IsActionMode() ? cameras.GetCurrentControlledUnit() : null;
        if (actor!=next)
        {
            CloseScope();
            wheel.Close();
            if(jump!=null) { StopCoroutine(jump); jump=null; }
            selected=OsowiecKitItem.Rifle;
            CancelCharge(); actor=next; spentOnMarch=false; spent=false; stopWalking=false; leaping=false; note=""; feedbackUntil=0;
        }

        if (!actor || !OsowiecFireTurn.Allows(actor) || Keyboard.current==null) { wheel.Close(); CancelCharge(); return; }
        var keys=Keyboard.current;
        var mouse=Mouse.current;
        bool click=mouse!=null && mouse.leftButton.wasPressedThisFrame;
        if (scope)
        {
            if(keys.qKey.wasPressedThisFrame || keys.tKey.wasPressedThisFrame) CloseScope();
            else if(mouse!=null && Mathf.Abs(mouse.scroll.ReadValue().y)>.01f)
            {
                zoomed=mouse.scroll.ReadValue().y>0;
                lens.fieldOfView=zoomed?18:34;
            }
            else if(click && !Cooling)
            {
                UpdateScopeTarget();
                if(ScopeTarget)
                {
                    tagged=ScopeTarget;
                    taggedUntil=round+2;
                    taggedPosition=tagged.transform.position;
                    FogWarIntelTracker.Instance?.MarkVisible(tagged,tagged.transform.position);
                    OsowiecAtmosphere.PlayOptics(actor.transform.position);
                    FinishSkill();
                    note="Цель опознана. Два хода отряд наносит ей на 20% больше урона из винтовок.";
                }
            }
            blockFireFrame=Time.frameCount;
            return;
        }
        if (wheel.Open)
        {
            blockLookFrame=Time.frameCount;
            blockFireFrame=Time.frameCount;
            wheel.Point(actor,selected,mouse!=null ? mouse.delta.ReadValue() : Vector2.zero);
            if(CancelPressed || keys.escapeKey.wasPressedThisFrame)
                wheel.Close();
            else if(click || !(wheelOnF1?keys.f1Key:keys.gKey).isPressed)
            {
                if(wheel.OnSector) SelectItem(wheel.Hover);
                wheel.Close();
            }
            return;
        }
        if(chargingLeap || chargingThrow)
        {
            blockFireFrame=Time.frameCount;
            if(CancelPressed || keys.escapeKey.wasPressedThisFrame)
            {
                if(chargingThrow)
                {
                    OsowiecWeaponView.StowEquipment(actor,selected,Charge);
                    selected=OsowiecKitItem.Rifle;
                }
                CancelCharge(); note="Отменено"; feedbackUntil=Time.time+1f; return;
            }
            if(chargingLeap && keys.qKey.wasReleasedThisFrame)
            {
                float strength=Charge;
                CancelCharge();
                if(OsowiecGroundEvents.ClearWire(actor.transform.position,3f))
                {
                    FinishSkill(); note="Проход расчищен";
                    OsowiecAtmosphere.PlayPneumatic(actor.transform.position);
                }
                else if(TryLeap(strength,out var travel))
                {
                    FinishSkill(); leaping=true; note="Пневмопрыжок";
                    OsowiecAtmosphere.PlayPneumatic(actor.transform.position);
                    jump=StartCoroutine(Jump(actor,travel));
                }
                else note="Для приземления нужно свободное место";
                feedbackUntil=Time.time+1.8f;
            }
            else if(chargingThrow && mouse!=null && mouse.leftButton.wasReleasedThisFrame)
            {
                throwing.Aim(cameras.GetActionCamera(),actor,Charge);
                CancelCharge();
                if(SpendThrowable(actor,selected))
                {
                    var item=selected;
                    SpendAttack();
                    OsowiecPlanner.NoteAttack(actor);
                    StartCoroutine(throwing.Fly(item,actor,particleMaterial,Impact));
                    selected=OsowiecKitItem.Rifle;
                    OsowiecWeaponView.UsedEquipment(actor);
                    note="Бросок"; feedbackUntil=Time.time+1;
                }
            }
            return;
        }
        if(keys.qKey.wasPressedThisFrame && !leaping)
        {
            blockFireFrame=Time.frameCount;
            if(Cooling) note="Снаряжение класса будет готово в следующем ходу";
            else ClassAction();
            feedbackUntil=Time.time+2;
            return;
        }
        if(spent || leaping) return;
        if(keys.f1Key.wasPressedThisFrame || keys.gKey.wasPressedThisFrame)
        {
            var gun=actor.GetEquippedWeapon();
            if(gun && gun.IsReloading) {note="Дождитесь перезарядки.";return;}
            wheelOnF1=keys.f1Key.wasPressedThisFrame;
            wheel.Begin(selected);
            return;
        }
        if(keys.hKey.wasPressedThisFrame) { SelectItem(OsowiecKitItem.Grenade); return; }
        if(keys.vKey.wasPressedThisFrame && actor.chessType==ChessUnitType.Pawn) { SelectItem(OsowiecKitItem.Bayonet); return; }
        if(!click || selected==OsowiecKitItem.Rifle) return;
        blockFireFrame=Time.frameCount;
        if(selected==OsowiecKitItem.Bayonet)
        {
            OsowiecWeaponView.UsedEquipment(actor);
            CloseEncounter();
        }
        else if(Throwable(selected) && Remaining(actor,selected)>0)
        {
            chargingThrow=true; chargeAt=Time.time;
        }
    }

    void SelectItem(OsowiecKitItem item)
    {
        if(Throwable(item) && Remaining(actor,item)==0) {note="Запас исчерпан.";feedbackUntil=Time.time+2;return;}
        selected=item;
        note=item==OsowiecKitItem.Rifle?"":OsowiecKitWheel.Name(item)+(Throwable(item)?"   ·   Держите ЛКМ для броска":"   ·   ЛКМ применить");
    }
    void ClassAction()
    {
        var op=OsowiecOperation.Instance;
        if (op==null || actor==null) return;
        Vector3 at=actor.transform.position;
        if (actor.chessType==ChessUnitType.Guardian)
        {
            if (op.Pressure(actor)>.65f) {note="Переждите подавление в укрытии.";return;}
            actor.SetRemainingMoveMeters(actor.GetRemainingMoveMeters()+4);
            note="Гончая   •   Добавлено 4 м"; FinishSkill();
            OsowiecAtmosphere.PlayPneumatic(at); return;
        }
        if (actor.chessType==ChessUnitType.Bishop)
        {
            if (op.MaskOn(actor)) {note="Снимите маску для оптики.";return;}
            lens=CameraManager.Instance.GetActionCamera();
            originalFov=lens.fieldOfView; scopeRest=lens.transform.localPosition;
            scope=true;
            lens.fieldOfView=34;
            var origin=lens.transform.position;
            scopeLift=Physics.Raycast(origin,Vector3.up,out var ceiling,1.05f,~0,QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0,ceiling.distance-.15f):1f;
            lens.transform.position=origin+Vector3.up*scopeLift;
            note="Наведите перископ на цель и нажмите ЛКМ.";
            OsowiecAtmosphere.PlayOptics(at); return;
        }
        chargingLeap=true;
        chargeAt=Time.time;
        note="Отпустите Q для прыжка";
    }

    bool TryLeap(float strength,out Vector3 travel)
    {
        Vector3 at=actor.transform.position;
        float budget=Mathf.Lerp(2,5,strength);
        if(!OsowiecMarch.Free(actor)) budget=Mathf.Min(budget,actor.GetRemainingMoveMeters());
        travel=default;
        if(budget<1 || !NavMesh.SamplePosition(at+actor.transform.forward*budget,out var landing,1.2f,NavMesh.AllAreas)) return false;
        travel=landing.position-at;
        float planar=Vector3.ProjectOnPlane(travel,Vector3.up).magnitude;
        if(planar>budget+.15f || planar<.8f || Mathf.Abs(travel.y)>1.1f) return false;
        leapPath[0]=at;
        for(int i=1;i<leapPath.Length;i++)
        {
            float t=i/(float)(leapPath.Length-1);
            leapPath[i]=at+LeapOffset(travel,t);
            Vector3 delta=leapPath[i]-leapPath[i-1];
            int found=Physics.CapsuleCastNonAlloc(leapPath[i-1]+Vector3.up*.45f,leapPath[i-1]+Vector3.up*1.55f,
                .22f,delta.normalized,leapHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            for(int h=0;h<found;h++)
                if(!leapHits[h].transform.IsChildOf(actor.transform) && leapHits[h].normal.y<.65f) return false;
        }
        return true;
    }
    static Vector3 LeapOffset(Vector3 travel,float t) => travel*t+Vector3.up*(.88f*Mathf.Sin(t*Mathf.PI));
    IEnumerator Jump(Unit jumper,Vector3 travel)
    {
        var capsule=jumper.GetComponent<CharacterController>();
        Vector3 previous=Vector3.zero;
        for(float t=0;t<1;)
        {
            if (actor!=jumper || capsule==null || !capsule.enabled) break;
            t=Mathf.Min(1,t+Time.deltaTime/.55f);
            Vector3 offset=LeapOffset(travel,t);
            var before=jumper.transform.position;
            capsule.Move(offset-previous);
            var moved=jumper.transform.position-before;moved.y=0;
            jumper.ConsumeMoveMeters(moved.magnitude);
            previous=offset;yield return null;
        }
        leaping=false;
    }
    internal void Impact(OsowiecKitItem item,Unit thrower,Vector3 position)
    {
        if(item is OsowiecKitItem.Smoke or OsowiecKitItem.Gas)
        {
            var center=position+Vector3.up*.50f;
            smoke.Add(center);
            gasKind.Add(item==OsowiecKitItem.Gas);
            smokeExpiry.Add(round+2);
            smokeClouds.Add(Emit(center,item==OsowiecKitItem.Gas?new Color(.63f,.61f,.28f,.96f):new Color(.65f,.68f,.62f,.96f),2.9f,65,2.1f,true));
            OsowiecAtmosphere.PlayKitImpact(position,true);
            return;
        }
        var origin=position+Vector3.up*.13f;
        OsowiecAtmosphere.PlayKitImpact(position,false);
        Emit(origin,new Color(1,.77f,.24f,1),1.8f,32,.18f);
        var burst=Emit(origin,new Color(1f,.68f,.24f,.97f),.21f,0,.65f);
        var main=burst.main;
        main.startSpeed=new ParticleSystem.MinMaxCurve(2.6f,5.7f);
        main.gravityModifier=.6f;
        var shape=burst.shape; shape.radius=.16f;
        var streak=burst.GetComponent<ParticleSystemRenderer>();
        streak.renderMode=ParticleSystemRenderMode.Stretch;
        streak.velocityScale=.11f;
        streak.lengthScale=1.5f;
        burst.Emit(OsowiecFieldSettings.ReducedEffects ? 24 : 56);
        var debris=Emit(origin+Vector3.up*.3f,new Color(.44f,.38f,.30f,.89f),1.85f,0,1.35f);
        var plume=debris.main; plume.startSpeed=new ParticleSystem.MinMaxCurve(.4f,1.8f);
        var dustFade=new Gradient();
        dustFade.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.92f,.15f),
                new GradientAlphaKey(.71f,.55f),new GradientAlphaKey(0,1)});
        var color=debris.colorOverLifetime; color.enabled=true; color.color=dustFade;
        debris.Emit(OsowiecFieldSettings.ReducedEffects ? 32 : 55);
        if(!OsowiecFieldSettings.ReducedEffects) StartCoroutine(Flash(origin));
        if (!thrower) return;
        bool heavy=thrower.chessType==ChessUnitType.Pawn;
        foreach(var unit in FindObjectsByType<Unit>(FindObjectsInactive.Exclude))
        {
            if(!unit || !unit.isActiveAndEnabled || unit.owner==thrower.owner || unit.GetHealth()<=0) continue;
            int damage=BlastDamage(origin,unit);
            if(damage>0) unit.TakeDamage(heavy?damage:Mathf.CeilToInt(damage*.7f),thrower,false,"Граната");
        }
        StartCoroutine(Fragments(thrower,origin,heavy));
    }

    IEnumerator Fragments(Unit thrower,Vector3 origin,bool heavy)
    {
        // Painted flecks use the same game rays as their hit checks; solid scenery stops each fleck.
        int count=heavy?36:24;
        var positions=new Vector3[count];
        var directions=new Vector3[count];
        float yaw=Random.value*Mathf.PI*2;
        for(int i=0;i<count;i++)
        {
            float angle=yaw+i*2.39996f;
            float lift=Mathf.Lerp(.02f,.78f,(i+.5f)/count);
            directions[i]=new Vector3(Mathf.Cos(angle),lift,Mathf.Sin(angle)).normalized;
            positions[i]=origin;
        }
        var sparks=Emit(origin,new Color(.82f,.68f,.48f,.94f),.05f,0,.36f);
        var main=sparks.main; main.simulationSpace=ParticleSystemSimulationSpace.World;
        var sprite=sparks.GetComponent<ParticleSystemRenderer>();
        sprite.renderMode=ParticleSystemRenderMode.Stretch; sprite.velocityScale=.07f;
        float travelled=0;
        while(travelled<(heavy?4.7f:3.6f))
        {
            float step=Mathf.Min((heavy?4.7f:3.6f)-travelled,Time.deltaTime*18);
            if(step<=0){yield return null;continue;}
            for(int i=0;i<count;i++)
            {
                if(directions[i]==Vector3.zero) continue;
                var ray=new Ray(positions[i],directions[i]);
                if(OsowiecHitBody.Trace(thrower,ray,step,~0,out var hit,out var part))
                {
                    var unit=hit.collider.GetComponentInParent<Unit>();
                    if(unit && unit.owner!=thrower.owner && unit.GetHealth()>0)
                    {
                        int damage=part ? part.area switch { OsowiecHitArea.Helmet=>1,OsowiecHitArea.Head=>7,
                            OsowiecHitArea.Limb=>3,_=>5 } : 5;
                        unit.TakeDamage(heavy?damage+1:damage,thrower,false,"Осколок");
                    }
                    directions[i]=Vector3.zero;
                }
                else
                {
                    positions[i]=ray.GetPoint(step);
                    if(!OsowiecFieldSettings.ReducedEffects || i%3==0)
                    {
                        var fleck=new ParticleSystem.EmitParams {position=positions[i],velocity=directions[i]*6,
                            startLifetime=.04f,startSize=.042f};
                        sparks.Emit(fleck,1);
                    }
                }
            }
            travelled+=step;
            yield return null;
        }
    }

    internal static int BlastDamage(Vector3 origin,Unit unit)
    {
        var body=unit.transform.position+Vector3.up*.77f;
        float distance=Vector3.Distance(origin,body);
        if(distance>=3f) return 0;
        if(Physics.Linecast(origin,body,out var hit,~0,QueryTriggerInteraction.Ignore) &&
            hit.collider.GetComponentInParent<Unit>()!=unit) return 0;
        return Mathf.RoundToInt(Mathf.Lerp(16,0,distance/3f));
    }

    IEnumerator Flash(Vector3 at)
    {
        var glow=new GameObject("Вспышка гранаты").AddComponent<Light>();
        glow.transform.position=at+Vector3.up*.3f;
        glow.color=new Color(1,.69f,.30f);
        glow.range=7;
        glow.shadows=LightShadows.None;
        for(float t=0;t<.20f;t+=Time.deltaTime)
        {
            glow.intensity=4f*(1-t/.2f);
            yield return null;
        }
        Destroy(glow.gameObject);
    }

    void CloseEncounter()
    {
        var eye=CameraManager.Instance.GetActionCamera();
        var aim=eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
        bool contact=OsowiecHitBody.Trace(actor,aim,2.1f,~0,out var hit,out _);
        var enemy=contact ? hit.collider.GetComponentInParent<Unit>() : null;
        if(!enemy || enemy.owner==actor.owner || enemy.GetHealth()<=0)
        {
            note="Цель должна быть под прицелом вблизи.";
            feedbackUntil=Time.time+1.5f;
            return;
        }
        OsowiecPlanner.NoteAttack(actor);
        enemy.TakeDamage(65,actor,false,"Удар штыком");
        SpendAttack();
        note="Ближний удар";
        OsowiecAtmosphere.PlayPneumatic(actor.transform.position);
    }
    ParticleSystem Emit(Vector3 pos,Color color,float size,int count,float duration,bool continuous=false)
    {
        var fx=new GameObject("Field smoke");
        fx.transform.position=pos;
        var sys=fx.AddComponent<ParticleSystem>();
        sys.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=sys.main;main.loop=continuous;main.startLifetime=duration;main.startSpeed=.34f;main.startSize=size;main.startColor=color;
        var emit=sys.emission;emit.rateOverTime=continuous?24:0;
        var shape=sys.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=size*.55f;
        var renderer=sys.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=particleMaterial;
        sys.Play();sys.Emit(count);
        if(!continuous) Destroy(fx,duration+.4f);
        return sys;
    }
    void LateUpdate()
    {
        if(!actor) return;
        if(scope && lens)
        {
            if(!lens.transform.parent || !lens.transform.IsChildOf(actor.transform)) { CloseScope(); return; }
            lens.transform.position=lens.transform.parent.TransformPoint(scopeRest)+Vector3.up*scopeLift;
            UpdateScopeTarget();
        }
        else if(chargingLeap) validLeap=TryLeap(Charge,out _);
        else if(!wheel.Open && !spent && Throwable(selected) && CameraManager.Instance)
            throwing.Aim(CameraManager.Instance.GetActionCamera(),actor,chargingThrow?Charge:0);
    }

    void UpdateScopeTarget()
    {
        ScopeTarget=null;
        if(!lens || !actor) return;
        // The illuminated sight ring also accepts a nearby silhouette inside the glass.
        float best=.056f;
        foreach(var u in OsowiecOperation.Instance.FieldRoster)
        {
            if(!u || !u.isActiveAndEnabled || u.owner==actor.owner || u.GetHealth()<=0) continue;
            var center=u.transform.position+Vector3.up*1.22f;
            var p=lens.WorldToViewportPoint(center);
            float offset=new Vector2((p.x-.5f)*lens.aspect,p.y-.5f).magnitude;
            if(p.z<=0 || p.z>70 || offset>best || Obscured(lens.transform.position,center)) continue;
            if(Physics.Linecast(lens.transform.position,center,out var cover,~0,QueryTriggerInteraction.Ignore) &&
                cover.collider.GetComponentInParent<Unit>()!=u) continue;
            best=offset; ScopeTarget=u;
        }
    }
    void OnGUI()
    {
        var game=GameManager.Instance;
        var manager=CameraManager.Instance;
        if(game==null || manager==null || game.IsPaused() || game.IsGameOver() || game.IsArmyDeploymentPhase()) return;
        float s=OsowiecHudPaint.Scale;
        if(IsIdentified(tagged) && !manager.IsActionMode())
        {
            var pixel=manager.GetTacticalCamera().WorldToScreenPoint(taggedPosition+Vector3.up*2);
            if(pixel.z>0) OsowiecHudPaint.Plate(new Rect(pixel.x-75*s,Screen.height-pixel.y-24*s,150*s,24*s),"Место отметки",true);
        }
        if(actor==null || !manager.IsActionMode()) return;
        int depth=GUI.depth;
        GUI.depth=-50;
        if(wheel.Open) wheel.Draw(actor,wheelOnF1);
        else if(scope) wheel.DrawScope(ScopeTarget,Cooling,ScopeTarget?Vector3.Distance(actor.transform.position,ScopeTarget.transform.position):0f,zoomed);
        else if(chargingLeap && validLeap) OsowiecKitThrow.DrawPath(manager.GetActionCamera(),leapPath,leapPath.Length);
        else if(!spent && Throwable(selected)) throwing.DrawAim(manager.GetActionCamera());
        if(!scope && !wheel.Open)
        {
            wheel.DrawAbility(actor,Cooling,false);
            if(chargingLeap || Throwable(selected))
            {
                string action=chargingLeap?(validLeap?"Отпустить Q":"Нет места для прыжка"):chargingThrow?"Отпустить ЛКМ":"Держите ЛКМ";
                string level=chargingLeap || chargingThrow ? "   "+Mathf.RoundToInt(Charge*100)+"%" : "";
                string cancel=chargingLeap || chargingThrow ? "   ·   "+OsowiecKitWheel.CancelName+" × Отмена" : "";
                OsowiecHudPaint.Plate(new Rect(Screen.width*.5f-165*s,Screen.height*.67f,330*s,24*s),action+level+cancel,true);
            }
        }
        GUI.depth=depth;
    }
}
