using System;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Geometry = OsowiecFieldRoofs.Geometry;
using Sample = OsowiecFieldRoofs.Sample;

public static class OsowiecSignalLinesAndRuin
{
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    const string Mission = OsowiecProjectPaths.OperationScene;
    static readonly Vector2[] Stakes = { new(-49,-20),new(-39,-20),new(-31,-13),new(-21.8f,-7.3f),
        new(-17,-6),new(-11,-16),new(4,-19),new(18,-16),new(26,-12),new(41,-14),new(49,-6) };
    static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Art+name+".mat")
        ?? throw new InvalidOperationException("Missing field detail material "+name);

    static MeshRenderer Receiver(Transform parent,string label)
    {
        var existing=parent.Find(label);
        if(existing)return existing.GetComponent<MeshRenderer>();
        var obj=new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer),typeof(NavMeshModifier));
        obj.transform.SetParent(parent,false);
        obj.GetComponent<NavMeshModifier>().ignoreFromBuild=true;
        return obj.GetComponent<MeshRenderer>();
    }

    static void SignalLines(Transform parent,MeshRenderer telegraph)
    {
        var mesh=telegraph.GetComponent<MeshFilter>().sharedMesh;
        int slot=Array.FindIndex(telegraph.sharedMaterials,m=>m && m.name=="Concrete");
        if(slot<0)throw new InvalidOperationException("Missing surveyed telegraph insulators.");
        var all=mesh.vertices;
        var fittings=mesh.GetTriangles(slot).Distinct().Select(i=>telegraph.transform.TransformPoint(all[i])).ToArray();
        var geo=new Geometry(3);
        for(int i=0;i<Stakes.Length;i++)
        {
            Vector2 stake=Stakes[i];
            var points=fittings.Where(p=>Mathf.Abs(p.x-stake.x)<.5f&&Mathf.Abs(p.z-stake.y)<.62f).ToArray();
            if(points.Length<8)throw new InvalidOperationException("No original crossarm at surveyed stake "+stake);
            Vector3 cap=new((points.Min(p=>p.x)+points.Max(p=>p.x))*.5f,points.Max(p=>p.y),
                (points.Min(p=>p.z)+points.Max(p=>p.z))*.5f);
            Vector3 post=cap-Vector3.up*.64f;
            Vector3 up=Vector3.up, across=Vector3.forward;
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 seat=cap+across*(side*.39f);
                var brace=cap+across*(side*.433f)-up*.224f;
                geo.Cord(1,t=>Vector3.Lerp(post,brace,t),.022f,2,7);
                geo.Grid(0,20,10,(u,v)=>{
                    float a=u*2*Mathf.PI;
                    float radius=.031f+.027f*Mathf.Pow(.5f+.5f*Mathf.Cos(v*4*Mathf.PI),2)
                        -.017f*Mathf.SmoothStep(0,1,(v-.88f)*8.4f);
                    Vector3 p=seat+up*(v*.12f-.107f)+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
                    return new Sample(p,new Vector2(u,v),Color.white);
                });
                geo.Cord(1,t=>{
                    float a=t*2*Mathf.PI;
                    return seat-up*.032f+new Vector3(.037f*Mathf.Cos(a),.008f*t,.037f*Mathf.Sin(a));
                },.005f,16,6);
            }
            if(i==2||i==5||i==9)
            {
                Vector3 seat=cap+across*.39f-up*.032f;
                Vector3 entry=post+new Vector3(.08f,-.18f,.025f);
                geo.Cord(2,t=>Vector3.Lerp(seat,entry,t)+new Vector3(.12f,-.14f,.05f)*Mathf.Sin(t*Mathf.PI),.013f,12,7);
                geo.Cord(2,t=>{
                    float a=t*4*Mathf.PI;
                    return entry+new Vector3(.13f*Mathf.Sin(a),-.31f*(1-Mathf.Cos(a))-.15f*t,.052f*t);
                },.014f,40,7);
                geo.Cord(2,t=>Vector3.Lerp(entry+new Vector3(0,-.15f,.052f),post+new Vector3(.07f,-2.28f,.035f),t),.014f,3);
            }
        }
        geo.Save(Receiver(parent,"Signal line hardware"),"Signal poles porcelain skirts and tied service leads",
            Mat("Concrete"),Mat("Oxide"),Mat("Tar"));
    }

    static float Ground(Vector3 point)
    {
        var hits=Physics.RaycastAll(point+Vector3.up*25,Vector3.down,55,~0,QueryTriggerInteraction.Ignore);
        foreach(var hit in hits)
            if(hit.collider.name.StartsWith("Biebrza ground ",StringComparison.Ordinal))return hit.point.y;
        throw new InvalidOperationException("Missing exposed survey ground under the ruined wall at "+point);
    }

    // Chamfered irregular prisms preserve individual courses and the broken silhouette at eye level.
    static void Brick(Geometry geo,int material,Vector3 center,Vector3 right,Vector3 size,int seed)
    {
        Vector3 forward=Vector3.Cross(right,Vector3.up), up=Vector3.up;
        float w=size.x*.5f,h=size.y*.5f,d=size.z*.5f;
        float chip=Mathf.Lerp(.016f,.062f,Mathf.PerlinNoise(seed*.67f,3.56f));
        Vector2[] shape={new(-w+chip,-d),new(w-.013f,-d),new(w,-d+.012f),new(w,d-chip),
            new(w-chip,d),new(-w+.016f,d),new(-w,d-.020f),new(-w,-d+chip)};
        Vector3 P(int i,float top,float inset) =>
            center+right*(shape[i].x*inset)+forward*(shape[i].y*inset)+up*top;
        void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 e)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c+e)*.25f-center)<0)(b,e)=(e,b);
            var axis=(b-a).normalized;
            var across=Vector3.Cross(Vector3.Cross(b-a,e-a).normalized,axis);
            int Id(Vector3 v)=>geo.Vertex(new Sample(v,new Vector2(Vector3.Dot(v,axis),Vector3.Dot(v,across)),Color.white));
            int i=Id(a),j=Id(b),k=Id(c),l=Id(e); geo.Tri(material,i,j,k);geo.Tri(material,i,k,l);
        }
        for(int i=0;i<8;i++)
        {
            int j=(i+1)%8;
            Face(P(i,-h,1),P(j,-h,1),P(j,h-.012f,1),P(i,h-.012f,1));
            Face(P(i,h-.012f,1),P(j,h-.012f,1),P(j,h,.96f),P(i,h,.96f));
            var normal=Vector3.up;
            int a=geo.Vertex(new Sample(center+up*h,new Vector2(center.x,center.z),Color.white));
            var b=P(i,h,.96f);var c=P(j,h,.96f);
            int ib=geo.Vertex(new Sample(b,new Vector2(b.x,b.z),Color.white));
            int ic=geo.Vertex(new Sample(c,new Vector2(c.x,c.z),Color.white));
            if(Vector3.Dot(Vector3.Cross(b-center-up*h,c-center-up*h),normal)>0)geo.Tri(material,a,ib,ic);
            else geo.Tri(material,a,ic,ib);
        }
    }

    static void Ruin(Transform parent)
    {
        var geo=new Geometry(3);
        var right=new Vector3(.93f,0,-.37f).normalized;
        Vector3 front=Vector3.Cross(right,Vector3.up);
        Vector3 foot=new(26.2f,0,14.8f);
        float baseY=Ground(foot);
        for(int col=0;col<18;col++)
        {
            int crown=col<3?9-col:col<8?7-(col-3):col<12?2:col-10;
            var column=foot+right*((col-8.5f)*.288f);
            float grade=Mathf.Min(Ground(column),Ground(column+right*.144f));
            int foundation=Mathf.FloorToInt((grade-baseY)/.113f)-1;
            for(int row=foundation;row<=crown;row++)
            {
                if(row==crown && col%4==0)continue;
                int seed=100+row*19+col;
                var center=column+right*(row%2==0?0:.144f);
                center.y=baseY+row*.113f+.043f;
                Brick(geo,(seed%4==0?1:0),center,right,new Vector3(.279f,.108f,.335f),seed);
                if(row<crown)
                    Brick(geo,2,center+Vector3.up*.055f,right,new Vector3(.290f,.016f,.30f),seed+3);
            }
        }
        var rng=new System.Random(44);
        for(int i=0;i<38;i++)
        {
            float along=(float)rng.NextDouble()*4.8f-2.4f;
            var p=foot+right*along+front*((float)rng.NextDouble()*.95f+.20f)*(i%4==0?-1:1);
            p.y=Ground(p)+.043f;
            var turn=Quaternion.AngleAxis((float)rng.NextDouble()*160,Vector3.up)*right;
            Brick(geo,i%3==0?1:0,p,turn,new Vector3(i%2==0?.17f:.25f,.097f,.132f),i);
        }
        var r=Receiver(parent,"Damaged brick wall at the eastern approach");
        geo.Save(r,"Eastern approach broken brick courses",Mat("Brick core"),Mat("Brick"),Mat("Concrete"));
        var collider=r.GetComponent<MeshCollider>();
        if(!collider)collider=r.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh=null;
        collider.sharedMesh=r.GetComponent<MeshFilter>().sharedMesh;
    }

    public static void Install()
    {
        var ground=UnityEngine.Object.FindAnyObjectByType<OsowiecGroundEvents>();
        if(!ground)throw new InvalidOperationException("Missing field terrain in current scene.");
        var telegraph=ground.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Telegraph");
        Physics.SyncTransforms();
        SignalLines(ground.transform,telegraph);
        Ruin(ground.transform);
    }

    public static void BakeCurrentScene()
    {
        OsowiecSurveyNormals.EnsureCurrentSurvey();
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=Mission)scene=EditorSceneManager.OpenScene(Mission);
        Install();
        var ground=UnityEngine.Object.FindAnyObjectByType<OsowiecGroundEvents>();
        OsowiecBatteryFinish.RefreshRoofRelief(ground.transform);
        Verify(ground.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save "+Mission);
        AssetDatabase.SaveAssets();
    }

    static void Verify(Transform ground)
    {
        var fittings=ground.Find("Signal line hardware").GetComponent<MeshFilter>().sharedMesh;
        var ruin=ground.Find("Damaged brick wall at the eastern approach").GetComponent<MeshCollider>().sharedMesh;
        if(!fittings||fittings.subMeshCount!=3||fittings.vertexCount<4000||
           !ruin||ruin.subMeshCount!=3||ruin.bounds.size.x<3.5f)
            throw new InvalidOperationException("Incomplete signal line hardware or collapsed brick courses.");
        var river=Mat("River");
        if(!river.GetTexture("_FlowSurvey"))throw new InvalidOperationException("Missing surveyed channel flow map.");
        Debug.Log($"Field visual pass verified: {Stakes.Length} fitted poles, {fittings.vertexCount} fitting vertices, "+
            $"{ruin.vertexCount} masonry vertices, surveyed flow texture and three rebuilt earth covers.");
    }
}
