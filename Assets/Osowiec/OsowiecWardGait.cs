using System;
using UnityEngine;

// The walk clip supplies the upper-body performance. These contacts adapt its legs to the actual path,
// including a backward or sideways carry, with a stationary supporting shoe on the uneven boards.
internal sealed class OsowiecWardGait
{
    readonly Transform wearer, pelvis;
    readonly bool onStairs;
    readonly Transform[,] legs = new Transform[2,3];
    readonly Vector3[] anchors = new Vector3[2];
    readonly Vector3[] targets = new Vector3[2];
    readonly float[] pitches = new float[2];
    readonly Quaternion[] level = new Quaternion[2];
    readonly float[] offset = new float[2];
    readonly RaycastHit[] surfaces = new RaycastHit[12];
    readonly Quaternion[,] original = new Quaternion[2,3];
    Vector3 oldPelvis, direction;
    float phase, previousPhase, cycle, scale;
    bool applied, travelling;
    int frame = -1;
    const float Support = .58f;
    internal int HeelStrike { get; private set; } = -1;
    internal Vector3 ContactPoint => anchors[HeelStrike]-Vector3.up*(.135f*scale);
    internal bool LeftShoe => offset[HeelStrike]<0;

    internal OsowiecWardGait(Transform actor,Animator body,bool onStairs=false)
    {
        this.onStairs=onStairs;
        wearer=actor;var bones=body.GetComponentsInChildren<Transform>(true);
        Transform Find(string name)=>Array.Find(bones,t=>t.name==name);
        pelvis=Find("Pelvis");
        scale=actor.lossyScale.y;
        for(int i=0;i<2;i++)
        {
            var side=i==0?"L":"R";
            legs[i,0]=Find(side+"Thigh"); legs[i,1]=Find(side+"Shin"); legs[i,2]=Find(side+"Foot");
            offset[i]=Mathf.Sign(Vector3.Dot(legs[i,2].position-actor.position,actor.right))*.119f*scale;
            level[i]=Quaternion.Inverse(actor.rotation)*legs[i,2].rotation;
        }
    }

    internal void Stop() { travelling=false;frame=-1; }

    internal void Reset()
    {
        HeelStrike=-1;
        if(applied)
        {
            pelvis.localPosition=oldPelvis;
            for(int side=0;side<2;side++)for(int i=0;i<3;i++)legs[side,i].localRotation=original[side,i];
            applied=false;
        }
        if(frame<Time.frameCount-1)travelling=false;
    }

    internal void Advance(Vector3 delta,float dt,bool carry,Animator body)
    {
        delta.y=0;
        float distance=delta.magnitude;
        if(dt<=0 || distance<.00001f || distance>1)return;
        direction=delta/distance;cycle=onStairs ? .73f*scale : (carry?.72f:.94f)*scale;
        if(!travelling)
        {
            phase=0;
            for(int i=0;i<2;i++) anchors[i]=Ground(legs[i,2].position);
        }
        previousPhase=phase;
        phase=Mathf.Repeat(phase+distance/cycle,1);
        travelling=true;frame=Time.frameCount;
        body.speed=Mathf.Clamp((carry?22f:20f)/24f*distance/(dt*cycle),.12f,2.4f);
    }

    Vector3 Ground(Vector3 p)
    {
        if(onStairs)
        {
            p=OsowiecGalleryStairs.SeatBoot(p);
            int count=Physics.RaycastNonAlloc(new Vector3(p.x,wearer.position.y+.60f,p.z),Vector3.down,surfaces,1.24f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            float best=-1e5f;
            for(int i=0;i<count;i++)
            {
                var hit=surfaces[i];
                if(hit.normal.y<.73f || hit.point.y<=best || hit.collider.GetComponentInParent<Unit>())continue;
                best=hit.point.y;
            }
            p.y=(best>-1e4f ? best : wearer.position.y)+.15f*scale;
            return p;
        }
        float baseY=OsowiecWesternWard.DeckHeight(p),floor=baseY-.075f;
        int n=Physics.RaycastNonAlloc(new Vector3(p.x,baseY+.24f,p.z),Vector3.down,surfaces,.34f,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        bool found=false;
        for(int i=0;i<n;i++)
        {
            var hit=surfaces[i];
            if(hit.normal.y<.78f || hit.point.y>baseY+.13f || hit.collider.GetComponentInParent<Animator>() ||
                hit.collider.transform.IsChildOf(wearer))continue;
            found=true;floor=Mathf.Max(floor,hit.point.y);
        }
        p.y=(found?floor:baseY)+.135f*scale;
        return p;
    }

    internal void Apply()
    {
        if(frame!=Time.frameCount || !travelling)return;
        oldPelvis=pelvis.localPosition;
        for(int side=0;side<2;side++)for(int i=0;i<3;i++)original[side,i]=legs[side,i].localRotation;
        applied=true;
        // The mass follows the supporting side, drops after heel strike and rises at mid-stance.
        float weight=Mathf.Sin(phase*2*Mathf.PI);
        pelvis.position+=wearer.right*(offset[0]*weight*.19f)+Vector3.down*((.016f+.008f*Mathf.Cos(phase*4*Mathf.PI))*scale);
        float lowerHips=0;
        for(int side=0;side<2;side++)
        {
            float footPhase=Mathf.Repeat(phase+side*.5f,1);
            float last=Mathf.Repeat(previousPhase+side*.5f,1);
            Vector3 lane=wearer.position+wearer.right*offset[side];
            if(footPhase<last)
            {
                anchors[side]=Ground(lane+direction*(cycle*Support*.5f));
                HeelStrike=side;
            }
            var target=anchors[side];
            float swing=Mathf.Clamp01((footPhase-Support)/(1-Support));
            if(swing>0)
            {
                var landing=Ground(lane+direction*(cycle*(1-footPhase+Support*.5f)));
                target=Vector3.Lerp(anchors[side],landing,Mathf.SmoothStep(0,1,swing));
                float lift=.093f*scale;
                if(onStairs) lift+=.065f*scale+Mathf.Abs(landing.y-anchors[side].y)*.27f;
                target.y+=Mathf.Sin(Mathf.PI*swing)*lift;
            }
            targets[side]=target;
            pitches[side]=10f*Mathf.Sin(2*Mathf.PI*swing);
            var hip=legs[side,0].position;
            float upper=Vector3.Distance(hip,legs[side,1].position);
            float lower=Vector3.Distance(legs[side,1].position,legs[side,2].position);
            float horizontal=Vector3.ProjectOnPlane(target-hip,Vector3.up).sqrMagnitude;
            // Retain a little knee flex as the leading shoe lands, including on the ramp.
            float spanSquared=upper*upper+lower*lower+2*upper*lower*.9816f;
            float highest=target.y+Mathf.Sqrt(Mathf.Max(0,spanSquared-horizontal));
            lowerHips=Mathf.Max(lowerHips,hip.y-highest);
        }
        pelvis.position-=Vector3.up*Mathf.Min(lowerHips,.095f*scale);
        for(int side=0;side<2;side++)
        {
            OsowiecWardCare.Fit(legs,side,targets[side],wearer.forward+wearer.right*(Mathf.Sign(offset[side])*.075f));
            legs[side,2].rotation=Quaternion.AngleAxis(pitches[side],wearer.right)*wearer.rotation*level[side];
        }
    }
}
