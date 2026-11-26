using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class OsowiecBankLitter
{
    const string Fort = "Assets/Osowiec/Art/Fort1916/";
    const string Output = Fort + "Bank deposits";
    const string Group = "Наносы и пригнутая осока";
    const float CellSize = 14f;
    const int Pebble = 0, Rubble = 1, Blade = 2, Grain = 3;

    sealed class Cell
    {
        public readonly Vector3 origin;
        public readonly List<Vector3> vertices = new(), normals = new();
        public readonly List<Vector2> uv = new();
        public readonly List<Color32> colours = new();
        public readonly List<int>[] slots = {new(), new(), new(), new()};

        public Cell(Vector2Int id) =>
            origin = new Vector3((id.x + .5f) * CellSize, 0, (id.y + .5f) * CellSize);

        public int Vertex(Vector3 p, Vector3 n, Vector2 tex, float wind = 0)
        {
            int i = vertices.Count;
            vertices.Add(p - origin);
            normals.Add(n.normalized);
            uv.Add(tex);
            colours.Add(new Color32((byte)Mathf.RoundToInt(255 * wind),255,255,255));
            return i;
        }

        public void Tri(int slot, int a, int b, int c)
        {
            slots[slot].Add(a); slots[slot].Add(b); slots[slot].Add(c);
        }
    }

    sealed class Scatter
    {
        public readonly Dictionary<Vector2Int,Cell> cells = new();
        readonly Transform root;
        readonly RaycastHit[] hits = new RaycastHit[48];
        readonly System.Random random = new(64615);
        public int stones, tufts, atReference;

        public Scatter(Transform root) => this.root = root;
        float Range(float lo,float hi) => Mathf.Lerp(lo,hi,(float)random.NextDouble());
        bool Chance(float weight) => (float)random.NextDouble() < weight;
        static float Patch(Vector3 p) =>
            Mathf.PerlinNoise(p.x*.34f + 83.2f,p.z*.34f + 30.1f);

        Cell At(Vector3 p)
        {
            var id = new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
            if (!cells.TryGetValue(id,out var cell)) cells.Add(id,cell=new Cell(id));
            return cell;
        }

        bool Ground(float x,float z,out RaycastHit hit)
        {
            hit=default;
            int count=Physics.RaycastNonAlloc(new Vector3(x,12,z),Vector3.down,hits,22,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            float nearest=float.MaxValue;
            for (int i=0;i<count;i++)
            {
                var h=hits[i];
                if (h.distance>=nearest || !h.collider.transform.IsChildOf(root)) continue;
                nearest=h.distance;hit=h;
            }
            return hit.collider is MeshCollider mc && mc.sharedMesh &&
                mc.sharedMesh.name.StartsWith("Ground_",StringComparison.Ordinal);
        }

        void Stone(RaycastHit h,bool rubble)
        {
            var p=h.point;
            var cell=At(p);
            Vector3 n=h.normal.normalized;
            Vector3 tangent=Vector3.ProjectOnPlane(Vector3.right,n).normalized;
            if(tangent.sqrMagnitude<.01f) tangent=Vector3.ProjectOnPlane(Vector3.forward,n).normalized;
            Vector3 axis=Quaternion.AngleAxis(Range(0,360),n)*tangent, cross=Vector3.Cross(n,axis);
            float width=rubble ? Range(.13f,.23f) : Range(.017f,.078f);
            float length=width*Range(1.04f,rubble?1.8f:1.55f), height=width*Range(.45f,.86f);
            int sides=rubble?5:8, start=cell.vertices.Count;
            Vector3 centre=p-n*height*Range(.15f,.42f);
            float phase=Range(0,6.28f), u0=Range(0,1),v0=Range(0,1);
            int slot=rubble?Rubble:Pebble;
            // Three curved rings give damp gravel a rounded silhouette. The bottom ring is buried.
            for(int ring=0;ring<3;ring++)
            {
                float radius=ring==0?.82f:ring==1?1f:.66f, elevation=ring==0?-.40f:ring==1?.25f:.81f;
                for(int j=0;j<sides;j++)
                {
                    float a=2*Mathf.PI*j/sides;
                    float cos=Mathf.Cos(a), sin=Mathf.Sin(a);
                    float rough=1+.145f*Mathf.Sin(3*a+phase)+.075f*Mathf.Cos(2*a+3*phase);
                    float x=cos*length*rough*radius, y=sin*width*rough*radius;
                    Vector3 d=axis*x+cross*y;
                    Vector3 smooth=(axis*(cos/length)+cross*(sin/width))*radius+n*(elevation/height);
                    Vector3 world=centre+d+n*(height*(elevation+.075f*Mathf.Sin(a+phase)));
                    cell.Vertex(world,smooth,new Vector2(u0+x/.43f,v0+y/.43f));
                    if(ring>0)
                    {
                        int a0=start+(ring-1)*sides+j;
                        int b0=start+(ring-1)*sides+(j+1)%sides;
                        cell.Tri(slot,a0,b0,b0+sides);cell.Tri(slot,a0,b0+sides,a0+sides);
                    }
                }
            }
            int top=cell.Vertex(centre+n*height+axis*length*.09f,n,new Vector2(u0,v0));
            for(int j=0;j<sides;j++)cell.Tri(slot,start+2*sides+j,start+2*sides+(j+1)%sides,top);
            stones++;
            if ((new Vector2(p.x+11.15f,p.z+6.65f)).sqrMagnitude<2f) atReference++;
        }

        // Folded, tapered ribbons follow four different tangents from root to drooping tip.
        void Leaf(Cell cell,Vector3 p,Vector3 lean,float length,float reach,float width)
        {
            Vector3 side=Vector3.Cross(Vector3.up,lean).normalized*width;
            int first=cell.vertices.Count;
            for(int row=0;row<4;row++)
            {
                float t=row/3f;
                Vector3 centre=p+lean*(reach*t*t)+Vector3.up*(length*t*(1-.67f*t));
                float w=(1-t*.94f)*(.62f+.50f*Mathf.Sin(t*Mathf.PI));
                Vector3 along=lean*(2*reach*t)+Vector3.up*length*(1-1.34f*t);
                Vector3 face=Vector3.Cross(along,side).normalized;
                cell.Vertex(centre-side*w,face+side.normalized*.27f,new Vector2(0,t),t);
                cell.Vertex(centre+face*width*.28f*w,face,new Vector2(.5f,t),t);
                cell.Vertex(centre+side*w,face-side.normalized*.27f,new Vector2(1,t),t);
                if(row==0)continue;
                for(int k=0;k<2;k++)
                {
                    int a=first+(row-1)*3+k,b=a+3;
                    cell.Tri(Blade,a,b,b+1);cell.Tri(Blade,a,b+1,a+1);
                }
            }
        }

        void SeedSpray(Cell cell,Vector3 p,Vector3 dir,float scale)
        {
            Vector3 top=p+Vector3.up*.38f*scale+dir*.16f*scale;
            Vector3 side=Vector3.Cross(Vector3.up,dir).normalized;
            // Stem and paired spikelets follow the same tip-weighted wind as the leaves.
            void Strip(Vector3 a,Vector3 b,float width,float startWind,float endWind)
            {
                int i=cell.Vertex(a-side*width,dir,new Vector2(0,0),startWind);
                cell.Vertex(a+side*width,dir,new Vector2(1,0),startWind);
                cell.Vertex(b+side*width*.19f,dir,new Vector2(1,1),endWind);
                cell.Vertex(b-side*width*.19f,dir,new Vector2(0,1),endWind);
                cell.Tri(Grain,i,i+1,i+2);cell.Tri(Grain,i,i+2,i+3);
            }
            Strip(p,top,.0025f*scale,0,.95f);
            for(int j=0;j<5;j++)
            {
                float t=.65f+j*.062f;
                Vector3 stem=Vector3.Lerp(p,top,t);
                Vector3 end=stem+side*(j%2==0?1:-1)*(.028f+(1-t)*.08f)*scale+Vector3.up*.043f*scale;
                Strip(stem,end,.005f*scale,t,.96f);
            }
        }

        void Tuft(RaycastHit h)
        {
            var n=h.normal.normalized;
            Vector3 downhill=new Vector3(n.x,0,n.z).normalized;
            if(downhill.sqrMagnitude<.1f) downhill=Quaternion.Euler(0,Range(0,360),0)*Vector3.forward;
            var cell=At(h.point);
            float scale=Range(.63f,1.20f), wind=Range(-30,30);
            for(int j=0,count=random.Next(6,11);j<count;j++)
            {
                Vector3 dir=Quaternion.Euler(0,wind+Range(-85,85),0)*downhill;
                Vector3 stem=h.point+Vector3.ProjectOnPlane(dir*Range(0,.047f),n)-n*.026f;
                Leaf(cell,stem,dir,Range(.17f,.44f)*scale,Range(.12f,.34f)*scale,Range(.0045f,.012f)*scale);
            }
            if(Chance(.49f))SeedSpray(cell,h.point-n*.012f,downhill,scale);
            tufts++;
            if ((new Vector2(h.point.x+11.15f,h.point.z+6.65f)).sqrMagnitude<2f) atReference++;
        }

        void Cluster(RaycastHit h,float radius,bool hero)
        {
            float patch=Patch(h.point);
            float slope=1-h.normal.y;
            bool leafy=h.point.y>.78f && slope>.10f && slope<.72f && (hero||patch>.48f);
            float span=radius*Range(.6f,1.2f);
            int count=hero?random.Next(12,21):random.Next(5,17);
            for(int j=0;j<count;j++)
            {
                float a=Range(0,2*Mathf.PI),r=Mathf.Sqrt(Range(0,1))*span;
                if(!Ground(h.point.x+Mathf.Cos(a)*r,h.point.z+Mathf.Sin(a)*r*.65f,out var site)) continue;
                if (site.normal.y<.32f || site.point.y<.04f) continue;
                float exposure=OsowiecBankErosion.Exposure(site.point,site.normal);
                if(!hero && exposure<.09f)continue;
                // Gravel rolls down to the foot while roots hang from the higher lip.
                if(leafy && site.point.y>=h.point.y-.04f && j%3==0) Tuft(site);
                else if(hero || site.point.y<h.point.y+.13f || Chance(.18f))
                {
                    bool masonry=site.point.x>-14 && site.point.x<19 && site.point.z<-6.4f;
                    Stone(site,masonry && Chance(.035f));
                }
            }
        }

        public void Populate()
        {
            for(int i=0;i<1850;i++)
            {
                if(!Ground(Range(-50,34),Range(-22,20),out var h))continue;
                float exposure=OsowiecBankErosion.Exposure(h.point,h.normal);
                float patch=Patch(h.point);
                if(exposure<.20f || patch<.43f || !Chance(exposure*Mathf.Lerp(.08f,.9f,patch)))continue;
                Cluster(h,Range(.33f,1.18f),false);
            }
            // The near bank below the casemate and its two small washouts are the review viewpoint.
            foreach(var xz in new[]{new Vector2(-11.3f,-6.88f),new Vector2(-11.78f,-7.37f),
                                      new Vector2(-10.91f,-6.2f)})
                if(Ground(xz.x,xz.y,out var h)) Cluster(h,.68f,true);
        }
    }

    public static void Bake(Transform root)
    {
        Physics.SyncTransforms();
        var dressing=new Scatter(root);
        dressing.Populate();
        if(dressing.stones==0 || dressing.tufts==0 || dressing.atReference==0)
            throw new InvalidOperationException($"Missing bank foreground: stone={dressing.stones}, grass={dressing.tufts}, review={dressing.atReference}.");
        if(!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Fort.TrimEnd('/'),"Bank deposits");
        var materials=new[]{StoneMaterial(),Load("Brick core"),Load("Sedge"),SeedMaterial()};
        var holder=root.Find(Group);
        if(holder) UnityEngine.Object.DestroyImmediate(holder.gameObject);
        holder=new GameObject(Group).transform;
        holder.SetParent(root,false);
        var modifier=holder.gameObject.AddComponent<NavMeshModifier>();modifier.ignoreFromBuild=true;
        foreach(var pair in dressing.cells)
        {
            var id=pair.Key;var cell=pair.Value;
            string label=$"Bank {id.x} {id.y}";
            string path=Output+"/"+label+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh{name=label};AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();mesh.indexFormat=cell.vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(cell.vertices);mesh.SetNormals(cell.normals);mesh.SetUVs(0,cell.uv);mesh.SetColors(cell.colours);
            var used=new List<Material>();
            for(int i=0;i<materials.Length;i++) if(cell.slots[i].Count>0)used.Add(materials[i]);
            mesh.subMeshCount=used.Count;
            for(int i=0,sub=0;i<materials.Length;i++) if(cell.slots[i].Count>0)mesh.SetTriangles(cell.slots[i],sub++,false);
            mesh.RecalculateBounds();mesh.RecalculateTangents();
            mesh.bounds=new Bounds(mesh.bounds.center,mesh.bounds.size+Vector3.one*.18f);
            EditorUtility.SetDirty(mesh);
            var go=new GameObject(label);
            go.transform.position=cell.origin;go.transform.SetParent(holder,true);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials=used.ToArray();renderer.shadowCastingMode=ShadowCastingMode.On;
            renderer.receiveShadows=true;
        }
        Debug.Log($"[Osowiec bank litter] {dressing.stones} partially buried stones, {dressing.tufts} bent sedges, {dressing.atReference} in the close bank, {dressing.cells.Count} culling cells, no colliders.");
    }

    static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(Fort+name+".mat");

    static Material StoneMaterial()
    {
        string path=Output+"/Washed gravel.mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        var source=Load("Concrete");
        foreach(var prop in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap"})
            m.SetTexture(prop,source.GetTexture(prop));
        m.SetColor("_BaseColor",new Color(.61f,.64f,.60f));
        m.SetFloat("_BumpScale",.56f);m.SetFloat("_Smoothness",.71f);
        m.EnableKeyword("_NORMALMAP");m.EnableKeyword("_METALLICSPECGLOSSMAP");
        EditorUtility.SetDirty(m);return m;
    }

    static Material SeedMaterial()
    {
        string path=Output+"/Bent sedge seed.mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Load("Sedge"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",new Color(.42f,.32f,.18f));
        EditorUtility.SetDirty(m);return m;
    }
}
