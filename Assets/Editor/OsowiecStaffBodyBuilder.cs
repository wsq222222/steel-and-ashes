using System.Linq;
using UnityEngine;
using UnityEditor;

public static class OsowiecStaffBodyBuilder
{
    public static void Install()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(OsowiecFieldCast.Folder+"StaffCoat.fbx");
        if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation || !importer.isReadable)
        {
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.importAnimation=true;
            importer.isReadable=true;
            importer.SaveAndReimport();
        }
        var rig=new GameObject("Дежурный по штабу").AddComponent<OsowiecStaffBody>();
        var model=OsowiecFieldCast.Visual("StaffCoat",rig.transform);
        var joints=model.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        Vector3 right=Vector3.ProjectOnPlane(joints["RThigh"].position-joints["LThigh"].position,Vector3.up);
        Vector3 forward=Vector3.Cross(right,Vector3.up);
        if(Vector3.Dot(forward,joints["LHand"].position-joints["Pelvis"].position)<0) forward=-forward;
        model.transform.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(forward,Vector3.forward,Vector3.up),Vector3.up)*model.transform.rotation;
        rig.animator=model.GetComponent<Animator>();
        if(!rig.animator) rig.animator=model.AddComponent<Animator>();
        rig.animator.runtimeAnimatorController=OsowiecFieldCast.Controller("StaffCoat");
        rig.animator.applyRootMotion=false;
        rig.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        bool lOnLeft=rig.transform.InverseTransformPoint(joints["LArm"].position).x < rig.transform.InverseTransformPoint(joints["RArm"].position).x;
        string left=lOnLeft?"L":"R", rightSide=lOnLeft?"R":"L";
        rig.leftArm=joints[left+"Arm"]; rig.leftElbow=joints[left+"Forearm"]; rig.leftHand=joints[left+"Hand"];
        rig.rightArm=joints[rightSide+"Arm"]; rig.rightElbow=joints[rightSide+"Forearm"]; rig.rightHand=joints[rightSide+"Hand"];
        var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.bones.Contains(rig.leftHand)&&s.bones.Contains(rig.rightHand));
        skin.updateWhenOffscreen=true;
        rig.leftPalmRest=Quaternion.Inverse(rig.transform.rotation)*rig.leftHand.rotation;
        rig.rightPalmRest=Quaternion.Inverse(rig.transform.rotation)*rig.rightHand.rotation;
        rig.leftElbowRest=rig.leftElbow.localPosition; rig.leftWristRest=rig.leftHand.localPosition;
        rig.rightElbowRest=rig.rightElbow.localPosition; rig.rightWristRest=rig.rightHand.localPosition;
    }
}
