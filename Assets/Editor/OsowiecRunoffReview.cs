using System;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class OsowiecRunoffReview
{
    [MenuItem("Steel and Ashes/Verify water contact and routes after bombardment")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finish Play Mode before checking the field.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scene before checking the field.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        NavMeshData temporary=null;
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/Osowiec/Osowiec.unity",OpenSceneMode.Single);
            var objects=scene.GetRootGameObjects();
            var events=objects.SelectMany(o=>o.GetComponentsInChildren<OsowiecGroundEvents>()).Single();
            var surface=objects.SelectMany(o=>o.GetComponentsInChildren<NavMeshSurface>()).Single(s=>s.enabled);
            if(!events.water || !events.plumeMaterial || !events.water.sharedMesh.isReadable)
                throw new InvalidOperationException("Missing surveyed water or plume");
            events.SendMessage("Awake"); events.SendMessage("CacheWater");
            var verts=events.water.sharedMesh.vertices;
            var triangles=events.water.sharedMesh.triangles;
            int tested=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var p=events.water.transform.TransformPoint((verts[triangles[i]]+verts[triangles[i+1]]+verts[triangles[i+2]])/3);
                if(!OsowiecGroundEvents.TryWaterAt(p-.05f*Vector3.up,out var actual) || Mathf.Abs(p.y-actual.y)>.001f ||
                    OsowiecGroundEvents.TryWaterAt(p+Vector3.up,out _))
                    throw new InvalidOperationException("Water query failed near "+p);
                tested++;
            }
            if(tested<100) throw new InvalidOperationException("Missing authored pools");
            for(int i=0;i<events.intact.Length;i++)
            {
                events.intact[i].SetActive(false);
                events.hollows[i].SetActive(true);
            }
            events.wire[1].SetActive(false);
            Physics.SyncTransforms();
            surface.RemoveData(); surface.navMeshData=null;
            surface.BuildNavMesh(); temporary=surface.navMeshData;
            for(int trip=1;trip<=3;trip++)
                foreach(bool marsh in new[]{false,true})
                    if(!OsowiecReliefColumn.VerifyEditorRoute(trip,marsh,out var reason))
                        throw new InvalidOperationException(reason);
            Debug.Log($"PASS Osowiec water contact ({tested} triangles), clearance above pools, six cart paths with both craters open.");
        }
        finally
        {
            EditorSceneManager.OpenScene("Assets/Osowiec/Osowiec.unity",OpenSceneMode.Single);
            EditorSceneManager.RestoreSceneManagerSetup(setup);
            if(temporary) UnityEngine.Object.DestroyImmediate(temporary);
        }
    }
}
