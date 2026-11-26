using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OsowiecFieldTelephoneArt
{
    const string Mission = "Assets/Osowiec/Osowiec.unity";
    const string FortArt = "Assets/Osowiec/Art/Fort1916/";
    const string ModelPath = FortArt + "FieldTelephoneStation.fbx";
    const string HeadquartersArt = "Assets/Osowiec/HQ/Art/";

    public static void Install(Transform fort)
    {
        var station = fort.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "Field_station");
        var collider = station.GetComponent<MeshCollider>();
        if (collider == null || collider.sharedMesh == null)
            throw new InvalidOperationException("The field station has no surveyed collision mesh.");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Missing field telephone model: " + ModelPath);
        var source = model.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh.name == "FieldStationVisual");
        var mesh = source.sharedMesh;
        var slots = source.GetComponent<MeshRenderer>().sharedMaterials;
        if (slots.Length != mesh.subMeshCount || slots[0] == null || slots[0].name != "Timber")
            throw new InvalidOperationException("The first field telephone material must be the original shelter timber.");

        // Both FBXs use the same survey frame. The first submesh is the original timber
        // roof, shelf, posts and back wall. Leave the existing collision mesh on the station.
        var oldTimber = collider.sharedMesh.GetSubMesh(0).bounds;
        var newTimber = mesh.GetSubMesh(0).bounds;
        if ((newTimber.center - oldTimber.center).sqrMagnitude > .0001f ||
            (newTimber.size - oldTimber.size).sqrMagnitude > .0001f)
            throw new InvalidOperationException("Field telephone export moved the surveyed shelter timber.");

        Material Resolve(Material material)
        {
            if (material == null) throw new InvalidOperationException("Missing field telephone material slot.");
            string path = material.name == "Timber" ? FortArt + "Timber.mat" :
                HeadquartersArt + material.name + ".mat";
            return AssetDatabase.LoadAssetAtPath<Material>(path) ??
                throw new InvalidOperationException("Missing field telephone material: " + path);
        }
        var sceneMaterials = slots.Select(Resolve).ToArray();
        var renderer = station.GetComponent<MeshRenderer>();
        if (station.sharedMesh == mesh && renderer.sharedMaterials.SequenceEqual(sceneMaterials)) return;
        station.sharedMesh = mesh;
        renderer.sharedMaterials = sceneMaterials;
        EditorUtility.SetDirty(station);
        EditorUtility.SetDirty(renderer);
    }

    [MenuItem("Osowiec/Field telephone/Bake mission telephone")]
    public static void BakeCurrentScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != Mission) scene = EditorSceneManager.OpenScene(Mission);
        var root = scene.GetRootGameObjects().Single(o => o.name == "Osowiec · Fort I");
        Install(root.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}

