using UnityEngine;

/// <summary>Geometry and presentation share the axle as their origin, in metres.</summary>
public sealed class OsowiecCartMotion : MonoBehaviour
{
    public const float WheelRadius = .488f;
    public Transform leftWheel, rightWheel, flywheel, rod;
    public Animator orderly;
    Quaternion leftRest, rightRest;
    Vector3 rodRest, leftPrint, rightPrint, lastAxle;
    Quaternion lastYaw;
    bool havePrints, havePose, carrying;
    struct Arm
    {
        public Transform shoulder, elbow, wrist;
        public float side;
    }
    Arm[] tillerArms;
    AudioSource motor;
    float roll, motorAngle, motorGain;
    static readonly Collider[] contacts = new Collider[48];
    static readonly RaycastHit[] soles = new RaycastHit[24];

    static bool RoadSurface(Collider item)
    {
        string label = item.name;
        return label.StartsWith("Biebrza ground", System.StringComparison.Ordinal) ||
               label.StartsWith("Duckboard deck ", System.StringComparison.Ordinal) || label == "Casemate brick floor" ||
               label.StartsWith("Walkway_", System.StringComparison.Ordinal) ||
               label.StartsWith("Event_", System.StringComparison.Ordinal) || label == "Brickfloor";
    }

    // Query the chassis, front shafts, and the orderly's occupied space individually.
    // A disc approximating the rig would permanently block even the wide straight gateway.
    static readonly Vector3[] offsets={new(0,.63f,-.03f),new(0,.68f,1.29f),new(0,.90f,-1.37f)};
    static readonly Vector3[] halves={new(.54f,.36f,.95f),new(.34f,.21f,.47f),new(.29f,.79f,.40f)};
    public static bool Clearance(Vector3 axle, Quaternion heading, out Collider blocker,bool people=false)
    {
        for(int i=0;i<offsets.Length;i++)
            if(!FreeBox(axle,heading,offsets[i],halves[i],people,out blocker))return false;
        blocker=null;
        return true;
    }

    static bool FreeBox(Vector3 axle,Quaternion q,Vector3 offset,Vector3 half,bool people,out Collider blocker)
    {
        int n=Physics.OverlapBoxNonAlloc(axle+q*offset,half,contacts,q,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
        for(int i=0;i<n;i++)
        {
            var c=contacts[i];
            if(c.isTrigger && !c.name.StartsWith("WireSnag_",System.StringComparison.Ordinal))continue;
            if(RoadSurface(c)) continue;
            if(c.GetComponentInParent<OsowiecCartMotion>())continue;
            var unit=c.GetComponentInParent<Unit>();
            if(unit && (!people || unit.GetHealth()<=0))continue;
            if(unit && c is CharacterController walker)
            {
                // PhysX can retain the controller's swept broadphase bounds until the next physics tick.
                // Use its current upright capsule to decide whether it still occupies this body section.
                Vector3 relative=Quaternion.Inverse(q)*(walker.bounds.center-axle)-offset;
                float sx=Mathf.Max(0,Mathf.Abs(relative.x)-half.x), sz=Mathf.Max(0,Mathf.Abs(relative.z)-half.z);
                float radius=Mathf.Max(walker.bounds.extents.x,walker.bounds.extents.z)+.025f;
                if(sx*sx+sz*sz>radius*radius || Mathf.Abs(relative.y)>walker.bounds.extents.y+half.y)continue;
            }
            blocker=c;return false;
        }
        blocker=null;
        return n<contacts.Length;
    }

    public static bool Sweep(Vector3 from, Quaternion oldHeading, Vector3 to, Quaternion newHeading, out Collider blocker,bool people=false)
    {
        int n=Mathf.Max(1,Mathf.Max(Mathf.CeilToInt(Vector3.Distance(from,to)/.14f),Mathf.CeilToInt(Quaternion.Angle(oldHeading,newHeading)/4f)));
        for(int i=0;i<=n;i++)
        {
            float t=(float)i/n;
            if(!Clearance(Vector3.Lerp(from,to,t),Quaternion.Slerp(oldHeading,newHeading,t),out blocker,people)) return false;
        }
        blocker=null;return true;
    }

    static Vector3 Contact(Vector3 above)
    {
        int n=Physics.RaycastNonAlloc(above+Vector3.up*.75f,Vector3.down,soles,1.65f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        float dist=float.PositiveInfinity;
        Vector3 result=above;
        for(int i=0;i<n;i++)
        {
            var hit=soles[i];
            if(hit.distance>=dist || hit.normal.y<.50f || !RoadSurface(hit.collider)) continue;
            dist=hit.distance;result=hit.point;
        }
        return result;
    }

    void Awake()
    {
        CaptureRest();
        for(int i=0;i<offsets.Length;i++)
        {
            var hull=gameObject.AddComponent<BoxCollider>();
            hull.center=offsets[i];
            hull.size=2*halves[i];
        }
        motor=OsowiecSoundBank.Source(transform,"Air engine · transmission","World",1f,109);
        motor.transform.localPosition=new Vector3(0,.54f,1.15f);
        motor.clip=OsowiecSoundBank.Clip("CartAirMotor");
        motor.loop=true;
        motor.volume=0;
    }

    void CaptureRest()
    {
        leftRest=leftWheel.localRotation;
        rightRest=rightWheel.localRotation;
        rodRest=rod.localPosition;
        if (orderly && tillerArms==null)
        {
            var bones=orderly.GetComponentsInChildren<Transform>(true);
            Transform Part(string key) => System.Array.Find(bones,b=>b.name==key);
            tillerArms=new Arm[2];
            for(int i=0;i<2;i++)
            {
                string side=i==0?"L":"R";
                var wrist=Part(side+"Hand");
                tillerArms[i]=new Arm {shoulder=Part(side+"Arm"),elbow=Part(side+"Forearm"),wrist=wrist,
                    side=Mathf.Sign(transform.InverseTransformPoint(wrist.position).x)};
            }
        }
    }

    // Wrist lies just behind the wrapped grip. Fingertips curl over its far edge.
    Vector3 TillerWrist(float side) => transform.TransformPoint(new Vector3(side*.288f,.932f,-1.193f));

    void LateUpdate() => FitTillerArms();

    void FitTillerArms()
    {
        if(tillerArms==null) return;
        foreach(var arm in tillerArms)
        {
            Vector3 target=TillerWrist(arm.side);
            Vector3 origin=arm.shoulder.position;
            float upper=Vector3.Distance(origin,arm.elbow.position);
            float lower=Vector3.Distance(arm.elbow.position,arm.wrist.position);
            Vector3 reach=target-origin;
            float distance=Mathf.Clamp(reach.magnitude,Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
            Vector3 forward=reach.normalized;
            Vector3 hinge=Vector3.ProjectOnPlane(transform.right*arm.side*.66f-transform.forward,forward).normalized;
            float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            Vector3 elbow=origin+along*forward+hinge*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            var handRotation=arm.wrist.rotation;
            arm.shoulder.rotation=Quaternion.FromToRotation(arm.elbow.position-origin,elbow-origin)*arm.shoulder.rotation;
            arm.elbow.rotation=Quaternion.FromToRotation(arm.wrist.position-arm.elbow.position,target-arm.elbow.position)*arm.elbow.rotation;
            arm.wrist.rotation=handRotation;
        }
    }

#if UNITY_EDITOR
    public void VerifyDrive()
    {
        if(!leftWheel || !rightWheel || !flywheel || !rod || leftWheel==rightWheel)
            throw new System.InvalidOperationException("Missing wheel, crank or connecting rod on the sanitary carriage prefab.");
        CaptureRest();
        var carryClip=System.Array.Find(orderly.runtimeAnimatorController.animationClips,c=>c.name.EndsWith("Carry",System.StringComparison.Ordinal));
        float maxGap=0,minKnee=180,maxKnee=0,minElbow=180;
        var bones=orderly.GetComponentsInChildren<Transform>(true);
        var hip=System.Array.Find(bones,t=>t.name=="LThigh");
        var knee=System.Array.Find(bones,t=>t.name=="LShin");
        var ankle=System.Array.Find(bones,t=>t.name=="LFoot");
        for(int i=0;i<16;i++)
        {
            carryClip.SampleAnimation(orderly.gameObject,carryClip.length*i/16f);
            FitTillerArms();
            float bend=Vector3.Angle(knee.position-hip.position,ankle.position-knee.position);
            minKnee=Mathf.Min(minKnee,bend); maxKnee=Mathf.Max(maxKnee,bend);
            foreach(var arm in tillerArms)
            {
                maxGap=Mathf.Max(maxGap,Vector3.Distance(arm.wrist.position,TillerWrist(arm.side)));
                minElbow=Mathf.Min(minElbow,Vector3.Angle(arm.elbow.position-arm.shoulder.position,arm.wrist.position-arm.elbow.position));
            }
        }
        if(maxGap>.025f || maxKnee-minKnee<16 || minElbow<5)
            throw new System.InvalidOperationException($"Tiller pose: wrist gap {maxGap:F3} m, knee {minKnee:F1}..{maxKnee:F1}°, minimum elbow bend {minElbow:F1}°.");
        Debug.Log($"PASS tiller: both wrists within {maxGap:F3} m over 16 carrying poses, knee bend {minKnee:F1}..{maxKnee:F1}°, minimum elbow bend {minElbow:F1}°.");
        var parked=leftWheel.localRotation;
        var driveRest=flywheel.localRotation;
        Place(transform.position,Quaternion.identity,.016f,0);
        var moved=leftWheel.localRotation;
        var driven=flywheel.localRotation;
        if(Quaternion.Angle(moved,parked)<1f || Quaternion.Angle(driven,driveRest)<12f || Vector3.Distance(rod.localPosition,rodRest)<.01f)
            throw new System.InvalidOperationException("The visible cart drive did not engage.");
        Place(transform.position,Quaternion.identity,0,0);
        if(Quaternion.Angle(leftWheel.localRotation,moved)>.01f || Quaternion.Angle(flywheel.localRotation,driven)>.01f)
            throw new System.InvalidOperationException("The stationary cart drive is still turning.");
    }
#endif

    public void Suspend()
    {
        if(motor!=null && motor.isPlaying)motor.Stop();
        motorGain=0;
    }

    public void Place(Vector3 axle, Quaternion yaw,float travelled,float dt)
    {
        bool moving=travelled>.000001f;
        if(orderly!=null && carrying!=moving)
        {
            orderly.CrossFadeInFixedTime(moving?"Carry":"Idle",.20f);
            carrying=moving;
        }
        if(dt>0)UpdateMotor(travelled/dt,dt);
        if(!moving && havePose && (axle-lastAxle).sqrMagnitude<.000001f && Quaternion.Angle(yaw,lastYaw)<.01f)return;
        var right=yaw*Vector3.right;
        var forward=yaw*Vector3.forward;
        var leftContact=Contact(axle-right*.491f);
        var rightContact=Contact(axle+right*.491f);
        Vector3 side=rightContact-leftContact;
        Vector3 rise=Contact(axle+forward*.55f)-Contact(axle-forward*.55f);
        Vector3 up=Vector3.Cross(rise,side).normalized;
        if(up.y<.4f)up=Vector3.up;
        Quaternion pitch=Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,up),up);
        Vector3 pivot=(leftContact+rightContact)*.5f+(Vector3.up-up)*WheelRadius;
        transform.SetPositionAndRotation(pivot,pitch);
        if(!havePose || (axle-lastAxle).sqrMagnitude>2.25f) {havePrints=false;havePose=true;}
        lastAxle=axle;lastYaw=yaw;
        roll=Mathf.Repeat(roll+travelled*Mathf.Rad2Deg/WheelRadius,360f);
        leftWheel.localRotation=leftRest*Quaternion.AngleAxis(roll,Vector3.right);
        rightWheel.localRotation=rightRest*Quaternion.AngleAxis(roll,Vector3.right);
        motorAngle=Mathf.Repeat(motorAngle+travelled*2850f,360);
        flywheel.localRotation=Quaternion.AngleAxis(motorAngle,Vector3.right);
        var crank=Quaternion.AngleAxis(motorAngle,Vector3.right)*Vector3.back;
        float crankY=crank.y*.072f;
        rod.localPosition=rodRest+new Vector3(0,crankY,(crank.z+1)*.072f);
        rod.localRotation=Quaternion.Euler(Mathf.Asin(Mathf.Clamp(crankY/.365f,-1,1))*Mathf.Rad2Deg,0,0);
        if(!havePrints){leftPrint=leftContact;rightPrint=rightContact;havePrints=true;}
        if(moving){WriteTrack(ref leftPrint,leftContact);WriteTrack(ref rightPrint,rightContact);}
    }

    void UpdateMotor(float speed,float dt)
    {
        bool running=speed>.002f;
        motorGain=Mathf.MoveTowards(motorGain,Mathf.Clamp01(speed/.49f),dt*(running?2f:3f));
        motor.volume=motorGain*.41f*OsowiecEnvironmentAudio.EffectsVolume;
        motor.pitch=Mathf.Lerp(.73f,1.08f,motorGain);
        if(running&&!motor.isPlaying)motor.Play();
        if(!running && motorGain==0 && motor.isPlaying)motor.Stop();
    }

    static void WriteTrack(ref Vector3 previous,Vector3 current)
    {
        float sq=(current-previous).sqrMagnitude;
        if(sq<.19f*.19f)return;
        if(sq<1.45f)OsowiecWetTracks.Wheel(previous,current);
        previous=current;
    }
}
