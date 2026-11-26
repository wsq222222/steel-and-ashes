using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

internal static class OsowiecWardWalkCheck
{
    internal static IEnumerator Run(string output)
    {
        Time.captureFramerate=30;
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<7;i++)yield return null;
        var care=UnityEngine.Object.FindFirstObjectByType<OsowiecWardCare>();
        var ward=care.ward;
        var old=Camera.main;
        if(old)old.tag="Untagged";
        var host=new GameObject("Ward observation camera"){tag="MainCamera"};
        var camera=host.AddComponent<Camera>();
        camera.depth=-50; camera.fieldOfView=46;camera.nearClipPlane=.035f;
        camera.cullingMask=~(1<<5);camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var rt=RenderTexture.GetTemporary(848,480,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        var saved=RenderTexture.active;
        void Aim(Vector3 from,Vector3 at) => host.transform.SetPositionAndRotation(from,Quaternion.LookRotation(at-from));
        void Frame(string name)
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".jpg"),image.EncodeToJPG(91));
            camera.targetTexture=null;RenderTexture.active=saved;
        }
        var staff=ward.people[1];var bones=ward.bodies[1].GetComponentsInChildren<Transform>(true);
        Transform Joint(string name)=>bones.First(b=>b.name==name);
        var lThigh=Joint("LThigh");var lShin=Joint("LShin");var lFoot=Joint("LFoot");
        var rThigh=Joint("RThigh");var rShin=Joint("RShin");var rFoot=Joint("RFoot");
        var lArm=Joint("LArm");var lForearm=Joint("LForearm");var lHand=Joint("LHand");
        float Flex(Transform a,Transform b,Transform c)=>180f-Vector3.Angle(a.position-b.position,c.position-b.position);
        var pelvis=Joint("Pelvis");
        var still=new int[2];
        var longest=new int[2];
        var minKnee=new[]{180f,180f};var maxKnee=new float[2];
        float lowElbow=180,highElbow=0,weightMin=float.MaxValue,weightMax=float.MinValue;
        try
        {
            ward.ResetWard();
            var rows=new System.Text.StringBuilder("frame,x,y,z,leftKnee,rightKnee,leftElbow,leftFootX,leftFootY,leftFootZ,rightFootX,rightFootY,rightFootZ,pelvisX,pelvisY,pelvisZ\n");
            var end=new WaitForEndOfFrame();
            Aim(staff.position+new Vector3(-2.65f,1.18f,-.80f),staff.position+Vector3.up*.88f);
            var previous=staff.position;
            var lastSole=new[]{lFoot.position,rFoot.position};
            int count=0,watch=0;
            while(count<63 && watch++<500)
            {
                yield return end;
                var p=staff.position;
                float moved=Vector3.ProjectOnPlane(p-previous,Vector3.up).magnitude;previous=p;
                if(count==0 && moved<.012f)continue;
                Aim(p+new Vector3(-2.65f,1.13f,-.73f),p+Vector3.up*.85f);
                if(count%2==0)Frame("walk-"+count.ToString("00"));
                var a=lFoot.position;var b=rFoot.position;var hip=pelvis.position;
                float lk=Flex(lThigh,lShin,lFoot),rk=Flex(rThigh,rShin,rFoot),elbow=Flex(lArm,lForearm,lHand);
                float lateral=Vector3.Dot(hip-p,staff.right);
                weightMin=Mathf.Min(weightMin,lateral);weightMax=Mathf.Max(weightMax,lateral);
                lowElbow=Mathf.Min(lowElbow,elbow);highElbow=Mathf.Max(highElbow,elbow);
                for(int side=0;side<2;side++)
                {
                    var foot=side==0?a:b;float knee=side==0?lk:rk;
                    float drift=Vector3.ProjectOnPlane(foot-lastSole[side],Vector3.up).magnitude;
                    still[side]=count>0 && moved>.012f && drift<.0015f?still[side]+1:0;
                    longest[side]=Mathf.Max(longest[side],still[side]);lastSole[side]=foot;
                    minKnee[side]=Mathf.Min(minKnee[side],knee);maxKnee[side]=Mathf.Max(maxKnee[side],knee);
                }
                var values=new[]{p.x,p.y,p.z,lk,rk,elbow,
                    a.x,a.y,a.z,b.x,b.y,b.z,hip.x,hip.y,hip.z};
                rows.Append(count).Append(',').AppendLine(string.Join(",",values.Select(v=>v.ToString("F5",CultureInfo.InvariantCulture))));
                count++;
            }
            if(count!=63)throw new InvalidOperationException("No water attendant walk cycle.");
            File.WriteAllText(Path.Combine(output,"Motion.csv"),rows.ToString());
            var visited=new bool[2];var gaps=new float[2];var wrists=new float[2];
            Aim(new Vector3(-51.93f,1.08f,-12.08f),new Vector3(-51.7f,.10f,-8.64f));
            for(int i=0;i<1000 && !visited.All(x=>x);i++)
            {
                yield return end;
                int person=ward.ResidentAtHand;
                if(person<0 || visited[person] || care.PalmGap>.055f || care.Contacts<12)continue;
                float glove=care.GloveToBlanket();
                if(glove>.045f)continue;
                var from=new Vector3(-51.0f+person*1.82f,.80f,-10.76f);
                var to=new Vector3(-52.37f+person*1.82f,.17f,-8.90f);
                Aim(from,to); Frame("care-"+(person+1));
                visited[person]=true;gaps[person]=glove;wrists[person]=care.PalmGap;
            }
            if(!visited.All(x=>x))throw new InvalidOperationException("Failed to contact both actual patients.");
            // At 70 m the previous distance-only gate disabled every staff animator,
            // even when the tactical camera was looking directly at the ward.
            ward.ResetWard();
            Aim(new Vector3(-48.6f,70,-12.9f),new Vector3(-48.6f,0,-11.8f));
            var fronts=new Transform[2];var starts=new Vector3[2];
            var travel=new float[2];var kneeLow=new[]{180f,180f};var kneeHigh=new float[2];
            var idleKnees=new Transform[2,3];
            string[] kneeJoints={"LThigh","LShin","LFoot"};
            for(int person=0;person<2;person++)
            {
                var joints=ward.bodies[person+2].GetComponentsInChildren<Transform>(true);
                fronts[person]=joints.First(t=>t.name=="RHand");
                for(int joint=0;joint<3;joint++)
                    idleKnees[person,joint]=joints.First(t=>t.name==kneeJoints[joint]);
            }
            var bucketBody=ward.bodies[1].GetComponentInChildren<SkinnedMeshRenderer>();
            int gloveMaterial=Array.FindIndex(bucketBody.sharedMaterials,m=>m && m.name=="Leather");
            if(gloveMaterial<0)throw new InvalidOperationException("No leather glove in ward attendant skin.");
            var baked=new Mesh();float worstHandleGap=0;
            try
            {
                for(int i=0;i<210;i++)
                {
                    yield return end;
                    if(!ward.bodies.All(a=>a.enabled))
                        throw new InvalidOperationException("Orderlies stopped animating in the overhead view.");
                    for(int person=0;person<2;person++)
                    {
                        var owner=ward.people[person+2];var hand=owner.InverseTransformPoint(fronts[person].position);
                        if(i==0)starts[person]=hand;
                        travel[person]=Mathf.Max(travel[person],Vector3.Distance(starts[person],hand));
                        float bend=Flex(idleKnees[person,0],idleKnees[person,1],idleKnees[person,2]);
                        kneeLow[person]=Mathf.Min(kneeLow[person],bend);kneeHigh[person]=Mathf.Max(kneeHigh[person],bend);
                    }
                    if(i%24!=0)continue;
                    bucketBody.BakeMesh(baked);
                    var vertices=baked.vertices;
                    Vector3 grip=ward.waterPail.TransformPoint(Vector3.down*.009f);
                    float gap=baked.GetTriangles(gloveMaterial).Min(k=>
                        Vector3.Distance(grip,bucketBody.transform.TransformPoint(vertices[k])));
                    worstHandleGap=Mathf.Max(worstHandleGap,gap);
                    if(i==120)Frame("ward-overhead");
                }
            }
            finally{UnityEngine.Object.Destroy(baked);}
            if(travel.Any(d=>d<.095f) || worstHandleGap>.055f ||
                Enumerable.Range(0,2).Any(i=>kneeHigh[i]-kneeLow[i]<3))
                throw new InvalidOperationException($"Waiting staff/pail checks: {travel[0]:F3}, {travel[1]:F3}, grip {worstHandleGap:F3}, knee ranges {kneeHigh[0]-kneeLow[0]:F1}/{kneeHigh[1]-kneeLow[1]:F1}.");
            string measurements=FormattableString.Invariant(
                $"Windows player {Application.buildGUID}\n63 moving samples at 30 Hz; 32 game frames.\nLongest planted intervals at under 1.5 mm per frame: L={longest[0]}, R={longest[1]}.\nKnee flexion L={minKnee[0]:F1}..{maxKnee[0]:F1}, R={minKnee[1]:F1}..{maxKnee[1]:F1} degrees. Elbow={lowElbow:F1}..{highElbow:F1} degrees. Pelvis lateral range={weightMax-weightMin:F3} m.\nBed 1: wrist {wrists[0]:F4} m, glove to surface {gaps[0]:F4} m.\nBed 2: wrist {wrists[1]:F4} m, glove to surface {gaps[1]:F4} m.\n");
            bool passed=longest.All(n=>n>=5) && maxKnee[0]-minKnee[0]>20 && maxKnee[1]-minKnee[1]>20 &&
                highElbow-lowElbow>6 && weightMax-weightMin>.012f;
            measurements+=FormattableString.Invariant($"Overhead 70 m: four active staff. Waiting wrist travel {travel[0]:F3}/{travel[1]:F3} m, knee flexion ranges {kneeHigh[0]-kneeLow[0]:F1}/{kneeHigh[1]-kneeLow[1]:F1} degrees. Handle-to-glove maximum gap {worstHandleGap:F3} m.\n");
            File.WriteAllText(Path.Combine(output,"Ward.txt"),measurements+(passed?"PASS":"FAIL"));
            if(!passed)throw new InvalidOperationException("Ward gait measurements failed: "+measurements);
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=saved;RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(image);host.tag="Untagged";UnityEngine.Object.Destroy(host);
            if(old)old.tag="MainCamera";
        }
    }
}
