using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Geometry = OsowiecFieldRoofs.Geometry;
using Sample = OsowiecFieldRoofs.Sample;

public static class OsowiecBatteryFinish
{
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    static readonly Vector3 Up = Vector3.up;
    static readonly Color Weather = new(.65f,.68f,.48f,1);
    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Art+name+".mat")
        ?? throw new InvalidOperationException("Missing fort surface "+name);
    static float Noise(float x,float z,int seed) => Mathf.PerlinNoise(x+seed*.83f,z+seed*.39f);
    static Sample At(Vector3 p,Vector2 uv) => new(p,uv,Weather);
    static MeshRenderer Child(Transform parent,string name)
    {
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        g.transform.SetParent(parent,false);
        g.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
        return g.GetComponent<MeshRenderer>();
    }
    static Material Tinted(string name,string source,Color tint,float smoothness)
    {
        string path=Art+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        var basis=Mat(source);
        if(!material){material=new Material(basis);AssetDatabase.CreateAsset(material,path);}
        material.shader=basis.shader;material.CopyPropertiesFromMaterial(basis);
        if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",tint);
        if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",smoothness);
        if(material.HasProperty("_ClothDye"))
        {
            material.SetColor("_ClothDye",tint);
            material.SetFloat("_ClothDampness",.68f);
        }
        if(material.HasProperty("_BumpScale"))material.SetFloat("_BumpScale",1.08f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // The existing roof renderer is also referenced by the camera cutaway; its physics
    // mesh retains the surveyed underside. Only the exposed facing material is replaced.
    static void Cover(MeshRenderer r,bool pitched,Vector3 origin,Vector3 forward,int seed,Material peat)
    {
        var side=Vector3.Cross(Up,forward);
        var geo=new Geometry(3);
        Vector3 Point(float u,float v)
        {
            float x,z,y;
            float variation=Noise(u*5.1f,v*4.4f,seed);
            float channels=Mathf.Pow(1-Mathf.Abs(2*Noise(u*3.3f+.23f*Mathf.Sin(v*3),v*.91f,seed+4)-1),7);
            if(pitched)
            {
                x=u*(3.46f+.09f*Mathf.Sin(v*6.5f+seed)+.11f*variation);
                z=v*(3.9f+.06f*Mathf.Sin(u*6f+seed));
                float ridge=1-Mathf.Clamp01(x<.57f?(.57f-x)/4.05f:(x-.57f)/2.89f);
                float eave=Mathf.SmoothStep(0,1,Mathf.Min(1-Mathf.Abs(u),1-Mathf.Abs(v))*8);
                y=6.62f+.75f*ridge+eave*(.12f+.12f*variation)-channels*.065f*eave+.05f*Mathf.Cos(v*4+seed);
            }
            else
            {
                float end=Mathf.Pow(Mathf.Abs(v),4);
                x=u*(3.42f-end*(.49f+.20f*Mathf.Sin(seed+u*3))+.12f*Mathf.Sin(v*4.9f+seed+u));
                z=v*(3.64f-.28f*Mathf.Pow(Mathf.Abs(u),3.4f))+.13f*Mathf.Sin(u*4+seed);
                float vault=3.32f+1.11f*Mathf.Sqrt(Mathf.Max(0,1-x*x/13.7641f));
                float roll=Mathf.Clamp01((1-Mathf.Pow(Mathf.Abs(u),2.6f))*(1-end));
                y=vault+.10f+roll*(.44f+.24f*Mathf.Sin(3*u+v+seed)+.14f*Mathf.Cos(6*v-2*u+seed)+.32f*variation-.12f*channels);
            }
            y+=.034f*(Noise(16*u,15*v,seed+23)-.5f);
            return origin+side*x+forward*z+Up*y;
        }
        float Bare(float u,float v)
        {
            float edge=Mathf.Max(Mathf.Abs(u),Mathf.Abs(v));
            return Mathf.Clamp01(.09f+.52f*Mathf.SmoothStep(0,1,(edge-.55f)*2.3f)
                +.81f*Mathf.SmoothStep(0,1,(Noise(u*2.2f,v*3.4f,seed+4)-.42f)*3.6f));
        }
        geo.Grid(0,64,65,(i,j)=>{
            float u=2*i-1,v=2*j-1;var p=Point(u,v);
            return new Sample(p,new Vector2(Vector3.Dot(p,side),Vector3.Dot(p,forward))*.90f,
                new Color(Bare(u,v),.42f+.40f*Noise(2*u,5*v,seed),Noise(u*6,v*6,seed),1));
        });
        for(int edge=0;edge<4;edge++)
        {
            int e=edge;
            Vector3 Lip(float t) => e switch{0=>Point(-1,2*t-1),1=>Point(1,1-2*t),2=>Point(1-2*t,-1),_=>Point(2*t-1,1)};
            // The skirt folds back into the board fascia and stone vault at the drip edge.
            geo.Grid(1,64,4,(t,depth)=>{
                var p=Lip(t);
                var inwards=origin-p;inwards.y=0;inwards.Normalize();
                p+=inwards*depth*.10f-Up*depth*(.17f+.11f*Noise(t*18,e,seed));
                return At(p,new Vector2(t*6,depth*.32f));
            });
        }
        var rng=new System.Random(seed);
        float Rand()=>(float)rng.NextDouble();
        for(int k=0;k<780;k++)
        {
            float u=Rand()*1.95f-.975f,v=Rand()*1.95f-.975f;
            if(Bare(u,v)>.70f || Noise(5*u,5*v,seed+20)<.45f)continue;
            var p=Point(u,v)-Up*.015f;
            float clump=.083f+.16f*Rand();
            for(int b=0;b<7;b++)
            {
                float a=b*2.4f+Rand(),h=clump*(.6f+.8f*Rand()),w=.007f+.007f*Rand();
                var away=side*Mathf.Cos(a)+forward*Mathf.Sin(a);
                var across=Vector3.Cross(Up,away);
                var foot=p+away*.035f;
                float bend=.25f+.39f*Rand();
                geo.Grid(2,2,3,(i,j)=>{
                    var pos=foot+Up*(h*(j-.29f*j*j))+(away*bend+Vector3.right*.17f)*(h*j*j)
                        +across*((i-.5f)*w*2*(1-.96f*j))+away*(1-Mathf.Abs(2*i-1))*.004f*(1-j);
                    return new Sample(pos,new Vector2(i,j),new Color(j,.75f,.6f,1));
                });
            }
        }
        var filter=r.GetComponent<MeshFilter>();
        var baseline=filter.sharedMesh;
        var originals=r.sharedMaterials;
        geo.Save(r,r.name+" facing",Mat("Casemate sod and scoured peat"),peat,Mat("Sedge"));
        var shell=filter.sharedMesh;
        var slots=new List<CombineInstance>();
        var mats=new List<Material>();
        for(int i=0;i<originals.Length;i++)
        {
            if(originals[i].name=="Sod" || originals[i].name=="Earth")continue;
            slots.Add(new CombineInstance{mesh=baseline,subMeshIndex=i});mats.Add(originals[i]);
        }
        var outer=r.sharedMaterials;
        for(int i=0;i<outer.Length;i++){slots.Add(new CombineInstance{mesh=shell,subMeshIndex=i});mats.Add(outer[i]);}
        string path=Art+r.name+" finished.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh){mesh=new Mesh{name=r.name+" finished"};AssetDatabase.CreateAsset(mesh,path);}
        mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;
        mesh.CombineMeshes(slots.ToArray(),false,false);
        filter.sharedMesh=mesh;r.sharedMaterials=mats.ToArray();
        EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(filter);EditorUtility.SetDirty(r);
    }

    static void Face(Geometry g,int slot,Vector3 outward,params Vector3[] points)
    {
        if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),outward)<0)
            Array.Reverse(points);
        Vector3 x=Mathf.Abs(outward.y)>.80f?Vector3.right:Vector3.Cross(Up,outward).normalized;
        Vector3 y=Vector3.Cross(outward,x).normalized;
        var ids=points.Select(p=>g.Vertex(At(p,new Vector2(Vector3.Dot(p,x),Vector3.Dot(p,y))*.79f))).ToArray();
        for(int k=1;k<points.Length-1;k++)g.Tri(slot,ids[0],ids[k],ids[k+1]);
    }
    static void Box(Geometry geo,int slot,Vector3 c,Vector3 size)
    {
        Vector3 x=Vector3.right*(size.x*.5f),y=Up*(size.y*.5f),z=Vector3.forward*(size.z*.5f);
        for(int sign=-1;sign<=1;sign+=2)
        {
            var a=c+x*sign;Face(geo,slot,x.normalized*sign,a-y-z,a-y+z,a+y+z,a+y-z);
            a=c+y*sign;Face(geo,slot,y.normalized*sign,a-x-z,a-x+z,a+x+z,a+x-z);
            a=c+z*sign;Face(geo,slot,z.normalized*sign,a-x-y,a-x+y,a+x+y,a+x-y);
        }
    }
    static float Ground(float x,float z)
    {
        var ray=Physics.RaycastAll(new Vector3(x,16,z),Vector3.down,37,~0,QueryTriggerInteraction.Ignore);
        foreach(var hit in ray)
            if(hit.collider.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal)||
                hit.collider.name.StartsWith("Flood_meadow_",StringComparison.Ordinal))return hit.point.y;
        return -.62f;
    }

    static void ReturnsAndMast(MeshRenderer ends,MeshFilter crown,Transform root,Material peat)
    {
        var vertices=crown.sharedMesh.vertices.Select(crown.transform.TransformPoint).ToArray();
        var indices=crown.sharedMesh.triangles;
        float CrownHeight(float x,float z)
        {
            float result=Ground(x,z);
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                float det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                if(Mathf.Abs(det)<.00001f)continue;
                float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/det;
                float v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/det;
                if(u>=-.0001f&&v>=-.0001f&&u+v<=1.0001f)result=Mathf.Max(result,a.y*u+b.y*v+c.y*(1-u-v));
            }
            return result;
        }
        var geo=new Geometry(2);
        foreach(var end in new[]{(-25,-1),(15,1),(19,-1),(53,1)})
        {
            var crest=vertices.Where(p=>Mathf.Abs(p.x-end.Item1)<.04f)
                .GroupBy(p=>Mathf.RoundToInt(p.z*100)).Select(s=>s.OrderByDescending(p=>p.y).First()).OrderBy(p=>p.z).ToArray();
            if(crest.Length<6)throw new InvalidOperationException("Missing surveyed breastwork profile at "+end.Item1);
            var outward=Vector3.right*end.Item2;
            for(int j=0;j<crest.Length-1;j++)
            {
                var a=crest[j];var b=crest[j+1];
                int slices=Mathf.Max(2,Mathf.CeilToInt((b.z-a.z)*3.5f));
                for(int k=0;k<slices;k++)
                {
                    Vector3[] Ring(float t)
                    {
                        var p=Vector3.Lerp(a,b,t);
                        float ground=Mathf.Min(Ground(p.x+end.Item2*.45f,p.z)-.11f,p.y-.34f);
                        float n=Noise(p.x*.21f,p.z*1.7f,32);
                        var low=new Vector3(p.x,ground,p.z);
                        return new[]{p-outward*.025f-Up*.006f,p+outward*(.045f+.08f*n)-Up*(.16f+.16f*n),
                            low+Up*(.14f+.18f*n)+outward*(.21f+.09f*n),low+outward*(.57f+.18f*n)-Up*.13f};
                    }
                    var left=Ring(k/(float)slices);var right=Ring((k+1)/(float)slices);
                    for(int band=0;band<3;band++)
                    {
                        var normal=band==1?outward:(outward+Up).normalized;
                        Face(geo,band==1?0:1,normal,left[band],right[band],right[band+1],left[band+1]);
                    }
                    if((j==0&&k==0)||(j==crest.Length-2&&k==slices-1))
                    {
                        var ring=j==0?left:right;
                        Face(geo,0,Vector3.forward*(j==0?-1:1),ring[0],ring[1],ring[2],ring[3],new Vector3(ring[0].x,ring[3].y,ring[0].z));
                    }
                }
            }
        }
        geo.Save(ends,"Rampart weathered solid returns",Mat("Brick"),peat);
        var collision=ends.GetComponent<MeshCollider>();
        if(!collision)collision=ends.gameObject.AddComponent<MeshCollider>();
        collision.sharedMesh=ends.GetComponent<MeshFilter>().sharedMesh;
        EditorUtility.SetDirty(collision);

        var shoes=new Geometry(2);
        foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
        {
            var foot=new Vector3(28.7f+.69f*sx,4.55f,20.6f+.69f*sz);
            float y=CrownHeight(foot.x,foot.z);
            // Follow the inclined iron leg at the point it actually enters the bank.
            if(y>foot.y)foot+=new Vector3(-sx*.5f,0,-sz*.5f)*((y-foot.y)/5.81f);
            y=CrownHeight(foot.x,foot.z);
            var bed=new Vector3(foot.x,y-.13f,foot.z);
            Box(shoes,0,bed,new Vector3(.52f,.52f,.50f));
            var plate=bed+Up*.287f;Box(shoes,1,plate,new Vector3(.38f,.058f,.36f));
            var join=new Vector3(28.7f+sx*.69f,4.59f,20.6f+sz*.69f);
            if(join.y>plate.y+.055f)shoes.Cord(1,t=>Vector3.Lerp(plate,join,t),.066f,2,8);
            foreach(float dx in new[]{-.13f,.13f})foreach(float dz in new[]{-.12f,.12f})
                Box(shoes,1,plate+new Vector3(dx,.048f,dz),new Vector3(.057f,.049f,.057f));
        }
        shoes.Save(Child(root,"Survey mast footings"),"Survey mast stepped anchor shoes",Mat("Concrete"),Mat("Oxide"));
    }

    static void Sacks(MeshRenderer r)
    {
        var geo=new Geometry(3);
        var rng=new System.Random(96321);
        float Rand()=>(float)rng.NextDouble();
        int count=0;
        foreach(var group in new[]{(-18f,7f,2.4f),(6f,16f,-.14f),(16f,9f,1.21f),(28f,5f,-.29f),(37f,3f,.81f),(20f,-9f,1.92f)})
        for(int row=0;row<2;row++)for(int i=0;i<4-row;i++)
        {
            float distance=(i-1.4f+row*.4f)*.66f;
            float x=group.Item1+distance*Mathf.Cos(group.Item3),z=group.Item2+distance*Mathf.Sin(group.Item3);
            float angle=group.Item3+(Rand()-.5f)*.18f;
            var along=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            var side=Vector3.Cross(along,Up);
            float phase=Rand()*6.28f,length=.383f+.024f*Rand(),width=.208f+.027f*Rand(),height=.116f+.022f*Rand();
            float y=Ground(x,z)+height-.013f+row*.213f;
            Vector3 p=new Vector3(x,y,z);
            float Sp(float n,float exponent)=>Mathf.Sign(n)*Mathf.Pow(Mathf.Abs(n),exponent);
            Vector3 Cloth(float along01,float phi)
            {
                float u=along01*2-1,abs=Mathf.Abs(u),end=Mathf.Pow(abs,3.8f);
                float endShape=Mathf.Max(.024f,Mathf.Pow(Mathf.Max(0,1-Mathf.Pow(abs,4)),.44f));
                float gather=.016f*end*Mathf.Sin(phi*7+u*4+phase);
                float sag=Mathf.Cos(2*phi+.8f*u+phase)*.007f;
                float vy=Sp(Mathf.Sin(phi),.63f);
                float fold=.009f*(.3f+.7f*end)*Mathf.Cos(phi*5+phase+u*7);
                return p+along*(u*length+.012f*Mathf.Sin(phi*2+phase)*end)
                    +side*((width+gather+fold)*endShape*Sp(Mathf.Cos(phi),.66f)+.012f*Mathf.Sin(u*4+phase))
                    +Up*((height+fold)*endShape*vy+.021f*u*Mathf.Sin(phase)+sag*endShape
                        -.016f*end+.012f*Mathf.Sin(3*u+phase)*Mathf.Max(0,vy));
            }
            geo.Grid(0,22,28,(u,v)=>{
                float theta=-Mathf.PI*2*v;
                float soaking=Mathf.Clamp01(.22f-.55f*Mathf.Sin(theta)+.25f*Noise(2*u,i+row,32));
                return new Sample(Cloth(u,theta),new Vector2(u*1.48f+.32f*i,v*1.70f+row*.36f),
                    new Color(1,soaking,1,1));
            });
            // Flattened folded selvages run the length of both flanks and catch the light.
            foreach(int sign in new[]{-1,1})
            {
                float baseAngle=sign>0?.17f:Mathf.PI-.17f;
                geo.Cord(1,t=>Cloth(.035f+.93f*t,baseAngle)+side*sign*.0015f,.0065f,21,5);
                for(int stitch=0;stitch<12;stitch++)
                {
                    float st=.09f+stitch*.07f;
                    geo.Cord(2,t=>Cloth(st+.030f*t,baseAngle+.038f*Mathf.Sin(t*Mathf.PI))
                        +side*sign*.006f,.0025f,2,4);
                }
            }
            // The closure is a short, folded ear above the twine, not a spherical end cap.
            var mouth=Cloth(.984f,1.57f);
            Face(geo,0,Up,mouth-along*.019f-side*.029f,mouth+along*.064f-side*.032f+Up*.017f,
                mouth+along*.074f+side*.026f+Up*.023f,mouth+side*.034f);
            geo.Cord(2,t=>Cloth(.982f,2*Mathf.PI*t),.007f,16,5);
            count++;
        }
        var jute=Tinted("Battery soaked sacking","Quartermaster linen",new Color(.86f,.78f,.63f),.18f);
        geo.Save(r,"Battery field filled sacks and sewn hems",jute,Mat("Repair flax"),Mat("Hemp ties"));
        Debug.Log("Eastern battery artist pass: "+count+" compressed canvas sacks across six surveyed parapets.");
    }

    public static void Install(Transform fort,Transform root)
    {
        var meshes=fort.GetComponentsInChildren<MeshRenderer>(true);
        MeshRenderer Find(string name)=>meshes.Single(r=>r.name==name);
        var peat=Tinted("Battery wet cut peat","Earth",new Color(.64f,.57f,.50f),.31f);
        var cap=Find("Turf_section_over_vault");
        cap.sharedMaterials=Enumerable.Repeat(peat,cap.sharedMaterials.Length).ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(cap);
        EditorUtility.SetDirty(cap);
        foreach(string label in new[]{"Battery gallery earthwork","Battery passage and chambers"})
        {
            var r=Find(label);
            r.sharedMaterials=r.sharedMaterials.Select(m=>m.name=="Earth"||m.name=="Sod"?peat:m).ToArray();
            EditorUtility.SetDirty(r);
        }
        Cover(Find("Battery gallery roof"),true,new Vector3(46,0,1.1f),Vector3.forward,123,peat);
        Cover(Find("Battery north pavilion roof"),false,new Vector3(32,0,9),new Vector3(Mathf.Cos(1.19f),0,Mathf.Sin(1.19f)),54,peat);
        Cover(Find("Battery south pavilion roof"),false,new Vector3(40,0,-8),new Vector3(Mathf.Cos(2.10f),0,Mathf.Sin(2.10f)),78,peat);
        ReturnsAndMast(Find("Fort_end_walls"),Find("Fort_crown").GetComponent<MeshFilter>(),root,peat);
        Sacks(Find("Solid_Sandbags"));
        Verify(fort);
    }

    public static void Verify(Transform fort)
    {
        var renderers=fort.GetComponentsInChildren<MeshRenderer>(true);
        MeshRenderer Find(string name)=>renderers.Single(r=>r.name==name);
        foreach(string roof in new[]{"Battery gallery roof","Battery north pavilion roof","Battery south pavilion roof"})
        {
            var r=Find(roof);
            var mesh=r.GetComponent<MeshFilter>().sharedMesh;
            if(mesh.name!=roof+" finished" || mesh.vertexCount<8000 ||
               mesh.tangents.Length!=mesh.vertexCount || mesh.normals.Length!=mesh.vertexCount ||
               r.sharedMaterials.Any(m=>!m) || !r.sharedMaterials.Contains(Mat("Casemate sod and scoured peat")) ||
               r.GetComponent<MeshCollider>().sharedMesh==mesh)
                throw new InvalidOperationException("Missing roof relief, mapped material or separate underside on "+roof);
            Debug.Log($"Eastern battery roof {roof}: {mesh.vertexCount} vertices, {mesh.subMeshCount} surface batches.");
        }
        var ends=Find("Fort_end_walls");
        var endGeometry=ends.GetComponent<MeshCollider>();
        Physics.SyncTransforms();
        foreach(var end in new[]{(-25,-1,27.5f),(15,1,27.5f),(19,-1,17f),(53,1,17f)})
        {
            float crest=end.Item1==19||end.Item1==53?4.80f:4.20f;
            float ground=Ground(end.Item1+end.Item2*.45f,end.Item3);
            var start=new Vector3(end.Item1+end.Item2*2,Mathf.Lerp(Mathf.Min(ground,crest-.7f),crest,.55f),end.Item3);
            if(!endGeometry.Raycast(new Ray(start,Vector3.left*end.Item2),out var hit,3f) ||
                Mathf.Abs(hit.point.x-end.Item1)>.7f)
                throw new InvalidOperationException("Gap at exterior retaining wall "+start);
        }
        var bags=Find("Solid_Sandbags");
        var cloth=bags.GetComponent<MeshFilter>().sharedMesh;
        if(cloth.subMeshCount!=3 || cloth.vertexCount<30000 || cloth.normals.Length!=cloth.vertexCount ||
            !bags.sharedMaterials[0].GetTexture("_BumpMap") || !Find("Survey mast footings"))
            throw new InvalidOperationException("Incomplete cover or aerial anchors.");
        Debug.Log($"Four outside bank returns are closed. Stitched sandbag surface has {cloth.vertexCount} vertices.");
    }
}

