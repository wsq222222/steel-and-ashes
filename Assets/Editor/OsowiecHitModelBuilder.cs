using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class OsowiecHitModelBuilder
{
    internal static void Attach(GameObject root, GameObject model, string role)
    {
        root.AddComponent<OsowiecHitBody>();
        var joints = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
        Transform head = joints["Head"];
        Vector3 up = root.transform.up;
        Vector3 forward = root.transform.forward;
        Capsule(joints["Chest"], "Chest", joints["Chest"].position + up * .09f,
            joints["Neck"].position - up * .075f, .167f, OsowiecHitArea.Torso);
        Capsule(joints["Pelvis"], "Abdomen", joints["Pelvis"].position + up * .025f,
            joints["Chest"].position + up * .035f, .147f, OsowiecHitArea.Torso);
        Capsule(joints["Neck"], "Collar", joints["Neck"].position,
            head.position - up * .04f, .066f, OsowiecHitArea.Torso);
        Capsule(head, "Head", head.position + up * .042f + forward * .008f,
            head.position + up * .133f + forward * .008f, .095f, OsowiecHitArea.Head);

        foreach (string side in new[] { "L", "R" })
        {
            Segment(joints[side+"Thigh"], joints[side+"Shin"], .084f);
            Segment(joints[side+"Shin"], joints[side+"Foot"], .063f);
            Segment(joints[side+"Arm"], joints[side+"Forearm"], .070f);
            Segment(joints[side+"Forearm"], joints[side+"Hand"], .052f);
            Transform hand = joints[side+"Hand"];
            Capsule(hand, "Glove", hand.position, hand.position + forward*.055f, .042f, OsowiecHitArea.Limb);
            Transform foot = joints[side+"Foot"];
            Capsule(foot, "Boot", foot.position - up*.065f,
                foot.position - up*.065f + forward*.12f, .058f, OsowiecHitArea.Limb);
        }

        var shell = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .SingleOrDefault(r => r.name.Contains("IssuedHelmet", StringComparison.Ordinal));
        if (shell) Shell(root.transform, head, shell, role);
        else if (role != "Support") throw new InvalidOperationException("Missing issued helmet on " + role);
    }

    static void Segment(Transform bone, Transform tip, float radius)
    {
        Vector3 direction = (tip.position - bone.position).normalized;
        Capsule(bone, bone.name, bone.position+direction*radius*.75f,
            tip.position-direction*radius*.75f, radius, OsowiecHitArea.Limb);
    }

    // Author shapes in metres and retain their bind-pose relationship to the animated joint.
    static GameObject Anchor(Transform joint, string name, Vector3 position, Quaternion rotation)
    {
        var obj = new GameObject(name);
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.transform.SetParent(joint, true);
        return obj;
    }

    static void Capsule(Transform bone, string name, Vector3 a, Vector3 b, float radius, OsowiecHitArea kind)
    {
        var obj = Anchor(bone, "Hit "+name, (a+b)*.5f, Quaternion.FromToRotation(Vector3.up, (b-a).normalized));
        var trigger = obj.AddComponent<CapsuleCollider>();
        trigger.isTrigger = true;
        trigger.radius = radius;
        trigger.height = Vector3.Distance(a,b) + 2*radius;
        obj.AddComponent<OsowiecHitPart>().area = kind;
    }

    static void Shell(Transform root, Transform head, SkinnedMeshRenderer skin, string role)
    {
        var obj = Anchor(head, "Issued steel helmet", head.position, root.rotation);
        var baked = new Mesh {name=role+" issued helmet"};
        skin.BakeMesh(baked, false);
        var points = baked.vertices;
        var normals = baked.normals;
        var tangents = baked.tangents;
        var basis = obj.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
        var normalBasis = basis.inverse.transpose;
        for (int i=0;i<points.Length;i++)
        {
            points[i] = basis.MultiplyPoint3x4(points[i]);
            normals[i] = normalBasis.MultiplyVector(normals[i]).normalized;
            if (i < tangents.Length)
            {
                var tangent = basis.MultiplyVector(tangents[i]).normalized;
                tangents[i] = new Vector4(tangent.x,tangent.y,tangent.z,tangents[i].w);
            }
        }
        baked.vertices = points; baked.normals = normals; baked.tangents = tangents;
        baked.RecalculateBounds();
        if (baked.bounds.size.magnitude < .3f || baked.bounds.size.magnitude > .7f ||
            baked.bounds.center.magnitude > .55f)
            throw new InvalidOperationException($"Invalid helmet dimensions on {role}: {baked.bounds}");
        string path = OsowiecFieldCast.Folder + "Game/"+role+"-Helmet.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh)
        {
            EditorUtility.CopySerialized(baked, mesh);
            EditorUtility.SetDirty(mesh);
            Object.DestroyImmediate(baked);
        }
        else
        {
            mesh=baked;
            AssetDatabase.CreateAsset(mesh,path);
        }
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
        // Retain the imported shell for baking the deployment silhouette. Tactical
        // and fog visibility switch Renderer.enabled on every part of a live unit.
        // The detached gameplay shell must be the only one they can show.
        skin.gameObject.SetActive(false);
        var broad = obj.AddComponent<BoxCollider>();
        broad.isTrigger = true;
        broad.center = mesh.bounds.center;
        broad.size = mesh.bounds.size + Vector3.one * .002f;
        var part = obj.AddComponent<OsowiecHitPart>();
        part.area = OsowiecHitArea.Helmet;
        part.helmetMesh = mesh;
    }
}
