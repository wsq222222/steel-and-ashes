using System;
using UnityEngine;

// Ambient staff and physical admissions use the confirmed count from the real relief column.
// This service never owns a playable Unit, spends supply, or writes mission progress.
public sealed class OsowiecWesternWard : MonoBehaviour
{
    static readonly string[] Patients = { "Фомин", "Лебедев", "Зотов" };
    public static string PatientName(int index) => Patients[index];
    public static string Ledger(int arrivals) => string.Join(" · ", Patients, 0, Mathf.Clamp(arrivals,0,Patients.Length));
    public OsowiecReliefColumn relief;
    public Transform[] people;
    public Animator[] bodies;
    public Transform stretcher;
    public Transform waterPail;
    public GameObject[] occupiedBeds;
    public GameObject curtain;
    public AudioClip[] responses;
    public int Admitted { get; private set; }

    static readonly float[] Berths = { -48.68f, -46.86f, -45.04f };
    static readonly Vector3 Landing = new(-48.94f, 0, -15.66f);
    static readonly Vector3 Aisle = new(-49.42f, 0, -11.67f);
    static readonly Vector3 PhysicianStart = new(-52.88f,0,-8.94f);
    static readonly Vector3 PhysicianEnd = new(-51.06f,0,-8.94f);
    static readonly Vector3 DoctorAisleA = new(-52.88f,0,-10.36f);
    static readonly Vector3 DoctorAisleB = new(-51.06f,0,-10.36f);
    static readonly Vector3 WaterStart = new(-44.13f,0,-12.02f);
    static readonly Vector3 WaterEnd = new(-44.19f,0,-10.17f);
    static readonly Vector3 FrontStand = new(-50.22f, 0, -13.37f);
    static readonly Vector3 RearStand = new(-48.52f, 0, -13.58f);
    const float EndHandle = 1.30f;

    enum Intake { Waiting, Meet, Lift, Ramp, Across, Berth, Transfer, ReturnAisle, ReturnStands }
    Intake stage;
    float clock, serviceTime;
    float waterClock,doctorClock;
    Unit[] occupants;
    Vector3 litterFoot;
    readonly string[] activePose = new string[4];
    readonly AudioSource[] movement = new AudioSource[4];
    readonly Transform[] wrists = new Transform[4];
    readonly OsowiecWardGait[] gaits = new OsowiecWardGait[4];
    AudioSource voice;
    bool closeLastFrame;
    bool pausedLastFrame;
    bool holdingLitter;
    int doctorPhase;
    public int ResidentAtHand { get; private set; } = -1;
    internal bool WaitingForLitter => stage==Intake.Waiting;
    internal float WorkTime => clock;
    internal float WaterDip { get; private set; }
    internal bool AtBarrel { get; private set; }
    readonly Plane[] viewPlanes = new Plane[6];
    static readonly Bounds WardBounds = new(new Vector3(-48.5f,.5f,-12.1f),new Vector3(12,3.5f,9.5f));

    void Awake()
    {
        ResetWard();
        for (int i=0;i<4;i++)
        {
            gaits[i]=new OsowiecWardGait(people[i],bodies[i]);
            bodies[i].cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var coat in bodies[i].GetComponentsInChildren<SkinnedMeshRenderer>())
                coat.updateWhenOffscreen=true;
            bodies[i].enabled=false;
            movement[i]=OsowiecSoundBank.Source(people[i],"Ward boot contact","Foley",1,180);
            movement[i].minDistance=.8f; movement[i].maxDistance=16;
        }
        voice=OsowiecSoundBank.Source(people[0],"Intake call","Foley",1,114);
        voice.transform.localPosition=Vector3.up*1.44f;
        voice.minDistance=2; voice.maxDistance=19;
    }

    void LateUpdate()
    {
        if(closeLastFrame && !pausedLastFrame)
        {
            for(int i=0;i<gaits.Length;i++)
            {
                var gait=gaits[i];
                gait.Apply();
                if(gait.HeelStrike<0)continue;
                Vector3 sole=gait.ContactPoint;
                OsowiecWetTracks.Boot(sole,people[i].forward,gait.LeftShoe);
                movement[i].transform.position=sole+Vector3.up*.045f;
                movement[i].PlayOneShot(OsowiecSoundBank.Variant("StepTimber",8),.20f*OsowiecEnvironmentAudio.EffectsVolume);
            }
            AlignCarryToHands();
        }
    }

    // Both bearers keep their grip on the side poles, including on the slope where their feet occupy different heights.
    public void AlignCarryToHands()
    {
        if(stage!=Intake.Ramp && stage!=Intake.Across && stage!=Intake.Berth)return;
        if(!wrists[0])
        {
            for(int i=0;i<4;i++)
            {
                string joint=i%2==0?"LHand":"RHand";
                foreach(var bone in bodies[2+i/2].GetComponentsInChildren<Transform>(true))
                    if(bone.name==joint){wrists[i]=bone;break;}
            }
        }
        if(!wrists[0] || !wrists[1] || !wrists[2] || !wrists[3])return;
        var front=(wrists[0].position+wrists[1].position)*.5f+people[2].forward*.045f;
        var rear=(wrists[2].position+wrists[3].position)*.5f+people[3].forward*.045f;
        stretcher.SetPositionAndRotation((front+rear)*.5f-Vector3.up*.013f,Quaternion.LookRotation(rear-front));
    }

    public void ResetWard()
    {
        stage=Intake.Waiting; clock=serviceTime=doctorClock=0; waterClock=1.4f; Admitted=0;
        foreach (var bed in occupiedBeds) bed.SetActive(false);
        curtain.SetActive(false); stretcher.gameObject.SetActive(false);
        holdingLitter=false;
        WaterDip=0;
        foreach(var gait in gaits)gait?.Stop();
        Array.Clear(activePose,0,activePose.Length);
        doctorPhase=0;ResidentAtHand=0;
        Place(0,PhysicianStart,Vector3.right); Place(1,WaterStart,Vector3.right);
        Place(2,FrontStand,Vector3.back); Place(3,RearStand,Vector3.back);
    }

    void Update()
    {
        var game=GameManager.Instance;
        bool paused=game!=null && game.IsPaused();
        if (paused!=pausedLastFrame)
        {
            foreach (var animator in bodies) animator.speed=paused ? 0 : 1;
            pausedLastFrame=paused;
        }
        if (paused || !relief) return;
        foreach(var gait in gaits)gait?.Reset();
        Camera cam=Camera.main;
        bool near=WardInView(cam);
        var cameras=CameraManager.Instance;
        if(!near && cameras && !cameras.IsActionMode())
        {
            var overhead=cameras.GetTacticalCamera();
            if(overhead!=cam)near=WardInView(overhead);
        }
        if(near!=closeLastFrame)
        {
            foreach(var animator in bodies) animator.enabled=near;
            Array.Clear(activePose,0,activePose.Length);
            closeLastFrame=near;
        }
        Tick(Time.deltaTime,relief.Delivered,near,OsowiecOperation.Instance?.FieldRoster);
    }

    bool WardInView(Camera camera)
    {
        if(!camera || !camera.isActiveAndEnabled)return false;
        if((camera.transform.position-Aisle).sqrMagnitude<35f*35f)return true;
        if(camera.transform.forward.y>-.4f)return false;
        GeometryUtility.CalculateFrustumPlanes(camera,viewPlanes);
        return GeometryUtility.TestPlanesAABB(viewPlanes,WardBounds);
    }

    public static float DeckHeight(Vector3 point)
    {
        float t=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-15.94f,-12.03f,point.z));
        return .082f-.626f*t;
    }

    void Place(int who,Vector3 destination,Vector3 facing)
    {
        destination.y=DeckHeight(destination);
        people[who].SetPositionAndRotation(destination,Quaternion.LookRotation(facing));
    }

    void Pose(int who,string name,bool visible)
    {
        if (!visible || !Application.isPlaying || activePose[who]==name) return;
        activePose[who]=name;
        bodies[who].speed=1;
        bodies[who].CrossFadeInFixedTime("Base Layer."+name,.14f,0,who*.09f);
    }

    void Staff(int who,Vector3 dest,Vector3 face,string pose,float dt,bool close)
    {
        var person=people[who];
        dest.y=DeckHeight(dest);
        var prev=person.position;
        person.position=dest;
        if(face.sqrMagnitude>.001f) person.rotation=Quaternion.RotateTowards(person.rotation,Quaternion.LookRotation(face),dt*275f);
        Pose(who,pose,close);
        float ds=Vector2.Distance(new Vector2(prev.x,prev.z),new Vector2(dest.x,dest.z));
        if (!close || !Application.isPlaying || ds>1f) return;
        if (ds<.0001f) return;
        if(pose=="Walk" || pose=="Carry" || pose=="CarryBack")
            gaits[who]?.Advance(dest-prev,dt,pose!="Walk",bodies[who]);
    }

    static Vector3 Flat(Vector3 p) => new(p.x,0,p.z);
    static float Smoothed(float seconds,float duration) => Mathf.SmoothStep(0,1,Mathf.Clamp01(seconds/duration));

    // The assistant stays on the east wall and the physician clears the active receiving berth.
    bool CanAdvance(Vector3 current,Vector3 candidate,float clearance)
    {
        if(occupants==null)return true;
        current=Flat(current);candidate=Flat(candidate);
        Vector3 movement=candidate-current;
        if(movement.sqrMagnitude<.000001f)return true;
        foreach(var unit in occupants)
        {
            if(!unit || unit.GetHealth()<=0 || !unit.isActiveAndEnabled)continue;
            Vector3 foot=unit.transform.position;
            if(Mathf.Abs(foot.y-DeckHeight(candidate))>1.6f)continue;
            Vector3 offset=Flat(foot)-current;
            float along=Vector3.Dot(movement,offset);
            if(along<=0)continue;
            Vector3 closest=current+movement*Mathf.Clamp01(along/movement.sqrMagnitude);
            if((Flat(foot)-closest).sqrMagnitude<clearance*clearance)return false;
        }
        return true;
    }

    void Work(int who,Vector3 from,Vector3 to,ref float time,float hold,string resting,float dt,bool close)
    {
        float motion=Vector3.Distance(from,to)/.79f;
        float cycle=Mathf.Repeat(time,2*(hold+motion));
        bool returning=cycle>=hold+motion;
        if(returning)cycle-=hold+motion;
        Vector3 a=returning?to:from,b=returning?from:to;
        bool stopped=cycle<hold;
        if(who==1)
        {
            AtBarrel=!returning;
            WaterDip=stopped ? Mathf.Pow(Mathf.Sin(Mathf.PI*cycle/hold),2) : 0;
        }
        Vector3 point=stopped?a:Vector3.Lerp(a,b,(cycle-hold)/motion);
        Vector3 facing=stopped?(who==0?Vector3.forward:Vector3.right):(b-a).normalized;
        Vector3 here=Flat(people[who].position);
        point=Vector3.MoveTowards(here,point,.84f*dt);
        if(!CanAdvance(here,point,.56f)){Pose(who,"Idle",close);return;}
        time+=dt;
        Staff(who,point,facing,stopped?resting:"Walk",dt,close);
    }

    public void Tick(float dt,int delivered,bool close,Unit[] fieldActors=null)
    {
        if(dt<=0) return;
        occupants=fieldActors;
        clock+=dt;serviceTime+=dt;
        Work(1,WaterStart,WaterEnd,ref waterClock,3.65f,"Lift",dt,close);
        // Detached preview simulations have no animated skeleton. In the player the hand drives the handle.
        if(!close)
        {
            var water=people[1];
            waterPail.SetPositionAndRotation(water.position+water.right*.30f+water.forward*.14f+Vector3.up*.84f,
                water.rotation);
        }
        AttendResidents(dt,close);
        if(stage==Intake.Waiting)
        {
            Pose(2,"Idle",close);Pose(3,"Idle",close);
            if(delivered<=Admitted || Admitted>=occupiedBeds.Length)return;
            if(close && voice && responses!=null && Admitted<responses.Length && responses[Admitted])
                voice.PlayOneShot(responses[Admitted],.81f*OsowiecEnvironmentAudio.EffectsVolume);
            litterFoot=Landing;
            stretcher.gameObject.SetActive(true);
            DrawLitter(.24f);
            Next(Intake.Meet);
        }
        float x=Berths[Mathf.Min(Admitted,2)];
        Vector3 lane=new(x+.65f,0,Aisle.z);
        Vector3 mattress=new(x+.65f,0,-8.98f);
        switch(stage)
        {
            case Intake.Meet:
                if(Approach(2,Landing+Vector3.forward*EndHandle,Vector3.back,dt,close) &
                   Approach(3,Landing+Vector3.back*EndHandle,Vector3.forward,dt,close))Next(Intake.Lift);
                break;
            case Intake.Lift:
                Pose(2,"Lift",close);Pose(3,"Lift",close);DrawLitter(Mathf.Lerp(.24f,.81f,Smoothed(serviceTime,1.5f)));
                if(serviceTime>=1.5f)Next(Intake.Ramp);
                break;
            case Intake.Ramp:
                if(Carry(Aisle,dt,close))Next(Intake.Across);break;
            case Intake.Across:
                if(Carry(lane,dt,close))Next(Intake.Berth);break;
            case Intake.Berth:
                if(Carry(mattress,dt,close))Next(Intake.Transfer);break;
            case Intake.Transfer:
                var transfer=Vector3.Lerp(mattress,new Vector3(x,0,mattress.z),Smoothed(serviceTime,1.4f));
                if(!PassLitter(transfer,close)){serviceTime-=dt;break;}
                litterFoot=transfer;
                Staff(2,litterFoot+Vector3.forward*EndHandle,Vector3.back,"Lift",dt,close);
                Staff(3,litterFoot+Vector3.back*EndHandle,Vector3.forward,"Lift",dt,close);
                DrawLitter(Mathf.Lerp(.81f,.48f,Smoothed(serviceTime,1.4f)));
                if(serviceTime>=1.4f)
                {
                    occupiedBeds[Admitted++].SetActive(true);
                    if(Admitted==3)curtain.SetActive(true);
                    stretcher.gameObject.SetActive(false);
                    Next(Intake.ReturnAisle);
                }
                break;
            case Intake.ReturnAisle:
            case Intake.ReturnStands:
                // Leave beds by the same clear aisle before separating at the ramp.
                bool returningToAisle=stage==Intake.ReturnAisle;
                int berth=Mathf.Max(0,Admitted-1);
                var shoulder=new Vector3(Berths[berth]+.65f,0,-11.81f);
                bool first=Approach(2,returningToAisle?shoulder+Vector3.forward*EndHandle:FrontStand,Vector3.back,dt,close);
                bool second=Approach(3,returningToAisle?shoulder+Vector3.back*EndHandle:RearStand,Vector3.back,dt,close);
                if(first && second)Next(returningToAisle?Intake.ReturnStands:Intake.Waiting);
                break;
        }
    }

    void AttendResidents(float dt,bool near)
    {
        bool returning=doctorPhase>=4;
        int leg=doctorPhase%4;
        ResidentAtHand=-1;
        if(leg==0)
        {
            ResidentAtHand=returning ? 1 : 0;
            Staff(0,returning?PhysicianEnd:PhysicianStart,Vector3.right,"Tend",dt,near);
            doctorClock+=dt;
            if(doctorClock>6.15f){doctorClock=0;doctorPhase++;}
            return;
        }
        Vector3 target=leg==1?(returning?DoctorAisleB:DoctorAisleA):
            leg==2?(returning?DoctorAisleA:DoctorAisleB):(returning?PhysicianStart:PhysicianEnd);
        if(Approach(0,target,Vector3.right,dt,near))doctorPhase=(doctorPhase+1)%8;
    }

    void DrawLitter(float lift)
    {
        Vector3 center=litterFoot+Vector3.up*(DeckHeight(litterFoot)+lift);
        float dz=DeckHeight(litterFoot+Vector3.forward*.75f)-DeckHeight(litterFoot-Vector3.forward*.75f);
        stretcher.SetPositionAndRotation(center,Quaternion.LookRotation(new Vector3(0,-dz,-1.5f)));
    }

    bool Approach(int index,Vector3 target,Vector3 finalFacing,float dt,bool near)
    {
        Vector3 at=Flat(people[index].position);
        Vector3 dest=Vector3.MoveTowards(at,target,dt*.94f);
        if(!CanAdvance(at,dest,.56f)){Pose(index,"Idle",near);return false;}
        bool arrived=(dest-target).sqrMagnitude<.0001f;
        Staff(index,dest,arrived?finalFacing:target-at,arrived?"Idle":"Walk",dt,near);
        return arrived;
    }

    bool Carry(Vector3 target,float dt,bool close)
    {
        var next=Vector3.MoveTowards(litterFoot,target,dt*.80f);
        if(!PassLitter(next,close))return false;
        litterFoot=next;
        DrawLitter(.81f);
        Staff(2,litterFoot+Vector3.forward*EndHandle,Vector3.back,"CarryBack",dt,close);
        Staff(3,litterFoot+Vector3.back*EndHandle,Vector3.forward,"Carry",dt,close);
        return (litterFoot-target).sqrMagnitude<.0001f;
    }

    bool PassLitter(Vector3 next,bool close)
    {
        // All five sample points move as one. This also protects the load during the sideways turn to a berth.
        for(int i=-2;i<=2;i++)
        {
            Vector3 offset=Vector3.forward*(EndHandle*i*.5f);
            if(CanAdvance(litterFoot+offset,next+offset,.69f))continue;
            holdingLitter=true;
            if(close && bodies[2] && bodies[3])bodies[2].speed=bodies[3].speed=0;
            return false;
        }
        if(holdingLitter && close && bodies[2] && bodies[3])bodies[2].speed=bodies[3].speed=1;
        holdingLitter=false;
        return true;
    }

    void Next(Intake next) {stage=next;serviceTime=0;}
}
