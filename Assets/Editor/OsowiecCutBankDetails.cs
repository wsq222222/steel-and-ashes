using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exposed peat lenses and fine root fans follow the exported landscape at each vertex.
public static class OsowiecCutBankDetails
{
    const string Folder = "Assets/Osowiec/Art/Fort1916/";
    const string MeshPath = Folder + "Exposed cut banks.asset";
    const string ScenePath = OsowiecProjectPaths.OperationScene;
    const string Label = "Осевший торф и обнажённые корни";

    sealed class Tile
    {
        readonly MeshCollider shape;
        readonly int[] triangles;
        readonly Color32[] colours;
        readonly Vector3[] normals;
        readonly Matrix4x4 normalBasis;
        public Tile(MeshCollider collider)
        {
            shape=collider;
            var mesh=shape.sharedMesh;
            triangles=mesh.triangles;
            colours=mesh.colors32;
            normals=mesh.normals;
            normalBasis=shape.transform.worldToLocalMatrix.transpose;
        }
        public bool Sample(Vector3 near,out Vector3 point,out Vector3 normal,out Color colour)
        {
            point=near; normal=Vector3.up; colour=Color.white;
            if(!shape.Raycast(new Ray(near + Vector3.up*2f,Vector3.down),out var h,4f)) return false;
            point=h.point;
            int t=h.triangleIndex*3;
            int a=triangles[t], b=triangles[t+1], c=triangles[t+2];
            Vector3 w=h.barycentricCoordinate;
            normal=normalBasis.MultiplyVector(normals[a]*w.x+normals[b]*w.y+normals[c]*w.z).normalized;
            if(colours.Length>0) colour=(Color)colours[a]*w.x+(Color)colours[b]*w.y+(Color)colours[c]*w.z;
            return true;
        }
    }

    public static void Bake(Transform parent,Material sod,Material bark)
    {
        Physics.SyncTransforms();
        var points=new List<Vector3>(); var uv=new List<Vector2>();
        var colours=new List<Color>(); var bankNormals=new List<Vector3>();
        var soil=new List<int>(); var roots=new List<int>();
        var tiles=new Dictionary<MeshCollider,Tile>();
        var random=new System.Random(160815);
        float Range(float lo,float hi) => Mathf.Lerp(lo,hi,(float)random.NextDouble());
        int banks=0, strands=0;
        Material ground=null;

        void Face(List<int> to,int a,int b,int c) { to.Add(a); to.Add(b); to.Add(c); }
        int Vertex(Vector3 p,Color colour,Vector2 map,Vector3 normal)
        {
            int i=points.Count;
            points.Add(p); colours.Add(colour); uv.Add(map); bankNormals.Add(normal);
            return i;
        }

        // A single irregular lens. The rim is below the actual cut, even where the bank bends.
        bool Lens(Tile tile,Vector3 origin,Vector3 n,Vector3 side,Vector3 down,float width,float height)
        {
            const int sectors=13, rings=4;
            var samples=new Vector3[1+sectors*rings]; var bases=new Vector3[samples.Length];
            var masking=new Color[samples.Length];
            float tilt=Range(-.21f,.21f), phase=Range(0,6.28f), relief=Range(.022f,.048f);
            var along=(side+down*tilt).normalized;
            var across=Vector3.Cross(n,along).normalized;
            if(Vector3.Dot(across,down)<0) across=-across;
            for(int i=0;i<samples.Length;i++)
            {
                int k=i==0 ? 0 : (i-1)%sectors;
                float rho=i==0 ? 0 : (.25f+.25f*((i-1)/sectors));
                float phi=2*Mathf.PI*k/sectors;
                float outline=1f+.105f*Mathf.Sin(3*phi+phase)+.075f*Mathf.Cos(5*phi+phase);
                float x=Mathf.Cos(phi)*rho*outline, y=Mathf.Sin(phi)*rho*outline;
                var target=origin+along*(width*x)+across*(height*y);
                if(!tile.Sample(target,out var p,out var norm,out var mask)) return false;
                float raised=(1f-rho*rho)*(1f+.23f*x-.22f*y+.23f*Mathf.Sin(phi*2+phase)*rho);
                samples[i]=p+norm*(relief*raised-.006f);
                bases[i]=norm; masking[i]=mask;
            }
            int first=points.Count;
            for(int i=0;i<samples.Length;i++)
                Vertex(samples[i],masking[i],new Vector2(samples[i].x,samples[i].z),bases[i]);
            for(int k=0;k<sectors;k++)
            {
                int j=(k+1)%sectors;
                void BankFace(int ia,int ib,int ic)
                {
                    if(Vector3.Dot(Vector3.Cross(points[ib]-points[ia],points[ic]-points[ia]),n)<0)
                        (ib,ic)=(ic,ib);
                    Face(soil,ia,ib,ic);
                }
                BankFace(first,first+1+k,first+1+j);
                for(int r=1;r<rings;r++)
                {
                    int a=first+1+(r-1)*sectors;
                    BankFace(a+k,a+sectors+k,a+sectors+j);
                    BankFace(a+k,a+sectors+j,a+j);
                }
            }
            banks++;
            return true;
        }

        // Partly buried, tapering strands; root forks leave the parent strand at the exact same point.
        void Root(Tile tile,Vector3 start,Vector3 run,Vector3 cross,float length,float radius,float bend,bool fork)
        {
            const int steps=7, sides=7;
            var axis=new Vector3[steps]; var normals=new Vector3[steps];
            float twist=Range(-.012f,.012f);
            for(int i=0;i<steps;i++)
            {
                float t=i/(float)(steps-1);
                var near=start+run*(length*t)+cross*(bend*t*t+twist*Mathf.Sin(t*Mathf.PI*2));
                if(!tile.Sample(near,out var p,out normals[i],out _)) return;
                float lift=Mathf.Sin(t*Mathf.PI)*radius*.85f;
                if(i==0) lift=fork ? radius*.38f : -radius*.5f;
                if(i==steps-1) lift=-radius*.45f;
                axis[i]=p+normals[i]*lift;
            }
            int first=points.Count;
            for(int i=0;i<steps;i++)
            {
                float t=i/(float)(steps-1);
                Vector3 tangent=(axis[Mathf.Min(steps-1,i+1)]-axis[Mathf.Max(0,i-1)]).normalized;
                Vector3 x=Vector3.ProjectOnPlane(normals[i],tangent).normalized, y=Vector3.Cross(tangent,x);
                float r=radius*(1f-.92f*t);
                for(int k=0;k<sides;k++)
                {
                    float ang=k*2*Mathf.PI/sides;
                    Vector3 radial=x*Mathf.Cos(ang)+y*Mathf.Sin(ang);
                    Vertex(axis[i]+radial*r,Color.white,new Vector2(k/(float)sides,t*length*3f),radial);
                }
                if(i==0)continue;
                for(int k=0;k<sides;k++)
                {
                    int a=first+(i-1)*sides+k,b=first+(i-1)*sides+(k+1)%sides;
                    Face(roots,a,b,a+sides); Face(roots,b,b+sides,a+sides);
                }
            }
            strands++;
            if(!fork && radius>.0085f)
                Root(tile,axis[3],(run*.69f+cross*(bend>0 ? -.65f:.65f)).normalized,cross,length*.46f,radius*.42f,-bend*.4f,true);
        }

        for(int patch=0;patch<4200;patch++)
        {
            float x=Range(-49,23),z=Range(-23,17);
            if(!Physics.Raycast(new Vector3(x,12,z),Vector3.down,out var hit,23,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
            if(hit.collider is not MeshCollider mc || !mc.sharedMesh || !mc.sharedMesh.name.StartsWith("Ground_",StringComparison.Ordinal)
                || !mc.transform.IsChildOf(parent))continue;
            if(hit.normal.y<.22f || hit.normal.y>.77f || hit.point.y<.30f || Mathf.PerlinNoise(x*.36f+18,z*.36f+28)<.52f)continue;
            if(!tiles.TryGetValue(mc,out var tile)) tiles[mc]=tile=new Tile(mc);
            if(!tile.Sample(hit.point,out var p,out var n,out _))continue;
            ground ??=mc.GetComponent<MeshRenderer>().sharedMaterial;
            Vector3 side=Vector3.Cross(n,Vector3.up).normalized, downhill=Vector3.ProjectOnPlane(Vector3.down,n).normalized;
            float width=Range(.14f,.33f), height=Range(.065f,.16f);
            if(!Lens(tile,p,n,side,downhill,width,height)) continue;
            int count=random.Next(1,4);
            for(int i=0;i<count;i++)
            {
                Vector3 neck=p+side*Range(-width*.67f,width*.67f)+downhill*(height*.52f);
                Root(tile,neck,downhill,side,Range(.14f,.36f),Range(.006f,.013f),Range(-.095f,.095f),false);
            }
        }
        if(banks==0 || !ground) throw new InvalidOperationException("No surveyed terrain for cut-bank dressing.");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if(!mesh){mesh=new Mesh{name="Exposed cut banks"};AssetDatabase.CreateAsset(mesh,MeshPath);}
        mesh.Clear();mesh.indexFormat=points.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(points);mesh.SetColors(colours);mesh.SetUVs(0,uv);
        mesh.subMeshCount=2;mesh.SetTriangles(soil,0);mesh.SetTriangles(roots,1);
        mesh.RecalculateNormals(); var shading=mesh.normals;
        foreach(int idx in new HashSet<int>(soil)) shading[idx]=Vector3.Slerp(bankNormals[idx],shading[idx],.58f).normalized;
        mesh.normals=shading;mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
        var item=parent.Find(Label)?.gameObject;
        if(!item)
        {
            item=new GameObject(Label);
            item.transform.SetParent(parent,true);
            item.AddComponent<MeshFilter>();item.AddComponent<MeshRenderer>();
            item.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
        }
        item.GetComponent<MeshFilter>().sharedMesh=mesh;
        item.GetComponent<MeshRenderer>().sharedMaterials=new[]{ground,RootMaterial(bark)};
        OsowiecBankLitter.Bake(parent);
        Debug.Log($"Osowiec cut banks: {banks} grounded lenses, {strands} root strands, {points.Count} vertices.");
    }

    static Material RootMaterial(Material bark)
    {
        const string path=Folder+"Exposed root fibre.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(bark);AssetDatabase.CreateAsset(mat,path);}
        mat.CopyPropertiesFromMaterial(bark);
        mat.SetColor("_BaseColor",new Color(1.48f,1.32f,1.13f));
        mat.SetFloat("_BumpScale",.50f);
        mat.SetFloat("_Smoothness",.29f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Rebuild this mesh and its two renderer slots without regenerating the operation or navigation.
    public static void RefreshExisting()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        bool opened=!scene.isLoaded;
        if(opened) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            foreach(var root in scene.GetRootGameObjects())
            {
                var field=root.GetComponentInChildren<OsowiecGroundEvents>(true);
                if(!field)continue;
                Bake(field.transform,field.debrisMaterial,AssetDatabase.LoadAssetAtPath<Material>(Folder+"Bark.mat"));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                return;
            }
            throw new InvalidOperationException("The existing mission has no ground dressing root.");
        }
        finally {if(opened) EditorSceneManager.CloseScene(scene,true);}
    }
}
