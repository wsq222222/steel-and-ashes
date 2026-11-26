using System;
using System.Linq;
using UnityEngine;

public static partial class OsowiecBatteryFort
{
    static void Shard(Masonry g,Vector3 p,Vector3 along,Vector3 side,float size)
    {
        var edge=p+along*size+side*(size*.14f)+U*(size*.42f);
        var crease=p+along*(size*.46f)+U*(size*.08f);
        var sections=new[]{p-side*(size*.19f),p+side*(size*.24f),crease+side*(size*.20f),
            crease-side*(size*.27f)};
        g.Prism(Finish.Iron,sections,U*.009f);
        g.Prism(Finish.Iron,new[]{sections[3],sections[2],edge+side*(size*.08f),edge-side*(size*.19f)},U*.009f);
    }
    static bool DebrisGround(Vector3 p,out Vector3 contact)
    {
        foreach(var hit in Physics.RaycastAll(p+U*16,-U,38,~0,QueryTriggerInteraction.Ignore))
            if(hit.collider.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal))
            {contact=hit.point;return true;}
        contact=default;return false;
    }
    static void Rim(Masonry g,Vector3 centre,float radius,int seed,Func<Vector3,Vector3?> floor)
    {
        var rng=new System.Random(seed);
        float Pick(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
        for(int cluster=0;cluster<3;cluster++)
        {
            float sector=Pick(0,6.28f);
            for(int j=0;j<6;j++)
            {
                float a=sector+Pick(-.34f,.34f);
                var p=centre+V(Mathf.Cos(a),0,Mathf.Sin(a)*.88f)*radius*Pick(.77f,1.13f);
                var hit=floor(p);if(hit==null)continue;p=hit.Value+U*.013f;
                var along=Quaternion.AngleAxis(Pick(-180,180),U)*R;
                var side=Vector3.Cross(U,along);float size=Pick(.15f,.41f);
                if(j==0 || j==4){Shard(g,p,along,side,size);continue;}
                if(j==2 || j==5)
                {
                    var end=p+along*size+U*Pick(.024f,.11f);
                    g.Prism(Finish.SplitWood,new[]{p-side*.027f,p+side*.036f,
                        end+side*.016f,end+along*.075f,end-side*.030f},U*.025f);
                    continue;
                }
                float h=size*Pick(.27f,.44f);
                g.Prism(j==1?Finish.BrokenBrick:Finish.Concrete,new[]{p-along*(size*.42f)-side*(size*.22f),
                    p+along*(size*.41f)-side*(size*.26f),p+along*(size*.53f)+side*(size*.09f),
                    p+along*(size*.23f)+side*(size*.28f),p-along*(size*.36f)+side*(size*.19f)},U*h+side*.018f);
            }
        }
    }
    static void InstallRimDebris(Transform fort,Transform battery)
    {
        // The standing-water mesh was cut to the depressions of the game landscape.
        var oldHoles=new[]{V(-21,3.1f,9),V(-8,4,17.5f),V(1,2.5f,10),V(7,2.9f,16),V(13.5f,2.6f,5.8f),
            V(-38,3.6f,6.5f),V(-4,3.6f,23),V(17,4,18.5f),V(10,4,-21),V(29,3.2f,-21)};
        var debris=new Masonry();
        foreach(var p in oldHoles)
            Rim(debris,V(p.x,0,p.z),p.y,Mathf.RoundToInt(p.x*117+p.z*89),
                v=>DebrisGround(v,out var hit)?hit:(Vector3?)null);
        debris.Save(battery,"Debris on old crater rims",false);
        var events=fort.GetComponent<OsowiecGroundEvents>();
        for(int index=0;index<events.hollows.Length;index++)
        {
            var hollow=events.hollows[index];
            var label="Debris on fresh crater rim "+index;
            var previous=hollow.transform.Find(label);
            if(previous)UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var mf=hollow.GetComponent<MeshFilter>();
            var vertices=mf.sharedMesh.vertices.Select(mf.transform.TransformPoint).ToArray();
            var triangles=mf.sharedMesh.triangles;
            var bounds=new Bounds(vertices[0],Vector3.zero);
            foreach(var v in vertices)bounds.Encapsulate(v);
            Vector3? Floor(Vector3 p)
            {
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=vertices[triangles[i]];var ab=vertices[triangles[i+1]]-a;var ac=vertices[triangles[i+2]]-a;
                    float det=ab.x*ac.z-ab.z*ac.x;if(Mathf.Abs(det)<.00001f)continue;
                    var delta=p-a;float u=(delta.x*ac.z-delta.z*ac.x)/det,v=(ab.x*delta.z-ab.z*delta.x)/det;
                    if(u>=0 && v>=0 && u+v<=1)return a+ab*u+ac*v;
                }
                return DebrisGround(p,out var edge)?edge:(Vector3?)null;
            }
            var fresh=new Masonry();Rim(fresh,bounds.center,1.61f,1231+index*77,Floor);
            var prop=fresh.Save(battery,label,false);
            prop.transform.SetParent(hollow.transform,true);
        }
    }
}
