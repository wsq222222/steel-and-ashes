using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OsowiecGateCaseBuilder
{
    const string Art = "Assets/Osowiec/Art/";
    const string Output = Art + "Resources/OsowiecGate/FieldCase.prefab";
    static readonly Dictionary<string,string> Slots = new()
    {
        { "Case_Canvas", "Cast/Game/Canvas.mat" },
        { "Case_Leather", "Cast/Game/Leather.mat" },
        { "Case_Brass", "Cast/Game/Brass.mat" },
        { "Case_Piping", "Cast/Game/Piping.mat" },
        { "Case_Oxide", "Fort1916/Oxide.mat" },
    };

    [MenuItem("Steel & Ashes/Art/Rebuild gate case")]
    public static void Generate()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "FieldCase.fbx");
        if (!asset) throw new InvalidOperationException("Missing Blender field case export");
        if (!AssetDatabase.IsValidFolder(Art + "Resources/OsowiecGate"))
            AssetDatabase.CreateFolder(Art + "Resources","OsowiecGate");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("FieldCase");
            SceneManager.MoveGameObjectToScene(root, scene);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
            model.transform.SetParent(root.transform,false);
            var points = model.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name is "CaseOrigin" or "CaseOutward" or "CaseUp")
                .ToDictionary(t => t.name);
            var origin = points["CaseOrigin"];
            Vector3 outward = points["CaseOutward"].position - origin.position;
            Vector3 up = points["CaseUp"].position - origin.position;
            model.transform.localScale *= .1f / up.magnitude;
            model.transform.rotation = Quaternion.LookRotation(Vector3.right,Vector3.up)
                * Quaternion.Inverse(Quaternion.LookRotation(outward,up)) * model.transform.rotation;
            model.transform.position -= origin.position;
            var renderers = model.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length != Slots.Count)
                throw new InvalidOperationException("Field case export needs five material batches");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Art + Slots[renderer.name]);
                if (!material || material.GetFloat("_Surface") != 0)
                    throw new InvalidOperationException("Expected an opaque cloth or hardware material: "+renderer.name);
                renderer.sharedMaterial = material;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                bounds.Encapsulate(renderer.bounds);
            }
            if (bounds.min.x < -.004f || bounds.size.x < .115f || bounds.size.x > .17f ||
                bounds.size.y < .30f || bounds.size.y > .36f || bounds.size.z < .27f || bounds.size.z > .32f)
                throw new InvalidOperationException("Incorrect field case support plane or metre scale: "+bounds);
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            if (!PrefabUtility.SaveAsPrefabAsset(root,Output))
                throw new InvalidOperationException("Failed to save the field case prefab");
            Debug.Log("Field case ready "+bounds);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
