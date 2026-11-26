using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Build the far riparian coppices on the actual relief exported with the playable fort.
public static class OsowiecFloodplain
{
    const string Folder = "Assets/Osowiec/Art/Fort1916/";

    public static void Create(Transform root)
    {
        var vertices = new List<Vector3>();
        var faces = new List<int>();
        var uv = new List<Vector2>();
        var crowns = new List<(Vector3 centre, float width, float height)>();
        Physics.SyncTransforms();
        CreateSeepage(root);
        var rng = new System.Random(1916);
        float Range(float min,float max) => min+(float)rng.NextDouble()*(max-min);
        for (int grove=0; grove<90; grove++)
        {
            float bearing=Range(0,Mathf.PI*2);
            float gx=Mathf.Cos(bearing)*Range(94,217), gz=Mathf.Sin(bearing)*Range(73,190);
            for(int tree=0;tree<8;tree++)
            {
                float x=gx+Range(-12,12), z=gz+Range(-9,9);
                if(Mathf.Abs(x)<75 && Mathf.Abs(z)<53)continue;
                if(!Physics.Raycast(new Vector3(x,24,z),Vector3.down,out var ground,40,~0,QueryTriggerInteraction.Ignore)
                    || ground.point.y < -.16f || !ground.collider.name.StartsWith("Biebrza ground "))continue;
                float y=ground.point.y-.07f, h=Range(2.3f,6.8f);
                Vector3 foot=new(x,y,z);
                Vector3 direction=new(Range(-.12f,.12f),1,Range(-.12f,.12f));
                Vector3 crown=foot+direction*h;
                Stem(foot,crown,h*.029f,.021f);
                for (int branch=0;branch<8;branch++)
                {
                    float level=Range(.25f,.85f), heading=Range(0,2*Mathf.PI);
                    var outward=new Vector3(Mathf.Cos(heading),0,Mathf.Sin(heading));
                    var origin=Vector3.Lerp(foot,crown,level);
                    var fork=origin+(outward*Range(.13f,.27f)+Vector3.up*Range(.08f,.22f))*h;
                    Stem(origin,fork,h*.015f,.025f);
                    for (int twig=-1;twig<=1;twig+=2)
                    {
                        Vector3 spread=Quaternion.Euler(0,twig*Range(26,51),0)*outward;
                        Vector3 tip=fork+(spread*Range(.08f,.17f)+Vector3.up*Range(.10f,.27f))*h;
                        Stem(fork,tip,.037f,.006f);
                        if(twig==1 && branch%2==0)
                            crowns.Add((tip,Range(.13f,.24f)*h,Range(.19f,.29f)*h));
                    }
                }
            }
        }
        void Stem(Vector3 a,Vector3 b,float r0,float r1)
        {
            Vector3 axis=(b-a).normalized;
            Vector3 u=Vector3.Cross(axis,Vector3.right).normalized;
            Vector3 v=Vector3.Cross(axis,u);
            int n=vertices.Count;
            for(int k=0;k<4;k++)
            {
                float ang=k*Mathf.PI*.5f;
                Vector3 radial=u*Mathf.Cos(ang)+v*Mathf.Sin(ang);
                vertices.Add(a+radial*r0);vertices.Add(b+radial*r1);
                uv.Add(Vector2.zero);uv.Add(Vector2.one);
                int start=n+k*2,next=n+(k+1)%4*2;
                faces.AddRange(new[]{start,next,next+1,start,next+1,start+1});
            }
        }
        string path=Folder+"Distant alders.mat";
        var leaves=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(leaves==null)
        {
            leaves=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(leaves,path);
        }
        leaves.SetColor("_BaseColor",new Color(.19f,.22f,.16f));
        leaves.SetFloat("_Smoothness",.09f);
        EditorUtility.SetDirty(leaves);
        SaveMesh(root,"Distant alders",vertices,uv,faces,leaves);
        vertices.Clear(); faces.Clear(); uv.Clear();
        foreach(var (centre,width,height) in crowns)
        {
            int first=vertices.Count;
            float angle=Range(0,6.28f);
            for(int band=0;band<4;band++)
            {
                float up=band/3f;
                float radius=width*(band==0?.14f:band==3?.16f:1f);
                for(int n=0;n<8;n++)
                {
                    float a=angle+n*Mathf.PI/4+band*.18f;
                    float r=radius*Range(.73f,1.18f);
                    vertices.Add(centre+new Vector3(Mathf.Cos(a)*r,(up-.46f)*height+Range(-.12f,.12f),Mathf.Sin(a)*r*.80f));
                    uv.Add(new Vector2(n/8f,up));
                    if(band==0)continue;
                    int i=first+(band-1)*8+n,j=first+(band-1)*8+(n+1)%8;
                    faces.AddRange(new[]{i,i+8,j+8,i,j+8,j});
                }
            }
        }
        string foliagePath=Folder+"Willow canopy.mat";
        var foliage=AssetDatabase.LoadAssetAtPath<Material>(foliagePath);
        if(foliage==null)
        {
            foliage=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(foliage,foliagePath);
        }
        foliage.SetColor("_BaseColor",new Color(.26f,.31f,.19f));
        foliage.SetFloat("_Smoothness",.18f);
        EditorUtility.SetDirty(foliage);
        SaveMesh(root,"Willow canopies",vertices,uv,faces,foliage);
    }

    static void CreateSeepage(Transform root)
    {
        // Trace rain draining from the peat lips, using the same triangles the boots stand on.
        var verts=new List<Vector3>();
        var uvs=new List<Vector2>();
        var depth=new List<Color>();
        var triangles=new List<int>();
        Vector2[] heads={new(-37,-.9f),new(-31,-4),new(-27.5f,3.2f),new(-30,-19.6f),
            new(-37,-22),new(-16,2.3f),new(4,6.4f),new(12.4f,1.9f),new(18.1f,-11.8f)};
        bool Soil(Vector2 p,out Vector3 at)
        {
            at=default;
            if(!Physics.Raycast(new Vector3(p.x,14,p.y),Vector3.down,out var hit,22,~0,QueryTriggerInteraction.Ignore)
                || !hit.collider.name.StartsWith("Biebrza ground "))return false;
            at=hit.point;
            return true;
        }
        foreach(var head in heads)
        {
            if(!Soil(head,out var point))continue;
            var course=new List<Vector3>{point};
            for(int step=0;step<46;step++)
            {
                Vector3 lowest=point;
                for(int ray=0;ray<12;ray++)
                {
                    float angle=ray*Mathf.PI/6f+step*.077f;
                    var probe=new Vector2(point.x+.21f*Mathf.Cos(angle),point.z+.21f*Mathf.Sin(angle));
                    if(Soil(probe,out var next) && next.y<lowest.y-.0018f)lowest=next;
                }
                if(lowest.y>=point.y-.003f)break;
                point=lowest;course.Add(point);
                if(point.y<=-.382f)break;
            }
            if(course.Count<5)continue;
            int first=verts.Count;
            for(int i=0;i<course.Count;i++)
            {
                point=course[i];
                var direction=course[Mathf.Min(i+1,course.Count-1)]-course[Mathf.Max(0,i-1)];
                var side=new Vector2(direction.z,-direction.x).normalized;
                float along=i/(float)(course.Count-1);
                float width=(.046f+.065f*along+.022f*Mathf.Sin(i*1.7f))*Mathf.SmoothStep(.15f,1f,Mathf.Min(i,course.Count-1-i)/3f);
                for(int bank=-1;bank<=1;bank++)
                {
                    var sample=new Vector2(point.x,point.z)+side*width*bank;
                    if(!Soil(sample,out var surface))surface=point;
                    verts.Add(surface+Vector3.up*.006f);
                    uvs.Add(new Vector2((bank+1)*.5f,i*.21f));
                    depth.Add(new Color(bank==0?.72f:.03f,0,0,1));
                }
                if(i==0)continue;
                int a=first+(i-1)*3;
                triangles.AddRange(new[]{a,a+3,a+4,a,a+4,a+1,a+1,a+4,a+5,a+1,a+5,a+2});
            }
        }
        if(verts.Count>0)
            SaveMesh(root,"Rain runnels",verts,uvs,triangles,AssetDatabase.LoadAssetAtPath<Material>(Folder+"Water.mat"),depth);
    }

    static void SaveMesh(Transform parent,string name,List<Vector3> vertices,List<Vector2> uv,List<int> triangles,Material material,List<Color> colors=null)
    {
        string path=Folder+name+".asset";
        var asset=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(asset==null){asset=new Mesh();AssetDatabase.CreateAsset(asset,path);}
        asset.Clear();
        asset.indexFormat=vertices.Count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        asset.SetVertices(vertices);asset.SetUVs(0,uv);asset.SetTriangles(triangles,0);
        if(colors!=null)asset.SetColors(colors);
        asset.RecalculateNormals();asset.RecalculateTangents();asset.RecalculateBounds();
        EditorUtility.SetDirty(asset);
        var group=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        group.transform.SetParent(parent,false);
        group.GetComponent<MeshFilter>().sharedMesh=asset;
        var renderer=group.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
    }
}
