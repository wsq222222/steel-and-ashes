using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Fits the mission's original cloth pieces to the project's animated unit rigs.</summary>
public static class OsowiecUniforms
{
    private const string Art = "Assets/Osowiec/Art";
    private const string Generated = Art + "/UniformMaterials";

    public static void Apply(GameObject unitInstance, bool enemy)
    {
        if (unitInstance == null)
            throw new ArgumentNullException(nameof(unitInstance));

        if (!AssetDatabase.IsValidFolder(Generated))
            AssetDatabase.CreateFolder(Art, "UniformMaterials");

        Transform unit = unitInstance.transform;
        Transform[] nodes = unitInstance.GetComponentsInChildren<Transform>(true);
        Animator rig = unitInstance.GetComponentsInChildren<Animator>(true)
            .FirstOrDefault(candidate => candidate.avatar != null && candidate.avatar.isHuman && candidate.avatar.isValid);
        Transform head = HumanBone(rig, HumanBodyBones.Head) ??
            NamedBone(nodes, "defspine006", "head", "biphead", "mixamorighead", "neck01") ?? unit;
        Transform torso = HumanBone(rig, HumanBodyBones.Chest) ??
            NamedBone(nodes, "defspine003", "spine03", "chest", "spine02", "spine") ?? unit;

        Transform cameraPoint = nodes.FirstOrDefault(candidate => candidate.name == "CameraPoint");
        float eyeHeight = cameraPoint != null
            ? Mathf.Clamp(cameraPoint.position.y - unit.position.y, 1.23f, 2.05f)
            : 1.70f;
        float scale = Mathf.Clamp(eyeHeight / 1.70f, .83f, 1.19f);
        Place(unit, head, "OsowiecHeadgear", "Osowiec field helmet", eyeHeight + .045f * scale, scale, enemy);
        Place(unit, torso, "OsowiecGreatcoat", "Osowiec field greatcoat", eyeHeight - .755f * scale, scale, enemy);
    }

    private static Transform HumanBone(Animator rig, HumanBodyBones bone)
    {
        if (rig == null)
            return null;
        try
        {
            return rig.GetBoneTransform(bone);
        }
        catch (InvalidOperationException)
        {
            // Editor-only prefab instances may not have initialised the avatar yet.
            return null;
        }
    }

    private static Transform NamedBone(Transform[] transforms, params string[] names)
    {
        foreach (string name in names)
        {
            Transform match = transforms.FirstOrDefault(t => Normalize(t.name) == name);
            if (match != null)
                return match;
        }
        return null;
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static void Place(Transform unit, Transform anchor, string filename, string objectName,
        float height, float scale, bool enemy)
    {
        var clothing = unit.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name == objectName);
        if (clothing == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Art}/{filename}.fbx");
            if (model == null)
                throw new InvalidOperationException($"{filename}.fbx did not import.");
            clothing = ((GameObject)PrefabUtility.InstantiatePrefab(model)).transform;
            clothing.name = objectName;
            clothing.SetParent(anchor, false);
            Transform facing = clothing.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == "OsowiecFront");
            var frontLocalPoint = facing == null ? Vector3.forward : clothing.InverseTransformPoint(facing.position);
            frontLocalPoint.y = 0;
            Vector3 meshForward = frontLocalPoint.sqrMagnitude > .001f ? frontLocalPoint.normalized : Vector3.forward;
            Vector3 forward = Vector3.ProjectOnPlane(unit.forward, Vector3.up).normalized;
            Quaternion desired = Quaternion.LookRotation(forward, Vector3.up) *
                                 Quaternion.Inverse(Quaternion.LookRotation(meshForward, Vector3.up));

            Vector3 horizontal = Vector3.ProjectOnPlane(anchor.position - unit.position, Vector3.up);
            if (horizontal.sqrMagnitude > .28f * .28f)
                horizontal = Vector3.zero;
            clothing.SetPositionAndRotation(unit.position + horizontal + Vector3.up * height, desired);
            Vector3 basis = clothing.lossyScale;
            clothing.localScale = Vector3.Scale(clothing.localScale, new Vector3(
                scale / Mathf.Max(.001f, Mathf.Abs(basis.x)),
                scale / Mathf.Max(.001f, Mathf.Abs(basis.y)),
                scale / Mathf.Max(.001f, Mathf.Abs(basis.z))));
        }
        foreach (Renderer renderer in clothing.GetComponentsInChildren<Renderer>(true))
        {
            renderer.gameObject.layer = anchor.gameObject.layer;
            Material[] current = renderer.sharedMaterials;
            for (int i = 0; i < current.Length; i++)
                current[i] = UniformMaterial(current[i] == null ? "Cloth" : current[i].name, enemy);
            renderer.sharedMaterials = current;
        }
    }

    private static Material UniformMaterial(string importedName, bool enemy)
    {
        string part;
        Color32 color;
        float smoothness, metallic = 0;
        if (importedName.IndexOf("Leather", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            part = "Leather";
            color = new Color32(75, 51, 34, 255);
            smoothness = .24f;
        }
        else if (importedName.IndexOf("Brass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            part = "Brass";
            color = new Color32(154, 119, 63, 255);
            smoothness = .39f;
            metallic = .41f;
        }
        else if (importedName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            part = "Glass";
            color = new Color32(69, 96, 81, 255);
            smoothness = .67f;
        }
        else if (importedName.IndexOf("Seams", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            part = "Seams";
            color = enemy ? new Color32(67, 76, 71, 255) : new Color32(91, 79, 52, 255);
            smoothness = .21f;
        }
        else
        {
            part = "Cloth";
            color = enemy ? new Color32(105, 119, 110, 255) : new Color32(133, 121, 84, 255);
            smoothness = .23f;
        }

        string path = $"{Generated}/{(enemy ? "Gray" : "Khaki")} {part}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }
}
