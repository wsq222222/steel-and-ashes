using System;
using UnityEngine;

// These three ward staff share the authored walk/carry clips. While their hands are
// free, the bearers tend their gloves. The water carrier keeps one hand on the bail.
internal sealed class OsowiecWardAttendantPose
{
    readonly Transform actor, pelvis, chest, head;
    readonly Transform[,] arms = new Transform[2,3], legs = new Transform[2,3];
    readonly float[] sides = new float[2];
    readonly Vector3[] originalFeet = new Vector3[2];
    readonly Quaternion[] footDirections = new Quaternion[2];
    readonly Transform[] upper, lower;
    readonly Quaternion[] upperRest, lowerRest;
    readonly Vector3 gripInWrist;
    Vector3 originalPelvis;
    bool applied, planted;

    internal OsowiecWardAttendantPose(Transform actor, Animator animator)
    {
        this.actor=actor;
        var bones=animator.GetComponentsInChildren<Transform>(true);
        Transform Bone(string name)=>Array.Find(bones,t=>t.name==name);
        pelvis=Bone("Pelvis");chest=Bone("Chest");head=Bone("Head");
        for(int i=0;i<2;i++)
        {
            string side=i==0?"L":"R";
            arms[i,0]=Bone(side+"Arm");arms[i,1]=Bone(side+"Forearm");arms[i,2]=Bone(side+"Hand");
            legs[i,0]=Bone(side+"Thigh");legs[i,1]=Bone(side+"Shin");legs[i,2]=Bone(side+"Foot");
            sides[i]=Mathf.Sign(Vector3.Dot(arms[i,0].position-chest.position,actor.right));
        }
        upper=new[]{chest,head,arms[0,0],arms[0,1],arms[0,2],arms[1,0],arms[1,1],arms[1,2]};
        lower=new[]{pelvis,legs[0,0],legs[0,1],legs[0,2],legs[1,0],legs[1,1],legs[1,2]};
        upperRest=new Quaternion[upper.Length];lowerRest=new Quaternion[lower.Length];
        gripInWrist=arms[1,2].InverseTransformVector((actor.forward*.050f-Vector3.up*.010f)*actor.lossyScale.y);
    }

    internal void Reset()
    {
        if(!applied)return;
        for(int i=0;i<upper.Length;i++)upper[i].localRotation=upperRest[i];
        if(planted)
        {
            pelvis.localPosition=originalPelvis;
            for(int i=0;i<lower.Length;i++)lower[i].localRotation=lowerRest[i];
        }
        applied=false;
    }

    void Save(bool fixFeet)
    {
        applied=true;planted=fixFeet;
        for(int i=0;i<upper.Length;i++)upperRest[i]=upper[i].localRotation;
        if(!fixFeet)return;
        originalPelvis=pelvis.localPosition;
        for(int i=0;i<lower.Length;i++)lowerRest[i]=lower[i].localRotation;
        for(int side=0;side<2;side++)
        {
            originalFeet[side]=legs[side,2].position;
            footDirections[side]=legs[side,2].rotation;
        }
    }

    void ShiftStance(float down,float weight,float lean)
    {
        float scale=actor.lossyScale.y;
        pelvis.position+=(actor.right*weight-Vector3.up*down)*scale;
        chest.rotation=Quaternion.AngleAxis(lean,actor.right)*chest.rotation;
        for(int side=0;side<2;side++)
        {
            OsowiecWardCare.Fit(legs,side,originalFeet[side],actor.forward+actor.right*sides[side]*.1f);
            legs[side,2].rotation=footDirections[side];
        }
    }

    void Reach(int side,Vector3 target,float strength)
    {
        var hand=arms[side,2];
        OsowiecWardCare.Fit(arms,side,Vector3.Lerp(hand.position,target,strength),
            actor.right*sides[side]-actor.forward*.42f);
    }

    internal void WhileWaiting(int bearer,float time)
    {
        Save(true);
        float phase=Mathf.Repeat(time+(bearer==2?2.2f:9f),bearer==2?9.7f:13.1f);
        float gesture=Mathf.SmoothStep(0,1,Mathf.Clamp01(phase-.4f))*
            (1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-4.3f)/1.3f)));
        float weight=Mathf.Sin(time*.92f+bearer)*.024f;
        ShiftStance(.028f+.021f*gesture,weight,3.5f+6f*gesture);
        head.rotation=Quaternion.AngleAxis(13f*gesture,actor.right)*
            Quaternion.AngleAxis(8f*Mathf.Sin(time*.57f+bearer)*(1-gesture),Vector3.up)*head.rotation;
        // The front bearer rubs cold palms. The rear one supports an arm and works along its cuff.
        for(int side=0;side<2;side++)
        {
            float s=sides[side];
            Vector3 local=bearer==2
                ? new Vector3(s*.037f,1.085f+s*.023f*Mathf.Sin(phase*7.2f),.245f+s*.014f*Mathf.Cos(phase*7.2f))
                : new Vector3(sides[0]*.075f+s*.032f,1.135f+(side==0?.027f:0),
                              .19f+(side==0?.077f:.016f*Mathf.Cos(phase*4.5f)));
            Reach(side,actor.TransformPoint(local),gesture);
        }
    }

    internal void WithPail(Transform pail,float time,float dip,bool atBarrel)
    {
        Save(dip>.001f);
        if(planted)ShiftStance(.065f*dip,.016f*dip,8f*dip);
        float swing=Mathf.Sin(time*4.6f);
        float side=sides[1];
        // Keep the load beside the thigh while moving. At each station the hand
        // reaches forward with a bent elbow to lower the pail or tilt it for pouring.
        Vector3 carrying=new(side*.315f,.83f+.008f*swing,.045f+.026f*swing);
        Vector3 working=atBarrel ? new Vector3(side*.17f,.74f,.385f) : new Vector3(side*.19f,1.105f,.29f);
        Reach(1,actor.TransformPoint(Vector3.Lerp(carrying,working,dip)),1);
        if(dip>.001f)
            Reach(0,actor.TransformPoint(new Vector3(-side*.055f,.86f+.20f*(atBarrel?0:1),.28f)),dip*.63f);
        var hand=arms[1,2];
        Vector3 palm=hand.TransformPoint(gripInWrist);
        Quaternion orientation=actor.rotation*Quaternion.Euler(atBarrel?dip*8f:-dip*37f,0,swing*(1-dip)*3f);
        // The centre of the modelled handle crown sits 9 mm below the pail pivot.
        pail.SetPositionAndRotation(palm-orientation*(Vector3.down*.009f),orientation);
    }
}

