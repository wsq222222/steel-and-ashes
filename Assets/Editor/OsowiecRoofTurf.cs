using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Bake the upper turf from its actual roof triangles. All decorative stalks share one mesh.
public static class OsowiecRoofTurf
{
    const string Art="Assets/Osowiec/Art/Fort1916/";
    const string TurfName="Северный вал · островки мокрой овсяницы";
    static T Asset<T>(string file) where T:UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Art+file);

    static Material WithSoil(string name,string baseName,string keyword)
    {
        var material=Asset<Material>(name+".mat");
        if(!material) { material=new Material(Shader.Find("Steel and Ashes/Seeping masonry")); AssetDatabase.CreateAsset(material,Art+name+".mat"); }
        var basis=Asset<Material>(baseName+".mat");
        material.CopyPropertiesFromMaterial(basis);
        material.shader=Shader.Find("Steel and Ashes/Seeping masonry");
        material.shaderKeywords=Array.Empty<string>();
        material.EnableKeyword(keyword);
        foreach(var map in new[]{("_SoilColour","colour.jpg"),("_SoilNormal","normal.jpg"),("_SoilMask","mask.png")})
            material.SetTexture(map.Item1,Asset<Texture2D>("Surfaces/Earth_"+map.Item2));
        if(baseName=="Sod")
        {
            material.SetColor("_BaseColor",new Color(.65f,.66f,.52f));
            material.SetFloat("_ReliefUV",.013f);
            material.SetFloat("_BumpScale",1.13f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Mesh AssetMesh(string label)
    {
        var mesh=Asset<Mesh>(label+".asset");
        if(mesh)mesh.Clear();
        else { mesh=new Mesh {name=label}; AssetDatabase.CreateAsset(mesh,Art+label+".asset"); }
        return mesh;
    }

    public static void Install(Transform root)
    {
        var old=root.Find(TurfName);
        if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var filters=root.GetComponentsInChildren<MeshFilter>(true);
        var floor=filters.Single(f=>f.name=="Casemate brick floor").GetComponent<MeshRenderer>();
        floor.sharedMaterial=WithSoil("Casemate trodden paving","Brick","_EARTH_APRON");
        var turf=WithSoil("North rampart turf","Sod","_EARTH_ROOF");
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var colours=new List<Color32>();var triangles=new List<int>();
        var heads=new List<int>(); int tussocks=0;
        var rng=new System.Random(657);
        float R()=>(float)rng.NextDouble();
        void Plant(Vector3 foot,float scale)
        {
            tussocks++;
            float phase=R()*Mathf.PI*2;
            int blades=5+(int)(6*R());
            for(int j=0;j<blades;j++)
            {
                float a=phase+j*2.40f+R()*.85f;
                var outwards=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var side=Vector3.Cross(Vector3.up,outwards);
                var basePoint=foot+outwards*(.04f+.12f*R())-Vector3.up*.012f;
                float height=scale*(.44f+.75f*R()),width=.004f+.010f*R();
                float droop=.19f+.64f*R();
                var bend=outwards*(height*(.44f+.52f*R()))+Vector3.right*(.035f+.058f*R());
                int n=vertices.Count;
                for(int row=0;row<4;row++)
                {
                    float t=row/3f;
                    var spine=basePoint+Vector3.up*(height*t*(1-droop*t*t))+bend*(t*t);
                    float w=width*(row==3?.035f:row==2?.61f:row==1?1f:.76f);
                    vertices.Add(spine-side*w);vertices.Add(spine+outwards*w*.42f);vertices.Add(spine+side*w);
                    for(int k=0;k<3;k++){uv.Add(new Vector2(k*.5f,t));colours.Add(new Color(t,1,1,1));}
                    if(row==0)continue;
                    int p=n+(row-1)*3;
                    for(int k=0;k<2;k++)triangles.AddRange(new[]{p+k,p+k+3,p+k+1,p+k+1,p+k+3,p+k+4});
                }
            }
            // The photographed seed-head atlas breaks up the profile of the folded grass.
            // Arch both crossed cards downwind, keeping their roots on the sampled roof.
            if(R()>.68f)return;
            int tile=R()<.45f?1:2;
            for(int wing=0;wing<2;wing++)
            {
                float a=phase+wing*1.52f;
                var across=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(.12f+scale*.35f);
                int n=vertices.Count;
                for(int row=0;row<3;row++)
                {
                    float t=row*.5f;
                    var mid=foot+Vector3.up*(scale*1.53f*t-.023f)+Vector3.right*(.13f*t*t);
                    vertices.Add(mid-across);vertices.Add(mid+across);
                    float v=(1-tile/2+.02f+t*.96f)*.5f;
                    uv.Add(new Vector2((tile%2+.02f)*.5f,v));uv.Add(new Vector2((tile%2+.98f)*.5f,v));
                    colours.Add(new Color(t,1,1,1));colours.Add(new Color(t,1,1,1));
                    if(row>0){int k=n+(row-1)*2;heads.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});}
                }
            }
        }
        foreach(var f in filters.Where(f=>f.name=="Fort_crown"||f.name=="Fort_upper_battery"))
        {
            var renderer=f.GetComponent<MeshRenderer>();var materials=renderer.sharedMaterials;var mesh=f.sharedMesh;
            var v=mesh.vertices;
            for(int sub=0;sub<materials.Length;sub++)
            {
                if(materials[sub].name!="Sod" && materials[sub]!=turf)continue;
                materials[sub]=turf;
                var indices=mesh.GetTriangles(sub);
                for(int k=0;k<indices.Length;k+=3)
                {
                    var a=f.transform.TransformPoint(v[indices[k]]);var b=f.transform.TransformPoint(v[indices[k+1]]);var c=f.transform.TransformPoint(v[indices[k+2]]);
                    var cross=Vector3.Cross(b-a,c-a);
                    if(Mathf.Abs(cross.normalized.y)<.62f)continue;
                    float expected=cross.magnitude*1.35f;
                    int samples=Mathf.FloorToInt(expected+R());
                    for(int s=0;s<samples;s++)
                    {
                        float u=R(),w=R();if(u+w>1){u=1-u;w=1-w;}
                        var q=a+u*(b-a)+w*(c-a);
                        if(f.name=="Fort_crown" && (Vector2.Distance(new(q.x,q.z),new(34.8f,20.1f))<3.95f ||
                            Vector2.Distance(new(q.x,q.z),new(-2.5f,30.4f))<3.15f))continue;
                        float islands=Mathf.PerlinNoise(q.x*.61f+18,q.z*.73f+5);
                        if(islands<.36f || (islands<.54f && R()<.65f))continue;
                        Plant(q,.13f+.23f*islands+.07f*R());
                    }
                }
            }
            renderer.sharedMaterials=materials;
            EditorUtility.SetDirty(renderer);
        }
        if(vertices.Count<1000)throw new InvalidOperationException("Northern turf surfaces were not sampled.");
        var grass=AssetMesh("North rampart folded blades");grass.indexFormat=IndexFormat.UInt32;
        grass.SetVertices(vertices);grass.SetUVs(0,uv);grass.SetColors(colours);grass.subMeshCount=2;grass.SetTriangles(triangles,0);grass.SetTriangles(heads,1);grass.RecalculateNormals();grass.RecalculateBounds();
        EditorUtility.SetDirty(grass);
        var plant=new GameObject(TurfName);plant.transform.SetParent(root,false);
        plant.AddComponent<MeshFilter>().sharedMesh=grass;
        var mr=plant.AddComponent<MeshRenderer>();mr.sharedMaterials=new[]{Asset<Material>("Sedge.mat"),Asset<Material>("Grass.mat")};mr.shadowCastingMode=ShadowCastingMode.Off;
        plant.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        EditorUtility.SetDirty(floor);
        Debug.Log("North rampart grass clumps: "+tussocks);
    }
    public static void BakeCurrentScene()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Osowiec/Osowiec.unity")scene=EditorSceneManager.OpenScene("Assets/Osowiec/Osowiec.unity");
        Install(GameObject.Find("Osowiec · Fort I").transform);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
}
