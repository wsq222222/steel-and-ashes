using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class OsowiecReliefReview
{
    const string Mission=OsowiecProjectPaths.OperationScene;

    public static void VerifyDrive()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(OsowiecProjectPaths.Prefab("MercyCart"));
        var cart=UnityEngine.Object.Instantiate(prefab);
        cart.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var mechanism=cart.GetComponent<OsowiecCartMotion>();
            if(mechanism==null)throw new InvalidOperationException("Rebuild MercyCart.prefab from its FBX first.");
            mechanism.VerifyDrive();
            var sound=Resources.Load<AudioClip>("OsowiecSound/CartAirMotor");
            if(!sound || sound.frequency!=48000 || sound.length<4)throw new InvalidOperationException("Missing authored cart drive audio.");
            Debug.Log($"PASS MercyCart pressure drive, connected wheels and rod, stationary brake, {sound.length:F2}s motor loop.");
        }
        finally {UnityEngine.Object.DestroyImmediate(cart);}
    }

    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finish Play Mode before checking relief routes.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scene before checking relief routes.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        bool swap=SceneManager.sceneCount!=1 || SceneManager.GetActiveScene().path!=Mission;
        NavMeshDataInstance added=default;
        try
        {
            if(swap) EditorSceneManager.OpenScene(Mission,OpenSceneMode.Single);
            Physics.SyncTransforms();
            VerifyFieldSolids();
            VerifyWire();
            var map=AssetDatabase.LoadAssetAtPath<NavMeshData>(OsowiecProjectPaths.OperationNavMesh);
            if(!NavMesh.SamplePosition(OsowiecReliefColumn.Hospital,out _,2f,NavMesh.AllAreas))
                added=NavMesh.AddNavMeshData(map);
            VerifyRoutes();
        }
        finally
        {
            if(added.valid) added.Remove();
            if(swap) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    internal static void VerifyRoutes()
    {
        for (int trip = 1; trip <= 3; trip++)
        foreach (bool marsh in new[] { false, true })
        {
            if (!OsowiecReliefColumn.VerifyEditorRoute(trip, marsh, out var result))
                throw new InvalidOperationException(result);
            Debug.Log("PASS Osowiec relief route " + result);
        }
    }

    static void VerifyFieldSolids()
    {
        const string art = "Assets/Osowiec/Art/Fort1916/";
        var filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(art + "MarshFort.fbx")
            .GetComponentsInChildren<MeshFilter>(true);
        MeshCollider Solid(MeshFilter f)
        {
            var solid = f.GetComponent<MeshCollider>();
            if (!solid || !solid.enabled || solid.isTrigger || !solid.sharedMesh || !f.sharedMesh)
                throw new InvalidOperationException("Missing solid field obstacle: " + f.name);
            return solid;
        }
        // These three visible replacements keep the surveyed silhouette for carts and soldiers.
        // Every other authored prop still uses its rendered mesh, including the four rebuilt end walls.
        foreach (var f in filters.Where(f => OsowiecEnvironment.IsSolidFieldProp(f.name)))
        {
            if (f.name == "Field_station" || f.name == "Battery_statuary") continue;
            if (Solid(f).sharedMesh != f.sharedMesh)
                throw new InvalidOperationException("Unexpected collision mesh on field obstacle: " + f.name);
        }
        MeshCollider Surveyed(string label, string visual)
        {
            var f = filters.Single(item => item.name == label);
            var collision = Solid(f);
            var original = source.Single(item => item.name == label).sharedMesh;
            if (collision.sharedMesh != original || AssetDatabase.GetAssetPath(f.sharedMesh) != art + visual)
                throw new InvalidOperationException("Incorrect visual or surveyed collision mesh on " + label);
            return collision;
        }
        void Surface(MeshCollider collider, Vector3 from, Vector3 dir, float range)
        {
            if (!collider.Raycast(new Ray(from, dir), out _, range))
                throw new InvalidOperationException("Missing physical surface on " + collider.name + " at " + from);
        }

        var station = Surveyed("Field_station", "FieldTelephoneStation.fbx");
        Surface(station, new Vector3(-15f, 2.5f, -4.8f), Vector3.down, .75f);
        var iron = Surveyed("Battery_statuary", "Battery iron and centre drums.asset");
        foreach (var emplacement in new[] { (32f, 9f, 1.19f), (40f, -8f, 2.10f) })
        {
            var forward = new Vector3(Mathf.Cos(emplacement.Item3), 0, Mathf.Sin(emplacement.Item3));
            var front = new Vector3(emplacement.Item1, 2.7f, emplacement.Item2) + forward * 5.2f;
            Surface(iron, front, -forward, 2f);
        }
        var sacks = Surveyed("Solid_Sandbags", "Battery field filled sacks and sewn hems.asset");
        foreach (var position in new[] { new Vector2(-18,7), new Vector2(6,16), new Vector2(16,9),
                     new Vector2(28,5), new Vector2(37,3), new Vector2(20,-9) })
            Surface(sacks, new Vector3(position.x,12f,position.y), Vector3.down, 22f);
    }

    static void VerifyWire()
    {
        var fence=GameObject.Find("Field_wire").GetComponent<MeshFilter>();
        var mesh=fence.sharedMesh;
        var materials=fence.GetComponent<MeshRenderer>().sharedMaterials;
        int steel=Array.FindIndex(materials,m=>m.name=="Oxide");
        if(steel<0)throw new InvalidOperationException("Missing fence strands");
        var vertices=mesh.vertices;
        var triangles=mesh.GetTriangles(steel);
        for(int i=0;i<triangles.Length;i+=3)
        {
            Vector3 point=fence.transform.TransformPoint((vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3);
            if(Mathf.Abs(point.x)>1.5f || point.z<23 || point.z>32)continue;
            var axle=point-new Vector3(0,.63f,0);
            if(OsowiecCartMotion.Sweep(axle-Vector3.forward*.18f,Quaternion.identity,axle,Quaternion.identity,out var obstacle) ||
               !obstacle || obstacle.gameObject!=fence.gameObject)
                throw new InvalidOperationException("A loaded cart did not meet the actual steel strand at "+point);
            Debug.Log("PASS cart detects the northern fence at an exported steel strand "+point);
            return;
        }
        throw new InvalidOperationException("No northern strand found on the raised fence line");
    }
}
