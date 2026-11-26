using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OsowiecGateReview
{
    public static void Verify()
    {
        const string path="Assets/Osowiec/Osowiec.unity";
        var scene=SceneManager.GetSceneByPath(path);
        bool opened=!scene.isLoaded;
        if(opened) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        var gate=GameObject.Find(OsowiecEnvironment.GateHingeName).transform;
        var closed=gate.localRotation;
        try
        {
            Physics.SyncTransforms();
            int samples=0;
            for(float z=-13.64f;z<=-10.35f;z+=.13f)
            for(float y=.18f;y<=3.45f;y+=.15f)
            {
                if(y>1.83f+Mathf.Sqrt(Mathf.Max(0,1.68f*1.68f-(z+12)*(z+12)))-.045f) continue;
                if(!PartitionAt(y,z)) throw new InvalidOperationException($"Open gap in closed partition at y={y:F2} z={z:F2}");
                samples++;
            }
            gate.localRotation=closed*Quaternion.Euler(0,92,0);
            Physics.SyncTransforms();
            for(float z=-12.45f;z<-11.54f;z+=.15f)
                for(float y=.2f;y<1.96f;y+=.18f)
                    if(PartitionAt(y,z)) throw new InvalidOperationException("Open leaf or framing obstructs the central passage");
            if(!PartitionAt(2.51f,-12f) || !PartitionAt(.91f,-13.59f)) throw new InvalidOperationException("Fixed masonry moved with leaf");
            Debug.Log($"PASS gate {samples} closed rays, central passage clear with leaf open, fixed jamb and lintel still solid.");
        }
        finally
        {
            gate.localRotation=closed;
            Physics.SyncTransforms();
            if(opened) EditorSceneManager.CloseScene(scene,true);
        }
    }

    static bool PartitionAt(float y,float z)
    {
        foreach(var hit in Physics.RaycastAll(new Vector3(-.85f,y,z),Vector3.right,1.7f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(hit.collider.name.StartsWith("Solid_Casemate_gate") || hit.collider.name==OsowiecEnvironment.GateHingeName) return true;
        return false;
    }
}
