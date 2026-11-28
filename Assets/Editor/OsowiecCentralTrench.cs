using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// The same surveyed centreline drives the excavation, duckboards and printed field map.
public static class OsowiecCentralTrench
{
    internal static readonly Vector2[] Course = {
        new(-25.6f,2.5f), new(-21.4f,2.5f), new(-19.25f,1), new(-15,1)
    };
    const string Field = OsowiecProjectPaths.OperationScene;
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    const string Prefix = "Central trench - ";
    const string Root = "Центральная траншея · сбор";
    static readonly HashSet<string> Cover = new() {
        "Marsh_cover_2_2", "Marsh_cover_2_1", "Folded_sedge_3_2",
        "Seedheads_3_2", "Marsh_cover_river_-2_0", "Reed_panicles_-1_0"
    };

    static float Distance(Vector3 p)
    {
        var pos = new Vector2(p.x,p.z);
        float best = float.PositiveInfinity;
        for (int i=1;i<Course.Length;i++)
        {
            var a = Course[i-1]; var ab = Course[i]-a;
            var near = a+ab*Mathf.Clamp01(Vector2.Dot(pos-a,ab)/ab.sqrMagnitude);
            best = Mathf.Min(best,(near-pos).sqrMagnitude);
        }
        return Mathf.Sqrt(best);
    }

    public static void Install()
    {
        var fort = GameObject.Find("Osowiec · Fort I").transform;
        var originals = AssetDatabase.LoadAllAssetsAtPath(Art+"MarshFort.fbx").OfType<Mesh>().ToLookup(m=>m.name);
        int cut = 0;
        foreach (var filter in fort.GetComponentsInChildren<MeshFilter>(true))
        {
            bool ground = filter.name=="Biebrza ground Ground_2_2" || filter.name=="Biebrza ground Ground_3_2";
            if (!ground && !Cover.Contains(filter.name)) continue;
            var source = filter.sharedMesh;
            if (source.name.StartsWith(Prefix,StringComparison.Ordinal))
                source = originals[source.name.Substring(Prefix.Length)].Single();
            var verts = source.vertices;
            var inside = ground ? null : new bool[verts.Length];
            for (int i=0;i<verts.Length;i++)
            {
                var w = filter.transform.TransformPoint(verts[i]);
                if (w.x < -29 || w.x > -11.6f || w.z < -2.4f || w.z > 5.95f) continue;
                float d = Distance(w);
                if (!ground) { inside[i] = d < 2.85f; continue; }
                if (d>=3.25f) continue;
                float room = Mathf.InverseLerp(-17.4f,-21,w.x);
                float bed = Mathf.Lerp(.85f,1.52f,room);
                float shoulder = Mathf.SmoothStep(0,1,Mathf.InverseLerp(bed,2.50f,d));
                float crown = (.94f+.32f*Mathf.PerlinNoise(w.x*.81f,w.z*.85f))*room;
                float shape = Mathf.Lerp(-.083f,crown,shoulder);
                float blend = 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.42f,3.25f,d));
                w.y = Mathf.Lerp(w.y,shape,blend);
                verts[i]=filter.transform.InverseTransformPoint(w);
            }
            var name = Prefix + source.name;
            string path = Art+name+".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh=Object.Instantiate(source); mesh.name=name; AssetDatabase.CreateAsset(mesh,path); }
            if (ground)
            {
                mesh.vertices=verts; mesh.RecalculateNormals();
                if(source.uv.Length==verts.Length)mesh.RecalculateTangents();
                cut++;
            }
            else
            {
                for(int s=0;s<source.subMeshCount;s++)
                {
                    var tris=source.GetTriangles(s);
                    var kept=new List<int>(tris.Length);
                    for(int j=0;j<tris.Length;j+=3)
                        if(!inside[tris[j]] && !inside[tris[j+1]] && !inside[tris[j+2]])
                        { kept.Add(tris[j]);kept.Add(tris[j+1]);kept.Add(tris[j+2]); }
                    mesh.SetTriangles(kept,s,false);
                }
            }
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            filter.sharedMesh=mesh;
            if(filter.TryGetComponent<MeshCollider>(out var collider))
            { collider.sharedMesh=null; collider.sharedMesh=mesh; }
        }
        if(cut!=2)throw new InvalidOperationException($"Central trench expected two terrain tiles, found {cut}.");
        var old=fort.Find(Root);
        if(old)Object.DestroyImmediate(old.gameObject);
        var root=new GameObject(Root).transform;
        root.SetParent(fort,true);
        OsowiecBatteryFort.MakeTrenchWalkway(root,Course);
        Physics.SyncTransforms();
    }

    [MenuItem("Steel and Ashes/Rebuild central trench and fort passage")]
    public static void BakeCurrentScene()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=Field)scene=EditorSceneManager.OpenScene(Field);
        Install();
        OsowiecBatteryFort.RepairPassageInLoadedScene();
        var surface=Object.FindObjectsByType<NavMeshSurface>().Single(s=>s.enabled);
        OsowiecApproachClosures.RebuildNavigation(surface,()=>{
            VerifyRoutes();
            OsowiecBatteryFort.VerifyPaths();
            OsowiecBatteryFort.VerifyPassageBothWays();
        });
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        OsowiecBriefingSurvey.Export();
    }

    internal static void VerifyRoutes()
    {
        Vector3 OnNav(Vector3 p,float tolerance)
        {
            if(!NavMesh.SamplePosition(p,out var hit,tolerance,NavMesh.AllAreas))
                throw new InvalidOperationException($"Central trench missing walkable ground at {p}.");
            return hit.position;
        }
        void Route(Vector3 from,Vector3 to,float maxLength)
        {
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(from,to,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException($"No continuous route between {from} and {to}.");
            var corners=path.corners;
            float length=0;
            for(int i=1;i<corners.Length;i++)length+=Vector3.Distance(corners[i-1],corners[i]);
            if(length>maxLength)throw new InvalidOperationException($"Detour of {length:F2}m between {from} and {to}.");
        }
        var grid=Object.FindFirstObjectByType<ChessGrid>();
        var phone=OnNav(OsowiecOperation.Telephone,1.5f);
        for(int sector=0;sector<OsowiecDeploymentSectors.Cells.Length;sector++)
        foreach(var cell in OsowiecDeploymentSectors.Cells[sector])
        {
            var centre=grid.GridToWorldPosition(cell.x,cell.y);
            var p=OnNav(centre,.65f);
            if(sector==1)
            {
                // A slot can be near a navigable cell yet sit on a wall or above the boards.
                if(!Physics.Raycast(centre+Vector3.up*.60f,Vector3.down,out var floor,1.0f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) ||
                    floor.collider.name!="Central assembly duckboards" || Mathf.Abs(floor.point.y-p.y)>.28f)
                    throw new InvalidOperationException($"Deployment {cell} does not stand on its duckboards.");
                var obstruction=Physics.OverlapCapsule(floor.point+Vector3.up*.32f,floor.point+Vector3.up*1.52f,
                    .24f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(c=>!c.GetComponentInParent<Unit>());
                if(obstruction)throw new InvalidOperationException($"Deployment {cell} intersects {obstruction.name}.");
            }
            Route(p,phone,65);
        }
        Vector3 prior=phone;
        for(int i=Course.Length-1;i>=0;i--)
        {
            var p=OnNav(new Vector3(Course[i].x,0,Course[i].y),.47f);
            Route(prior,p,Vector3.Distance(prior,p)+1.65f);
            Route(p,prior,Vector3.Distance(prior,p)+1.65f);
            prior=p;
        }
        Debug.Log("PASS central trench: six deployment cells reach the phone and both directions follow the cut.");
    }

}

public static partial class OsowiecBatteryFort
{
    internal static void MakeTrenchWalkway(Transform root,Vector2[] course)
    {
        var deck=new Masonry(); var lining=new Masonry();
        var rng=new System.Random(5047);
        float Jitter(float scale)=>(float)(rng.NextDouble()-.5)*scale;
        Vector3 Point(Vector2 p)=>new Vector3(p.x,0,p.y);
        // At turns adjacent boards share a mitred edge, keeping a level continuous tread.
        var side=new Vector3[course.Length-1];
        for(int s=0;s<side.Length;s++)
            side[s]=Vector3.Cross(U,(Point(course[s+1])-Point(course[s])).normalized);
        var miter=new Vector3[course.Length];miter[0]=side[0];miter[^1]=side[^1];
        for(int i=1;i<miter.Length-1;i++)
        {
            var half=(side[i-1]+side[i]).normalized;
            miter[i]=half/Vector3.Dot(half,side[i]);
        }
        for(int s=0;s<course.Length-1;s++)
        {
            var a=Point(course[s]);var b=Point(course[s+1]);float length=Vector3.Distance(a,b);
            Vector3 CrossEnd(float t,float signedWidth)
                =>Vector3.Lerp(a,b,t)+Vector3.Lerp(miter[s],miter[s+1],t)*signedWidth;
            float Width(float t)=>Mathf.Lerp(.72f,1.34f,Mathf.InverseLerp(-17.5f,-21,Mathf.Lerp(a.x,b.x,t)));
            int count=2*Mathf.RoundToInt((length/.248f-1f)*.5f)+1;
            for(int i=0;i<count;i++)
            {
                float t0=(i+.024f)/count,t1=(i+.976f)/count;
                float w0=Width(t0)+Jitter(.026f),w1=Width(t1)+Jitter(.026f);
                float y=.041f+Jitter(.009f);
                var polygon=new[]{CrossEnd(t0,-w0),CrossEnd(t1,-w1),CrossEnd(t1,w1),CrossEnd(t0,w0)};
                for(int j=0;j<4;j++)polygon[j]+=U*y;
                deck.Prism(i%5==2 ? Finish.SplitWood : Finish.Timber,polygon,-U*.10f);
            }
            if(s==0)
            {
                foreach(int k in new[]{-1,1})
                {
                    for(int j=0;j<4;j++)
                    {
                        var p0=CrossEnd(.01f,(1.65f+j*.043f)*k)+U*(.12f+j*.19f);
                        var p1=CrossEnd(.985f,(1.65f+j*.043f)*k)+U*(.12f+j*.19f+Jitter(.025f));
                        lining.Box(j==1 ? Finish.SplitWood : Finish.Timber,(p0+p1)*.5f,
                            side[s]*.087f,U*.178f,p1-p0);
                    }
                    for(int j=0;j<=4;j++)
                    {
                        var post=CrossEnd(.035f+.23f*j,1.61f*k)+U*.384f;
                        lining.Box(Finish.Timber,post,side[s]*.118f,U*(.82f+Jitter(.08f)),(b-a).normalized*.14f);
                        foreach(float y in new[]{-.17f,.19f})
                            lining.Block(Finish.Iron,post-side[s]*(.065f*k)+U*y,.025f,.034f,.035f);
                    }
                }
            }
        }
        var tread=deck.Save(root,"Central assembly duckboards");
        tread.gameObject.AddComponent<OsowiecAcousticSurface>().surface=OsowiecGroundSurface.Timber;
        lining.Save(root,"Central assembly retaining boards");
    }
}
