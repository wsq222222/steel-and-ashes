using System;
using UnityEngine;

// Work against the actual blanket triangles after the generic rig animator has posed the limbs.
[DefaultExecutionOrder(125)]
public sealed class OsowiecWardCare : MonoBehaviour
{
    public OsowiecWesternWard ward;
    public MeshCollider[] residents;
    public float PalmGap { get; private set; }
    public int Contacts { get; private set; }
    Transform pelvis,chest;
    readonly OsowiecWardAttendantPose[] staff=new OsowiecWardAttendantPose[3];
    readonly Transform[,] arms=new Transform[2,3],legs=new Transform[2,3];
    readonly Vector3[] plantedFeet=new Vector3[2];
    readonly Quaternion[] footRotations=new Quaternion[2];
    Transform[] adjusted;
    Quaternion[] localRotations;
    Vector3 restPelvis;
    bool applied;

    void Start()
    {
        ward.bodies[0].cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var skin in ward.bodies[0].GetComponentsInChildren<SkinnedMeshRenderer>())skin.updateWhenOffscreen=true;
        var bones=ward.bodies[0].GetComponentsInChildren<Transform>(true);
        Transform Bone(string label)=>Array.Find(bones,b=>b.name==label);
        pelvis=Bone("Pelvis");chest=Bone("Chest");
        for(int i=0;i<2;i++)
        {
            string side=i==0?"L":"R";
            arms[i,0]=Bone(side+"Arm");arms[i,1]=Bone(side+"Forearm");arms[i,2]=Bone(side+"Hand");
            legs[i,0]=Bone(side+"Thigh");legs[i,1]=Bone(side+"Shin");legs[i,2]=Bone(side+"Foot");
        }
        for(int i=0;i<staff.Length;i++)
            staff[i]=new OsowiecWardAttendantPose(ward.people[i+1],ward.bodies[i+1]);
        adjusted=new[]{pelvis,chest,arms[0,0],arms[0,1],arms[0,2],arms[1,0],arms[1,1],arms[1,2],
            legs[0,0],legs[0,1],legs[0,2],legs[1,0],legs[1,1],legs[1,2]};
        localRotations=new Quaternion[adjusted.Length];
    }

    public float GloveToBlanket()
    {
        if(ward.ResidentAtHand<0)return float.PositiveInfinity;
        var body=ward.bodies[0].GetComponentInChildren<SkinnedMeshRenderer>();
        var surface=residents[ward.ResidentAtHand];
        var mesh=new Mesh();
        try
        {
            // BakeMesh follows the same weighted vertices as the player's visible gloves.
            body.BakeMesh(mesh);
            var points=mesh.vertices;
            var mats=body.sharedMaterials;
            int leather=Array.FindIndex(mats,m=>m && m.name=="Leather");
            if(leather<0)return float.PositiveInfinity;
            var gloveTriangles=mesh.GetTriangles(leather);
            float worst=0;
            for(int side=0;side<2;side++)
            {
                float nearest=float.PositiveInfinity;
                foreach(int i in gloveTriangles)
                {
                    Vector3 p=body.transform.TransformPoint(points[i]);
                    if((p-arms[side,2].position).sqrMagnitude>.12f*.12f)continue;
                    if(surface.Raycast(new Ray(p+Vector3.up*.5f,Vector3.down),out var hit,1f))
                        nearest=Mathf.Min(nearest,Mathf.Abs(p.y-hit.point.y));
                }
                worst=Mathf.Max(worst,nearest);
            }
            return worst;
        }
        finally{Destroy(mesh);}
    }

    void Update()
    {
        if(GameManager.Instance && GameManager.Instance.IsPaused())return;
        foreach(var person in staff)person.Reset();
        if(!applied)return;
        pelvis.localPosition=restPelvis;
        for(int i=0;i<adjusted.Length;i++)adjusted[i].localRotation=localRotations[i];
        applied=false;
    }

    void LateUpdate()
    {
        if(!ward.bodies[0].enabled || !pelvis || ward.bodies[0].speed==0)return;
        staff[0].WithPail(ward.waterPail,ward.WorkTime,ward.WaterDip,ward.AtBarrel);
        if(ward.WaitingForLitter)
            for(int i=1;i<staff.Length;i++)staff[i].WhileWaiting(i+1,ward.WorkTime);
        int index=ward.ResidentAtHand;
        if(index<0 || index>=residents.Length)return;
        var doctor=ward.people[0];
        var patient=residents[index];
        if(!patient || !patient.gameObject.activeInHierarchy || Vector3.Dot(doctor.forward,Vector3.right)<.96f)return;
        restPelvis=pelvis.localPosition;
        for(int i=0;i<adjusted.Length;i++)localRotations[i]=adjusted[i].localRotation;
        applied=true;
        for(int side=0;side<2;side++)
        {
            plantedFeet[side]=legs[side,2].position;
            footRotations[side]=legs[side,2].rotation;
        }
        // Bend from the knees and hips while keeping both soles planted on the floor boards.
        pelvis.position+=Vector3.down*.23f;
        float cycle=Time.time*1.55f;
        chest.rotation=Quaternion.AngleAxis(26+1.8f*Mathf.Cos(cycle),doctor.right)*chest.rotation;
        PalmGap=0;
        for(int side=0;side<2;side++)
        {
            Fit(legs,side,plantedFeet[side],doctor.forward);
            legs[side,2].rotation=footRotations[side];
            // One hand steadies the blanket while the other makes a short dressing stroke.
            // Both probes follow its actual surface as this patient and posture change.
            float phase=cycle+side*Mathf.PI;
            float active=Mathf.SmoothStep(0,1,(1+Mathf.Sin(phase))*.5f);
            float reach=.006f*Mathf.Sin(phase)+active*.036f;
            float x=-52.32f+index*1.82f;
            float outside=Mathf.Sign(Vector3.Dot(arms[side,0].position-chest.position,doctor.right));
            var probe=new Vector3(x-.18f+reach,1f,-8.95f)+
                doctor.right*(outside*(.13f+.024f*active));
            if(!patient.Raycast(new Ray(probe,Vector3.down),out var cloth,2f))continue;
            var target=cloth.point-doctor.forward*.027f+cloth.normal*.034f;
            Fit(arms,side,target,doctor.right*outside-doctor.forward*.25f);
            PalmGap=Mathf.Max(PalmGap,Vector3.Distance(target,arms[side,2].position));
            Contacts++;
        }
    }

    internal static void Fit(Transform[,] chain,int side,Vector3 target,Vector3 bend)
    {
        var root=chain[side,0];var elbow=chain[side,1];var tip=chain[side,2];
        Vector3 origin=root.position;
        float upper=Vector3.Distance(origin,elbow.position),lower=Vector3.Distance(elbow.position,tip.position);
        Vector3 dir=(target-origin).normalized;
        float span=Mathf.Clamp(Vector3.Distance(origin,target),Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
        float along=(upper*upper-lower*lower+span*span)/(2f*span);
        Vector3 hinge=Vector3.ProjectOnPlane(bend,dir).normalized;
        var joint=origin+along*dir+hinge*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        var rotation=tip.rotation;
        root.rotation=Quaternion.FromToRotation(elbow.position-origin,joint-origin)*root.rotation;
        elbow.rotation=Quaternion.FromToRotation(tip.position-elbow.position,target-elbow.position)*elbow.rotation;
        tip.rotation=rotation;
    }
}

