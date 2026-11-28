using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OsowiecArchitectureDetails
{
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    const string Materials = Art + "Surfaces/";
    const string Arch = "Камень портала и пят свода";
    const string Walls = "Оголённая кладка и бетон двора";
    const string Wood = "Тёсаный брус и затёсы стен";

    // Hard faces keep individual chipped corners visible when a lamp grazes an edge.
    sealed class Geometry
    {
        readonly List<Vector3> vertices = new();
        readonly List<Vector2> uv = new();
        readonly List<int>[] indices;
        readonly Material[] materials;
        public Geometry(params Material[] materials)
        {
            this.materials = materials;
            indices = materials.Select(_ => new List<int>()).ToArray();
        }

        public void Face(int material, Vector3[] ps, Vector2[] coords, Vector3 outward)
        {
            int start = vertices.Count;
            bool reverse = Vector3.Dot(Vector3.Cross(ps[1]-ps[0],ps[2]-ps[0]),outward)<0;
            vertices.AddRange(ps);
            uv.AddRange(coords);
            for(int i=1;i<ps.Length-1;i++)
            {
                indices[material].Add(start);
                indices[material].Add(start+(reverse?i+1:i));
                indices[material].Add(start+(reverse?i:i+1));
            }
        }

        public void StoneFace(int material, Vector3[] ps, Vector3 outward, float tile=1.6f)
        {
            Vector3 n=outward.normalized;
            Vector3 u=Mathf.Abs(n.y)>.8f ? Vector3.right : Vector3.Cross(Vector3.up,n).normalized;
            Vector3 v=Vector3.Cross(n,u);
            Face(material,ps,ps.Select(p=>new Vector2(Vector3.Dot(p,u),Vector3.Dot(p,v))/tile).ToArray(),n);
        }

        public Renderer Save(Transform parent,string objectName,string assetName)
        {
            var mesh=Store(assetName,()=>new Mesh());
            mesh.Clear();
            mesh.indexFormat=vertices.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.subMeshCount=indices.Length;
            for(int i=0;i<indices.Length;i++)mesh.SetTriangles(indices[i],i);
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var child=parent.Find(objectName);
            if(!child)
            {
                child=new GameObject(objectName,typeof(MeshFilter),typeof(MeshRenderer),typeof(NavMeshModifier)).transform;
                child.SetParent(parent,false);
            }
            child.GetComponent<MeshFilter>().sharedMesh=mesh;
            child.GetComponent<NavMeshModifier>().ignoreFromBuild=true;
            var renderer=child.GetComponent<MeshRenderer>();renderer.sharedMaterials=materials;
            return renderer;
        }
    }

    static Mesh Store(string assetName,Func<Mesh> create)
    {
        string path=Art+assetName+".asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh){mesh=create();mesh.name=assetName;AssetDatabase.CreateAsset(mesh,path);}
        return mesh;
    }

    static float Between(System.Random rng,float lo,float hi)=>
        Mathf.Lerp(lo,hi,(float)rng.NextDouble());

    static void Stone(Geometry g,int mat,Vector3 center,Vector3 u,Vector3 v,Vector3 outward,
        float width,float height,float depth,System.Random rng)
    {
        // Octagonal broken arris and a second bevel ring; no rectilinear front perimeter.
        float a=width/2, b=height/2, cut=Mathf.Min(a,b);
        float cA=Between(rng,.12f,.53f)*cut, cB=Between(rng,.09f,.37f)*cut;
        float cC=Between(rng,.08f,.29f)*cut, cD=Between(rng,.10f,.41f)*cut;
        Vector2[] outline={new(-a+cA,-b),new(a-cB,-b),new(a,-b+cB),new(a,b-cC),
            new(a-cC,b),new(-a+cD,b),new(-a,b-cD),new(-a,-b+cA)};
        var back=new Vector3[8];var lip=new Vector3[8];var face=new Vector3[8];
        for(int k=0;k<8;k++)
        {
            var p=outline[k];
            var planar=u*p.x+v*p.y;
            back[k]=center+planar-outward*.010f;
            lip[k]=center+planar+outward*depth*.67f;
            float bevel=Between(rng,.003f,.010f);
            face[k]=center+u*(p.x-Mathf.Sign(p.x)*bevel)+v*(p.y-Mathf.Sign(p.y)*bevel)+outward*depth;
        }
        g.StoneFace(mat,face,outward,.49f);
        for(int k=0;k<8;k++)
        {
            int l=(k+1)%8;
            Vector3 side=((lip[k]+lip[l])*.5f-center).normalized;
            g.StoneFace(mat,new[]{back[k],back[l],lip[l],lip[k]},side,.49f);
            g.StoneFace(mat,new[]{lip[k],lip[l],face[l],face[k]},side+outward,.49f);
        }
    }

    static void Vault(Geometry g,float x,float radius,float outer,float halfDepth,bool portal,System.Random rng)
    {
        // Interior ribs and the outer eastern arch use separate barrel, soffit and cheek faces.
        int count=portal?25:19;
        for(int k=0;k<count;k++)
        {
            float a=Mathf.PI*(k+.026f)/count,b=Mathf.PI*(k+.971f)/count;
            float worn=Between(rng,-.016f,.014f);
            if(k==2 || k==count-6)worn+=.025f;
            float inside=radius+worn;
            float outside=outer+Between(rng,-.010f,.012f);
            float d=halfDepth+Between(rng,-.009f,.015f);
            float spring=portal?1.85f:1.80f;
            Vector3 Pt(float xx,float r,float angle) => new(xx,spring+r*Mathf.Sin(angle),-12+r*Mathf.Cos(angle));
            float m=(a+b)*.5f;
            Vector3 radial=new(0,Mathf.Sin(m),Mathf.Cos(m));
            int mat=portal?(k%8==3?1:0):(k%7==2?0:1);
            for(int sign=-1;sign<=1;sign+=2)
            {
                float front=x+sign*d;
                var outline=new[]{Pt(front,inside+.020f,a),Pt(front,inside+.005f,a+.014f),Pt(front,inside+.005f,b-.012f),
                    Pt(front,inside+.025f,b),Pt(front,outside-.023f,b),Pt(front,outside,b-.010f),
                    Pt(front,outside,a+.011f),Pt(front,outside-.021f,a)};
                g.StoneFace(mat,outline,Vector3.right*sign,portal?1.4f:.8f);
                var lip=new[]{Pt(front,inside+.005f,a+.014f),Pt(front,inside+.005f,b-.012f),
                    Pt(front-sign*.018f,inside-.007f,b-.008f),Pt(front-sign*.018f,inside-.007f,a+.009f)};
                g.StoneFace(mat,lip,Vector3.right*sign-radial,.8f);
            }
            g.StoneFace(mat,new[]{Pt(x-d+.017f,inside-.007f,a+.009f),Pt(x+d-.017f,inside-.007f,a+.009f),
                Pt(x+d-.017f,inside-.007f,b-.008f),Pt(x-d+.017f,inside-.007f,b-.008f)},-radial,.8f);
        }
    }

    static void Patch(Geometry g,float x,float y,int side,System.Random rng)
    {
        // Lime bedding is visible only in a damaged pocket, with chipped headers set at varying depths.
        Vector3 outward=Vector3.back*side, along=Vector3.right;
        Vector3 origin=new(x,y,-12+side*1.701f);
        int[] lengths={3,5,6,6,4,2};
        for(int row=0;row<lengths.Length;row++)
        {
            int length=lengths[row];
            float left=-length*.134f+(row%2)*.072f;
            for(int i=0;i<length;i++)
            {
                float centre=left+i*.266f;
                var point=origin+along*centre+Vector3.up*(row*.111f);
                int mat=(i==0 && row>1) || (row==0 && i==1) ? 1 : 0;
                Stone(g,mat,point,along,Vector3.up,outward,Between(rng,.243f,.258f),Between(rng,.095f,.103f),
                    mat==1?Between(rng,.008f,.019f):Between(rng,.025f,.046f),rng);
            }
        }
    }

    static readonly Vector2[] Emplacements={new(32,9),new(40,-8)};

    static void ConcreteRim(Geometry g,Vector2 centre,int offset)
    {
        const int segments=144;
        Vector3[] outer=new Vector3[segments],face=new Vector3[segments],top=new Vector3[segments],back=new Vector3[segments];
        float Damage(float angle)
        {
            float result=0;
            foreach(float chip in new[]{-.61f, .37f,1.79f,2.77f,4.47f})
            {
                float d=Mathf.Abs(Mathf.DeltaAngle(angle*Mathf.Rad2Deg,(chip+offset*.45f)*Mathf.Rad2Deg))*.0174533f;
                result+=Mathf.Pow(Mathf.Clamp01(1-d/.15f),1.3f);
            }
            return Mathf.Clamp01(result);
        }
        for(int i=0;i<segments;i++)
        {
            float a=2*Mathf.PI*i/segments;
            float scar=Damage(a),grain=Mathf.PerlinNoise(i*.9f+offset,7f);
            Vector3 n=new(Mathf.Cos(a),0,Mathf.Sin(a)),basePoint=new(centre.x,0,centre.y);
            outer[i]=basePoint+n*(3.495f-.028f*scar)+Vector3.up*.02f;
            face[i]=basePoint+n*(3.496f-.033f*scar)+Vector3.up*(.226f-.158f*scar);
            top[i]=basePoint+n*(3.461f-.139f*scar-.018f*grain)+Vector3.up*(.28f-.034f*scar);
            back[i]=basePoint+n*2.8f+Vector3.up*.28f;
        }
        for(int i=0;i<segments;i++)
        {
            int j=(i+1)%segments;
            Vector3 n=((outer[i]+outer[j])*.5f-new Vector3(centre.x,0,centre.y)).normalized;
            g.StoneFace(1,new[]{outer[i],outer[j],face[j],face[i]},n,1.9f);
            g.StoneFace(1,new[]{face[i],face[j],top[j],top[i]},n+Vector3.up,.64f);
            g.StoneFace(1,new[]{top[i],top[j],back[j],back[i]},Vector3.up,1.9f);
            g.StoneFace(1,new[]{back[i],back[j],back[j]-Vector3.up*.26f,back[i]-Vector3.up*.26f},-n,1.9f);
        }
    }

    static void RetainBatteryMount(Transform parent)
    {
        var mount=parent.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Battery_statuary");
        // The original collider retains the FBX for navigation and all subsequent rebakes.
        var original=mount.GetComponent<MeshCollider>().sharedMesh;
        var source=original.vertices; var inRing=new bool[source.Length];
        for(int i=0;i<source.Length;i++)
        {
            Vector3 p=mount.transform.TransformPoint(source[i]);
            foreach(var centre in Emplacements)
            {
                float r=Vector2.Distance(new Vector2(p.x,p.z),centre);
                if(p.y>=.018f && p.y<=.282f && (Mathf.Abs(r-3.50f)<.006f || Mathf.Abs(r-2.80f)<.006f))inRing[i]=true;
            }
        }
        var mesh=Store("Battery iron and centre drums",()=>UnityEngine.Object.Instantiate(original));
        int removed=0;
        for(int m=0;m<original.subMeshCount;m++)
        {
            var triangles=original.GetTriangles(m); var kept=new List<int>(triangles.Length);
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                if(inRing[a] && inRing[b] && inRing[c]){removed++;continue;}
                kept.Add(a);kept.Add(b);kept.Add(c);
            }
            mesh.SetTriangles(kept,m);
        }
        if(removed==0)throw new InvalidOperationException("Could not locate the surveyed concrete plinth rims.");
        mount.sharedMesh=mesh;EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(mount);
    }

    public static Renderer BakeFort(Transform root)
    {
        var materials=new Dictionary<string,Material>();
        var clay=OsowiecEnvironment.Resolve("Brick core",materials);
        var concrete=OsowiecEnvironment.Resolve("Concrete",materials);
        OsowiecEnvironment.Resolve("Brick",materials);
        var rng=new System.Random(4316);
        var vault=new Geometry(clay,concrete);
        foreach(float x in new[]{-6.15f,-2.70f,4.25f,7.68f}) Vault(vault,x,1.604f,1.82f,.157f,false,rng);
        Vault(vault,10.4f,1.704f,2.22f,.299f,true,rng);
        var canopy=vault.Save(root,Arch,"Dressed vault and east portal");
        var wall=new Geometry(clay,concrete);
        Patch(wall,-4.78f,.64f,-1,rng);Patch(wall,-1.52f,.84f,1,rng);Patch(wall,5.53f,.57f,1,rng);
        // The eastern cheek is the exposed terminal of the brick arch. Interrupted courses
        // reveal individual snapped ends on both sides of the return.
        foreach(int side in new[]{-1,1})
        {
            for(int row=0;row<14;row++)
            {
                int count=row<4?3:row<9?4:2;
                for(int i=0;i<count;i++)
                {
                    float across=2.10f+.268f*i+(row%2)*.12f;
                    var at=new Vector3(10.805f,.39f+row*.148f,-12+side*across);
                    Stone(wall,(i+row)%11==4?1:0,at,Vector3.forward,Vector3.up,Vector3.right,
                        Between(rng,.24f,.263f),Between(rng,.133f,.141f),Between(rng,.018f,.066f),rng);
                }
            }
        }
        RetainBatteryMount(root);
        for(int i=0;i<Emplacements.Length;i++)ConcreteRim(wall,Emplacements[i],i);
        wall.Save(root,Walls,"Patched masonry and spalled plinths");
        return canopy;
    }

    internal static Material SawnTimber()
    {
        const string path=Art+"Adzed timber.mat";
        var shader=Shader.Find("Steel and Ashes/Seeping masonry");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
        else mat.shader=shader;
        foreach(var maps in new[]{("_BaseMap","Timber_colour.jpg"),("_BumpMap","Timber_normal.jpg"),("_MetallicGlossMap","Timber_mask.png")})
            mat.SetTexture(maps.Item1,AssetDatabase.LoadAssetAtPath<Texture2D>(Materials+maps.Item2));
        mat.SetColor("_BaseColor",new Color(.64f,.56f,.47f));
        mat.SetFloat("_BumpScale",1.02f);mat.SetFloat("_Smoothness",.41f);mat.SetFloat("_ReliefUV",.0038f);mat.SetFloat("_Finish",2f);
        mat.enableInstancing=true;EditorUtility.SetDirty(mat);
        return mat;
    }

    static void Timber(Geometry g,Transform root,Transform source,int beam,System.Random rng)
    {
        Vector3 size=source.localScale;
        int longitudinal=size.x>size.y && size.x>size.z?0:size.y>size.z?1:2;
        int width=(longitudinal+1)%3, height=(longitudinal+2)%3;
        float len=size[longitudinal],a=size[width]*.5f,b=size[height]*.5f;
        float chamfer=Mathf.Min(a,b)*Between(rng,.19f,.37f);
        Vector2[] profile={new(-a+chamfer,-b),new(a-chamfer,-b),new(a,-b+chamfer),new(a,b-chamfer),
            new(a-chamfer,b),new(-a+chamfer,b),new(-a,b-chamfer),new(-a,-b+chamfer)};
        int sections=Mathf.Max(4,Mathf.CeilToInt(len/.45f));
        float baseU=new[]{.055f,.146f,.262f,.382f,.476f,.592f,.731f,.883f}[beam%8];
        float baseV=Between(rng,0,1);
        var rings=new Vector3[sections+1,8];
        var orientation=Quaternion.Inverse(root.rotation)*source.rotation;
        var centre=root.InverseTransformPoint(source.position);
        Vector3 Axis(int dimension){var v=Vector3.zero;v[dimension]=1;return orientation*v;}
        var uAxis=Axis(width);var vAxis=Axis(height);var along=Axis(longitudinal);
        for(int j=0;j<=sections;j++)
        {
            float t=(float)j/sections;
            for(int k=0;k<8;k++)
            {
                var p=profile[k];
                float notch=(j>0 && j<sections && (k+j*13+beam*11)%17==0)?Between(rng,.009f,.027f):0;
                p.x-=Mathf.Sign(p.x)*(notch+Between(rng,0,.0033f));
                p.y-=Mathf.Sign(p.y)*(notch*.54f+Between(rng,0,.0028f));
                rings[j,k]=centre+along*((t-.5f)*len)+uAxis*p.x+vAxis*p.y;
            }
        }
        // Select a single photographed stave. Grain follows the length on each face.
        // Adjacent wall boards deliberately use different portions of the same scan.
        for(int j=0;j<sections;j++)for(int k=0;k<8;k++)
        {
            int l=(k+1)%8;
            var n=(uAxis*(profile[k].x+profile[l].x)+vAxis*(profile[k].y+profile[l].y)).normalized;
            float u0=baseU+(k%3)*.0015f;
            float u1=u0+Mathf.Min(.061f,Vector2.Distance(profile[k],profile[l])*.25f);
            float v0=baseV+j*len/(1.82f*sections),v1=baseV+(j+1)*len/(1.82f*sections);
            g.Face(0,new[]{rings[j,k],rings[j,l],rings[j+1,l],rings[j+1,k]},
                new[]{new Vector2(u0,v0),new Vector2(u1,v0),new Vector2(u1,v1),new Vector2(u0,v1)},n);
        }
        for(int end=0;end<2;end++)
        {
            int j=end*sections;var polygon=new Vector3[8]; var coords=new Vector2[8];
            for(int k=0;k<8;k++)
            {
                polygon[k]=rings[j,k]; var p=profile[k];
                coords[k]=new Vector2(baseU+p.x*.31f,baseV+p.y*.52f);
            }
            g.Face(0,polygon,coords,end==0?-along:along);
        }
    }

    public static void BakeHQ(Transform room)
    {
        var g=new Geometry(SawnTimber());var rng=new System.Random(19158);int beam=0;
        foreach(Transform item in room)
        {
            if(item.name!="Joist" && item.name!="Wall buttress" && item.name!="Timber shoring")continue;
            Timber(g,room,item,beam++,rng);
            var renderer=item.GetComponent<MeshRenderer>();
            renderer.enabled=false;EditorUtility.SetDirty(renderer);
        }
        if(beam==0)throw new InvalidOperationException("No headquarters timbers were found.");
        g.Save(room,Wood,"Hewn dugout structure");
    }

    [MenuItem("Steel & Ashes/Refresh architectural surfaces and silhouettes")]
    public static void RefreshScenes()
    {
        var active=SceneManager.GetActiveScene();
        void PatchScene(string path,Action<Scene> edit)
        {
            var scene=SceneManager.GetSceneByPath(path);
            bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            edit(scene);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            if(opened)EditorSceneManager.CloseScene(scene,true);
        }
        PatchScene(OsowiecProjectPaths.OperationScene,scene=>
        {
            var cut=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<OsowiecFortCutaway>(true)).Single();
            var stone=BakeFort(cut.transform);
            cut.surfaces=cut.surfaces.Where(r=>r && r!=stone).Append(stone).ToArray();EditorUtility.SetDirty(cut);
        });
        PatchScene(OsowiecProjectPaths.HeadquartersScene,scene=>
        {
            var room=scene.GetRootGameObjects().FirstOrDefault(go=>go.name=="Timber signal dugout");
            if(room) BakeHQ(room.transform); // Only the legacy timber room uses the procedural bake.
        });
        if(active.isLoaded)SceneManager.SetActiveScene(active);
        AssetDatabase.SaveAssets();
        Debug.Log("Osowiec architecture baked into fort and headquarters.");
    }
}
