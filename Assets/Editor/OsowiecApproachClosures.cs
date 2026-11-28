using System;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

public static class OsowiecApproachClosures
{
    const string Mission=OsowiecProjectPaths.OperationScene;
    const string Root="Запруды у каземата и речного вала";
    const string Materials="Assets/Osowiec/Art/Fort1916/";
    static Material wood,bark,iron;

    static float Terrain(Vector3 p)
    {
        var hits=Physics.RaycastAll(new Vector3(p.x,14,p.z),Vector3.down,24,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        var ground=hits.Where(h=>h.collider.name.StartsWith("Biebrza ground") || h.collider.name.StartsWith("Ground_") || h.collider.name.StartsWith("Flood_meadow_") ||
            h.collider.name.StartsWith("Duckboard deck") || h.collider.name.StartsWith("Event_")).OrderBy(h=>h.distance).FirstOrDefault();
        if(!ground.collider)throw new InvalidOperationException("No surveyed earth under closure at "+p);
        return ground.point.y;
    }

    static GameObject Beam(Transform root,string name,Vector3 a,Vector3 b,float width,float depth,Material material,bool solid=false)
    {
        var board=GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name=name;board.transform.SetParent(root,true);
        Vector3 along=b-a;
        board.transform.SetPositionAndRotation((a+b)*.5f,Quaternion.FromToRotation(Vector3.up,along));
        board.transform.localScale=new Vector3(width,along.magnitude,depth);
        board.GetComponent<Renderer>().sharedMaterial=material;
        if(!solid)Object.DestroyImmediate(board.GetComponent<Collider>());
        board.isStatic=true;
        return board;
    }

    static void Revetment(Transform root,string name,params Vector2[] knots)
    {
        var run=new GameObject(name).transform;
        run.SetParent(root,false);
        var noWalk=run.gameObject.AddComponent<NavMeshModifier>();
        noWalk.overrideArea=true;
        noWalk.area=NavMesh.GetAreaFromName("Not Walkable");
        // Keep the surveyed collision spans used by the infantry and wagon routes.
        int index=0;
        for(int j=1;j<knots.Length;j++)
        {
            Vector3 from=new(knots[j-1].x,0,knots[j-1].y),to=new(knots[j].x,0,knots[j].y);
            int bays=Mathf.CeilToInt(Vector3.Distance(from,to)/2.25f);
            for(int i=0;i<bays;i++,index++)
            {
                Vector3 a=Vector3.Lerp(from,to,(float)i/bays),b=Vector3.Lerp(from,to,(i+1f)/bays);
                a.y=Terrain(a); b.y=Terrain(b);
                Vector3 direction=Vector3.ProjectOnPlane(b-a,Vector3.up);
                Vector3 side=Vector3.Cross(Vector3.up,direction).normalized;
                float lowest=Mathf.Min(a.y,b.y)-.28f, highest=Mathf.Max(a.y,b.y)+1.67f+.09f*Mathf.Sin(index*1.7f);
                Vector3 centre=(a+b)*.5f+side*(.025f*Mathf.Sin(index*13+12));
                centre.y=(lowest+highest)*.5f;
                var solid=new GameObject("Линия насыпи").AddComponent<BoxCollider>();
                solid.transform.SetParent(run,true);
                solid.transform.SetPositionAndRotation(centre,Quaternion.LookRotation(direction,Vector3.up));
                solid.size=new Vector3(.23f,highest-lowest,direction.magnitude+.10f);
                solid.gameObject.isStatic=true;
            }
        }
        OsowiecFieldRevetment.Bake(run,knots,Terrain);
    }

    public static void Install()
    {
        var fort=GameObject.Find("Osowiec · Fort I").transform;
        if(fort.Find(Root))Object.DestroyImmediate(fort.Find(Root).gameObject);
        var root=new GameObject(Root).transform;root.SetParent(fort,false);
        wood=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Adzed timber.mat");
        bark=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Bark.mat");
        iron=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Oxide.mat");
        Physics.SyncTransforms();
        // Join the west portal piers, leaving the playable door and the inner brick passage clear.
        Revetment(root,"Южное плечо каземата",new(-7.9f,-14.0f),new(-9.30f,-16.0f),new(-8.85f,-21.4f),new(-9.75f,-26.7f),new(-9.22f,-33.4f));
        Revetment(root,"Северное плечо каземата",new(-7.9f,-10.00f),new(-9.28f,-8.05f),new(-8.44f,-3.0f),new(-9.51f,2.3f),new(-9.42f,8.35f));
        Revetment(root,"Насыпь у реки",new(-7.50f,16.22f),new(-7.94f,18.35f),new(-7.95f,20.23f));
        Revetment(root,"Речной вал у фасада",new(-7.95f,23.77f),new(-8.52f,26.9f),new(-7.96f,29.31f),new(-8.27f,33.4f));
        MakeServiceBoom(root);
        Physics.SyncTransforms();
    }

    static void MakeServiceBoom(Transform root)
    {
        Vector3 at=new(-7.95f,0,20.20f);at.y=Terrain(at);
        var gate=new GameObject("Служебный проезд санитарной повозки").AddComponent<OsowiecBankPassage>();
        gate.transform.SetParent(root,false);gate.transform.position=at;
        gate.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
        var boom=new GameObject("Подъёмная перекладина").transform;
        boom.SetParent(gate.transform,false);boom.localPosition=Vector3.up*1.02f;gate.boom=boom;
        Vector3 hinge=boom.position;
        Beam(boom,"Верхняя перекладина",hinge,hinge+Vector3.forward*3.59f,.18f,.19f,wood,true);
        Beam(boom,"Нижняя перекладина",hinge-Vector3.up*.54f,hinge-Vector3.up*.54f+Vector3.forward*3.58f,.13f,.17f,wood,true);
        for(int i=0;i<4;i++)
        {
            Vector3 a=hinge+Vector3.forward*(.13f+i*.90f);
            Beam(boom,"Связь створки",a-Vector3.up*.54f,a+Vector3.forward*.71f,.085f,.12f,i%2==0?bark:wood,true);
        }
        Beam(boom,"Противовес",hinge+Vector3.forward*.08f,hinge-Vector3.forward*.50f,.25f,.25f,iron);
        foreach(var r in boom.GetComponentsInChildren<Renderer>())r.gameObject.isStatic=false;
        var block=gate.gameObject.AddComponent<NavMeshObstacle>();
        block.shape=NavMeshObstacleShape.Box;block.center=new Vector3(0,.89f,1.78f);block.size=new Vector3(.31f,1.78f,3.65f);
        block.carving=true;block.carveOnlyStationary=false;block.enabled=false;gate.pedestrianBlock=block;
    }

    public static void BakeCurrentArt()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=Mission)scene=EditorSceneManager.OpenScene(Mission);
        Install();
        Verify();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    public static void BakeCurrentScene()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=Mission)scene=EditorSceneManager.OpenScene(Mission);
        Install();
        var surface=Object.FindObjectsByType<NavMeshSurface>().Single(s=>s.enabled);
        RebuildNavigation(surface, () => { Verify(); OsowiecBatteryFort.VerifyPaths(); });
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    // The moveable door and units are added after navigation in the original level build.
    // Exclude the same physical geometry when rebuilding a populated, saved scene.
    internal static void RebuildNavigation(NavMeshSurface surface, Action verify)
    {
        var stored = surface.navMeshData;
        if (!stored || !EditorUtility.IsPersistent(stored))
            throw new InvalidOperationException("The saved mission must reference its navigation asset before rebaking.");
        var door = GameObject.Find(OsowiecEnvironment.GateHingeName)?.GetComponent<BoxCollider>();
        var moving = Object.FindObjectsByType<Unit>().SelectMany(u => u.GetComponentsInChildren<Collider>())
            .Append(door).Where(c => c && c.enabled).Distinct().ToArray();
        NavMeshData baked = null;
        try
        {
            surface.RemoveData();
            surface.navMeshData = null;
            try
            {
                foreach (var c in moving) c.enabled = false;
                Physics.SyncTransforms();
                surface.BuildNavMesh();
                baked = surface.navMeshData;
                if (!baked) throw new InvalidOperationException("Navigation produced no new data.");
            }
            finally
            {
                foreach (var c in moving) c.enabled = true;
                Physics.SyncTransforms();
            }
            verify();
            OsowiecReliefReview.VerifyRoutes();
            EditorUtility.CopySerialized(baked, stored);
            EditorUtility.SetDirty(stored);
        }
        finally
        {
            surface.RemoveData();
            surface.navMeshData = stored;
            surface.AddData();
            if (baked && baked != stored && !EditorUtility.IsPersistent(baked)) Object.DestroyImmediate(baked);
        }
    }

    static void Verify()
    {
        int blocked=0;
        var gate=Object.FindAnyObjectByType<OsowiecBankPassage>();
        var centre=gate.transform.position+new Vector3(0,1.02f,1.81f);
        bool HitsBoom()=>Physics.RaycastAll(centre-Vector3.right,Vector3.right,2f).Any(h=>h.collider.transform.IsChildOf(gate.boom));
        if(!HitsBoom())throw new InvalidOperationException("Closed wagon boom has no collision");
        gate.SetInspectionPose(true);
        if(HitsBoom())throw new InvalidOperationException("Raised wagon boom is still blocking the road");
        gate.SetInspectionPose(false);
        foreach(var section in new[]{new Vector2(-9.04f,-19f),new Vector2(-9.24f,-29f),new Vector2(-8.7f,-4.3f),new Vector2(-9.5f,4.4f),
            new Vector2(-7.94f,19.15f),new Vector2(-8.27f,25.65f)})
        {
            Vector3 mid=new(section.x,0,section.y);mid.y=Terrain(mid);
            for(int dir=-1;dir<=1;dir+=2)
            {
                Vector3 start=mid+Vector3.right*(dir*1.2f);
                var hits=Physics.CapsuleCastAll(start+Vector3.up*.28f,start+Vector3.up*1.48f,.22f,Vector3.left*dir,2.4f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                if(!hits.Any(h=>h.collider.transform.IsChildOf(GameObject.Find(Root).transform)))throw new InvalidOperationException("Capsule bypass at "+mid);
                blocked++;
            }
            if(NavMesh.SamplePosition(mid+Vector3.left*1.5f,out var a,.95f,NavMesh.AllAreas) &&
                NavMesh.SamplePosition(mid+Vector3.right*1.5f,out var b,.95f,NavMesh.AllAreas) &&
                !NavMesh.Raycast(a.position,b.position,out _,NavMesh.AllAreas))throw new InvalidOperationException("Navigable shortcut across "+mid);
        }
        VerifyPath(OsowiecOperation.Telephone,new Vector3(-6,0,-12));
        VerifyPath(OsowiecOperation.Telephone,OsowiecOperation.Lowland);
        VerifyPath(OsowiecOperation.Lowland,OsowiecOperation.Junction);
        VerifyPath(OsowiecOperation.Junction,OsowiecOperation.NorthLine);
        VerifyPath(OsowiecOperation.Junction,OsowiecOperation.SouthLine);
        Debug.Log("PASS "+blocked+" opposing capsule sweeps at the casemate and north bank; the two mission branches stay connected.");
    }

    static void VerifyPath(Vector3 from,Vector3 to)
    {
        var path=new NavMeshPath();
        if(!NavMesh.SamplePosition(from,out var a,2f,NavMesh.AllAreas) || !NavMesh.SamplePosition(to,out var b,2f,NavMesh.AllAreas) ||
            !NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
            throw new InvalidOperationException("Interrupted allowed route "+from+" to "+to);
    }
}
