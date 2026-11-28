using UnityEngine;

[DefaultExecutionOrder(210)]
public sealed class OsowiecStaffBody : MonoBehaviour
{
    public Animator animator;
    public Transform leftArm,leftElbow,leftHand,rightArm,rightElbow,rightHand;
    public Quaternion leftPalmRest=Quaternion.identity, rightPalmRest=Quaternion.identity;
    public Vector3 leftElbowRest,leftWristRest,rightElbowRest,rightWristRest;
    Unit wearer;
    Vector3 previous;
    float pace,phase;
    bool walking;
    void LateUpdate()
    {
        var manager=CameraManager.Instance;
        if(!manager || !manager.IsHubExploreMode()) return;
        Unit actor=manager.GetCurrentControlledUnit();
        if(!actor) return;
        var camera=manager.GetActionCamera();
        var eye=camera.transform;
        camera.fieldOfView=OsowiecFieldSettings.FieldOfView;
        if(actor!=wearer)
        {
            wearer=actor;previous=actor.transform.position;
            // This camera also draws the fitted coat within arm's reach.
            camera.nearClipPlane=.045f;
            foreach(var r in actor.GetComponentsInChildren<Renderer>()) r.forceRenderingOff=true;
        }
        var flat=Vector3.ProjectOnPlane(eye.forward,Vector3.up);
        if(flat.sqrMagnitude<.001f) flat=actor.transform.forward;
        var rotation=Quaternion.LookRotation(flat,Vector3.up);
        // Keep the eyes in front of the breast and belt. A fixed offset keeps
        // the planted boots still while the player looks up and down.
        Vector3 ground=actor.transform.position;
        float eyeHeight=Mathf.Clamp(eye.position.y-ground.y,1.3f,1.95f);
        float stature=eyeHeight/1.65f;
        float eyeLead=.27f*stature;
        transform.SetPositionAndRotation(new Vector3(eye.position.x,ground.y,eye.position.z)-rotation*Vector3.forward*eyeLead,rotation);
        transform.localScale=Vector3.one*stature;
        float dt=Time.deltaTime;
        if(dt>0)
        {
            float motion=Vector3.ProjectOnPlane(ground-previous,Vector3.up).magnitude/dt;
            pace=Mathf.MoveTowards(pace,Mathf.Clamp(motion,0,4),dt*10);
            phase+=dt*Mathf.Lerp(1.3f,7f,Mathf.Clamp01(pace/2f));
        }
        previous=ground;
        if(walking!=(pace>.16f))
        {
            walking=!walking; animator.CrossFadeInFixedTime(walking?"Base Layer.Walk":"Base Layer.Idle",.15f);
        }
        animator.speed=walking?Mathf.Clamp(pace/1.35f,.65f,1.65f):1;
        float stride=Mathf.Clamp01(pace/2)*Mathf.Sin(phase);
        // Generic locomotion tracks can carry translations. Keep the tailored sleeve and glove on their bind-length chain.
        leftElbow.localPosition=leftElbowRest;leftHand.localPosition=leftWristRest;
        rightElbow.localPosition=rightElbowRest;rightHand.localPosition=rightWristRest;
        RelaxArm(-1,leftArm,leftElbow,leftHand,leftPalmRest,stride);
        RelaxArm(1,rightArm,rightElbow,rightHand,rightPalmRest,stride);
    }

    void RelaxArm(int side,Transform arm,Transform elbow,Transform hand,Quaternion palmRest,float stride)
    {
        float swing=side*stride;
        Vector3 wrist=transform.TransformPoint(new Vector3(side*.24f,1.01f + .028f*Mathf.Abs(swing),.215f + swing*.09f));
        Vector3 elbowPole=transform.TransformPoint(new Vector3(side*.35f,1.16f,-.14f));
        Reach(arm,elbow,hand,wrist,elbowPole);
        hand.rotation=transform.rotation*Quaternion.Euler(66-swing*15,side*8,side*9)*palmRest;
    }

    static void Reach(Transform arm,Transform elbow,Transform hand,Vector3 target,Vector3 pole)
    {
        var axis=target-arm.position;
        float upper=Vector3.Distance(arm.position,elbow.position), lower=Vector3.Distance(elbow.position,hand.position);
        float distance=Mathf.Clamp(axis.magnitude,Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
        axis.Normalize();
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        var plane=Vector3.ProjectOnPlane(pole-arm.position,axis).normalized;
        var bend=axis*along+plane*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        arm.rotation=Quaternion.FromToRotation(elbow.position-arm.position,bend)*arm.rotation;
        elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,arm.position+axis*distance-elbow.position)*elbow.rotation;
    }
}
