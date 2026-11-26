using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Scenery for the fictional eastern service court. Its existing cast-iron exhibits stay in their surveyed positions.
public static partial class OsowiecBatteryFort
{
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    const string Mission = "Assets/Osowiec/Osowiec.unity";
    const string Name = "Восточная батарея · нижний ход и галерея";
    const float TunnelX = 46f, Bottom = -2.85f, Deck = 3.55f;
    public static Vector3 GalleryPost => new(TunnelX,Deck,1.1f);
    enum Finish { Brick, Concrete, Iron, Timber, Turf, Brass, Glass, Earth, BrokenBrick, Lime, Damp, Wet, Canvas, Patch, Rope, Tar, SplitWood, Deal, Linen, Cord }
    static readonly string[] Surfaces = {
        "Brick", "Concrete", "Oxide", "Timber", "Sod", "Brass", "Glass", "Earth", "Brick core",
        "Battery limewash", "Battery seepage", "Battery wet floor","Canvas","Repair flax","Exposed root fibre","Tar","Adzed timber","Washed deal","Quartermaster linen","Hemp ties" };
    static readonly Vector3 R = Vector3.right, U = Vector3.up, F = Vector3.forward;
    static Vector3 V(float x, float y, float z) => new(x,y,z);

    sealed class Masonry
    {
        readonly List<Vector3> vertices = new();
        readonly List<Vector3> uvs = new();
        readonly List<int>[] faces = Surfaces.Select(_ => new List<int>()).ToArray();

        void Face(Finish mat, Vector3 inside, params Vector3[] corners)
        {
            var normal = Vector3.Cross(corners[1]-corners[0],corners[2]-corners[0]).normalized;
            if (Vector3.Dot(normal,corners[0]-inside) < 0)
            {
                Array.Reverse(corners);
                normal = -normal;
            }
            Vector3 axis = Mathf.Abs(normal.y) > .85f ? R : Vector3.Cross(U,normal).normalized;
            Vector3 other = Vector3.Cross(normal,axis);
            int start = vertices.Count;
            foreach (var point in corners)
            {
                vertices.Add(point);
                uvs.Add(new Vector2(Vector3.Dot(point,axis),Vector3.Dot(point,other)) / 1.55f);
            }
            for (int i=1;i<corners.Length-1;i++)
                faces[(int)mat].AddRange(new[] { start, start+i, start+i+1 });
        }

        internal void Prism(Finish mat, Vector3[] loop, Vector3 depth)
        {
            Vector3 centre = depth*.5f;
            foreach (var p in loop) centre += p / loop.Length;
            Face(mat,centre,(Vector3[])loop.Clone());
            Face(mat,centre,loop.Select(p => p+depth).ToArray());
            for (int i=0;i<loop.Length;i++)
            {
                var a = loop[i];
                var b = loop[(i+1)%loop.Length];
                Face(mat,centre,a,b,b+depth,a+depth);
            }
        }

        internal void Box(Finish mat, Vector3 centre, Vector3 width, Vector3 height, Vector3 depth)
        {
            var p = centre-(width+height+depth)*.5f;
            Prism(mat,new[]{p,p+width,p+width+height,p+height},depth);
        }

        internal void Block(Finish mat, Vector3 centre,float x,float y,float z) => Box(mat,centre,R*x,U*y,F*z);

        internal void Skin(Finish mat, Vector3 normal,params Vector3[] points)
            => Face(mat,points[0]-normal,points);

        internal void Stain(Finish mat,Vector3 centre,Vector3 across,Vector3 along,Vector3 facing,int seed)
        {
            int start=vertices.Count;
            vertices.AddRange(new[]{centre-across-along,centre+across-along,centre+across+along,centre-across+along});
            uvs.AddRange(new[]{V(0,0,seed),V(1,0,seed),V(1,1,seed),V(0,1,seed)});
            bool forward=Vector3.Dot(Vector3.Cross(across,along),facing)>0;
            var indices=forward ? new[]{0,1,2,0,2,3} : new[]{0,2,1,0,3,2};
            foreach(int index in indices)faces[(int)mat].Add(start+index);
        }

        // Uneven strips with an actual crown, shoulder and crumbling toe. The courses follow the wall.
        internal void Bank(Vector3 origin,Vector3 along,Vector3 outward,float length,
            float toe,float shoulder,float crown,float crest,int seed,bool capped=true)
        {
            var rng=new System.Random(seed);
            Vector3[] previous=null;
            for(int i=0;i<=12;i++)
            {
                float t=i/12f;
                float n=(float)rng.NextDouble();
                float profile=Mathf.Lerp(.40f,1f,Mathf.Clamp01(Mathf.Min(t,1-t)*6.2f));
                float h=crown*profile;
                var centre=origin+along*((t-.5f)*length);
                var ring=new[]{
                    centre+outward*(toe*(.78f+.22f*n)*profile),
                    centre+outward*(shoulder*profile)+U*h*(.37f+n*.21f),
                    centre+outward*(shoulder*(.26f+.14f*n)*profile)+U*h*(.87f+n*.10f),
                    centre-outward*crest+U*h };
                if(previous!=null)for(int j=0;j<3;j++)
                {
                    Finish finish=j==0 || (j==1 && (i+seed)%4!=0) ? Finish.Earth : Finish.Turf;
                    var normal=outward+U;
                    Skin(finish,normal,previous[j],ring[j],ring[j+1]);
                    Skin(finish,normal,previous[j],ring[j+1],previous[j+1]);
                }
                if(capped && (i==0 || i==12))
                    Skin(Finish.Earth,along*(i==0?-1:1),ring[0],ring[1],ring[2],ring[3],centre-outward*crest);
                previous=ring;
            }
        }

        internal void Gable(Vector3 spring,Vector3 side,Vector3 along)
        {
            for(int i=0;i<14;i++)
            {
                float a=Mathf.PI*i/14, b=Mathf.PI*(i+1)/14;
                var p=spring+side*(3.41f*Mathf.Cos(a));var q=spring+side*(3.41f*Mathf.Cos(b));
                var face=new[]{p-U*.1f,q-U*.1f,q+U*(.80f*Mathf.Sin(b)),p+U*(.80f*Mathf.Sin(a))};
                Prism(Finish.Brick,face,along*.30f);
            }
        }

        internal void Vault(Vector3 spring, Vector3 across, Vector3 axis, float halfSpan, float rise,
            float wall, float depth, bool dressed)
        {
            const int pieces = 17;
            Vector3 Pt(float angle,float extra) => spring-axis*(depth*.5f) +
                across*(Mathf.Cos(angle)*(halfSpan+extra)) + U*(Mathf.Sin(angle)*(rise+extra));
            for (int i=0;i<pieces;i++)
            {
                float a=Mathf.PI*i/pieces, b=Mathf.PI*(i+1)/pieces;
                if(dressed){a+=.004f;b-=.004f;}
                Prism(dressed && i%5!=1 ? Finish.Concrete : Finish.Brick,
                    new[]{Pt(a,0),Pt(b,0),Pt(b,wall),Pt(a,wall)},axis*depth);
            }
        }

        internal Renderer Save(Transform parent, string label,bool solid=true)
        {
            string path = Art + label + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh {name=label}; AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0,uvs);
            var used = Enumerable.Range(0,Surfaces.Length).Where(i=>faces[i].Count>0).ToArray();
            mesh.subMeshCount = used.Length;
            for(int i=0;i<used.Length;i++) mesh.SetTriangles(faces[used[i]],i);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(parent,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            if(solid) go.AddComponent<MeshCollider>().sharedMesh=mesh;
            else go.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials=used.Select(i=>AssetDatabase.LoadAssetAtPath<Material>(Art+Surfaces[i]+".mat")).ToArray();
            return renderer;
        }
    }

    static float Ground(float x,float z)
    {
        foreach(var hit in Physics.RaycastAll(V(x,10,z),Vector3.down,20,~0,QueryTriggerInteraction.Ignore))
            if(hit.collider.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal)) return hit.point.y;
        throw new InvalidOperationException($"Missing surveyed floor at {x}, {z}.");
    }

    static float PassageHeight(float z,float south,float north) => z < -4.5f
        ? Mathf.Lerp(south,Bottom,Mathf.InverseLerp(-12,-4.5f,z))
        : Mathf.Lerp(Bottom,north,Mathf.InverseLerp(5.5f,14,z));

    static void Excavate(Transform fort, float south, float north)
    {
        const string prefix="Battery passage cut ";
        var originals=AssetDatabase.LoadAllAssetsAtPath(Art+"MarshFort.fbx").OfType<Mesh>().ToLookup(m=>m.name);
        foreach(var filter in fort.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal)) continue;
            var renderer=filter.GetComponent<Renderer>();
            if(!renderer.bounds.Intersects(new Bounds(V(TunnelX,0,1),V(6,14,28))))continue;
            var source=filter.sharedMesh;
            if(source.name.StartsWith(prefix,StringComparison.Ordinal))source=originals[source.name.Substring(prefix.Length)].Single();
            var verts=source.vertices;
            bool changed=false;
            for(int i=0;i<verts.Length;i++)
            {
                var world=filter.transform.TransformPoint(verts[i]);
                float x=Mathf.Abs(world.x-TunnelX);
                if(x>=2.8f || world.z < -12.55f || world.z >14.55f)continue;
                float weight=1-Mathf.InverseLerp(1.77f,2.8f,x);
                weight*=Mathf.Clamp01((world.z+12.55f)/.55f)*Mathf.Clamp01((14.55f-world.z)/.55f);
                world.y=Mathf.Lerp(world.y,PassageHeight(world.z,south,north)-.14f,weight);
                verts[i]=filter.transform.InverseTransformPoint(world);
                changed=true;
            }
            if(!changed)continue;
            string label=prefix+source.name;
            var path=Art+label+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=Object.Instantiate(source);mesh.name=label;AssetDatabase.CreateAsset(mesh,path);}
            mesh.vertices=verts;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            filter.sharedMesh=mesh;
            var ground=filter.GetComponent<MeshCollider>();
            ground.sharedMesh=null;ground.sharedMesh=mesh;
        }
    }

    static void Corridor(Masonry stone,Masonry canopy,float south,float north)
    {
        var from=V(TunnelX,south,-12);
        foreach(var to in new[]{V(TunnelX,Bottom,-4.5f),V(TunnelX,Bottom,5.5f),V(TunnelX,north,14)})
        {
            var span=to-from;
            stone.Box(Finish.Concrete,(from+to)*.5f-U*.09f,R*3.3f,U*.18f,span);
            bool level=Mathf.Abs(span.y)<.001f;
            foreach(int side in new[]{-1,1})
            {
                float height=level?1.66f:.92f;
                var centre=(from+to)*.5f+R*(side*1.70f)+U*(height*.5f-.06f);
                stone.Box(Finish.Brick,centre,R*.35f,U*height,span);
                stone.Box(Finish.Concrete,centre+U*(height*.5f),R*.40f,U*.11f,span);
            }
            from=to;
        }
        var spring=V(TunnelX,Bottom+1.62f,.5f);
        canopy.Vault(spring,R,F,1.55f,.96f,.32f,10,false);
        canopy.Block(Finish.Turf,V(TunnelX,.24f,.5f),3.8f,.28f,9.95f);
        foreach(float z in new[]{-4.50f,-2.0f,.50f,3.0f,5.50f})
            canopy.Vault(V(TunnelX,spring.y,z),R,F,1.49f,.89f,.28f,.27f,true);
        foreach(float z in new[]{-3.5f,4.5f})
        {
            var bulb=V(TunnelX+1.30f,Bottom+1.85f,z);
            stone.Block(Finish.Iron,bulb+V(.15f,.17f,0),.36f,.07f,.21f);
            stone.Block(Finish.Glass,bulb,.13f,.26f,.16f);
            foreach(float y in new[]{-.14f,.14f})stone.Block(Finish.Brass,bulb+U*y,.21f,.048f,.23f);
            foreach(int s in new[]{-1,1})stone.Block(Finish.Iron,bulb+V(-.08f,0,.075f*s),.020f,.26f,.02f);
        }
        for(int i=0;i<10;i++)
        {
            Vector3 a=V(TunnelX+1.39f,Bottom+1.78f+.07f*Mathf.Cos(i*Mathf.PI/2),-4.5f+i);
            Vector3 b=V(a.x,Bottom+1.78f+.07f*Mathf.Cos((i+1)*Mathf.PI/2),-3.5f+i);
            stone.Box(Finish.Iron,(a+b)*.5f,R*.033f,U*.038f,b-a);
        }
        foreach(float z in new[]{-1.8f,2.85f})
        {
            var centre=V(TunnelX-1.18f,Bottom+.28f,z);
            stone.Block(Finish.Timber,centre,.53f,.53f,.90f);
            foreach(float along in new[]{-.31f,.31f})
                stone.Block(Finish.Iron,centre+F*along,.55f,.55f,.042f);
            stone.Block(Finish.Brass,centre+V(.28f,.02f,0),.027f,.035f,.18f);
        }
    }

    static void Scar(Masonry wear,Vector3 p,Vector3 along,Vector3 normal,float w,float h,int seed)
    {
        var rng=new System.Random(seed);
        wear.Stain(Finish.Lime,p-U*h*.26f,along*(w*.57f),U*h*.37f,normal,seed);
        for(int i=0;i<4;i++)
        {
            float x=(i-1.5f+(float)rng.NextDouble()*.36f)*w*.20f;
            float tail=h*(.27f+.27f*(float)rng.NextDouble());
            var centre=p+along*x-U*(tail+.12f*h)+normal*.0012f;
            wear.Stain(Finish.Damp,centre,along*(w*.07f),U*tail,normal,seed+13*i);
        }
    }

    static void WetPatch(Masonry wear,Vector3 centre,Vector3 across,Vector3 along,int seed)
    {
        wear.Stain(Finish.Wet,centre,across*1.35f,along*1.26f,U,seed);
    }

    static void TurfCap(Masonry roof,Vector3 centre,Vector3 side,Vector3 forward,int seed)
    {
        // Weathered shoulders narrow at the ends and reveal the old vault around the eaves.
        const int lanes=16,rows=17;
        Vector3 At(int col,int row)
        {
            float u=col/(float)lanes*2-1,v=row/(float)rows*2-1;
            float edge=Mathf.Pow(Mathf.Abs(v),3.7f);
            float width=3.40f-edge*(.79f+.23f*Mathf.Sin(seed+u*2.7f))
                +.11f*Mathf.Sin(4.9f*v+seed+u);
            float x=u*width;
            float z=v*(3.57f-.42f*Mathf.Pow(Mathf.Abs(u),3.4f))+.14f*Mathf.Sin(u*4+seed);
            float skin=3.32f+1.11f*Mathf.Sqrt(Mathf.Max(0,1-x*x/(3.71f*3.71f)));
            float roll=Mathf.Clamp01((1-Mathf.Pow(Mathf.Abs(u),2.6f))*(1-edge));
            float swell=.51f+.19f*Mathf.Sin(3*u+v+seed)+.10f*Mathf.Cos(6*v-2*u+seed);
            return centre+side*x+forward*z+U*(skin+.075f+roll*swell);
        }
        for(int row=0;row<rows;row++)for(int col=0;col<lanes;col++)
        {
            var a=At(col,row);var b=At(col+1,row);var c=At(col+1,row+1);var d=At(col,row+1);
            roof.Skin(Finish.Turf,U,a,b,c);roof.Skin(Finish.Turf,U,a,c,d);
            void Lip(Vector3 p,Vector3 q,Vector3 normal)=>roof.Skin(Finish.Earth,normal,p,q,q-U*.15f,p-U*.15f);
            if(col==0)Lip(a,d,-side);
            if(col==lanes-1)Lip(b,c,side);
            if(row==0)Lip(a,b,-forward);
            if(row==rows-1)Lip(d,c,forward);
        }
    }

    static void Chamber(Masonry wall,Masonry canopy,Masonry wear,float cx,float cz,float angle)
    {
        Vector3 forward=V(Mathf.Cos(angle),0,Mathf.Sin(angle)), side=Vector3.Cross(U,forward);
        Vector3 At(float x,float y,float z)=>V(cx,0,cz)+side*x+U*y+forward*z;
        void Block(Masonry g,Finish mat,float x,float y,float z,float w,float h,float d) =>
            g.Box(mat,At(x,y,z),side*w,U*h,forward*d);
        // The opening passes around the existing static display mesh and through the full front wall.
        foreach(int s in new[]{-1,1})
        {
            Block(wall,Finish.Brick,s*2.56f,1.69f,3.57f,2.12f,3.46f,.69f);
            Block(wall,Finish.Brick,s*3.41f,1.68f,.16f,.42f,3.46f,6.81f);
            Block(wall,Finish.Brick,s*2.64f,1.63f,-3.21f,1.94f,3.34f,.44f);
            Block(wall,Finish.Concrete,s*3.42f,.22f,.15f,.54f,.48f,6.94f);
            Block(wall,Finish.Concrete,s*3.42f,3.41f,.15f,.58f,.19f,7.08f);
            foreach(float z in new[]{-2.97f,.34f,3.31f})
            {
                Block(wall,Finish.Brick,s*3.60f,1.75f,z,.38f,3.27f,.52f);
                Block(wall,Finish.Concrete,s*3.63f,.28f,z,.49f,.54f,.71f);
                Block(wall,Finish.Concrete,s*3.63f,3.27f,z,.49f,.21f,.71f);
            }
        }
        Block(wall,Finish.Concrete,0,.70f,3.57f,3.1f,1.46f,.84f);
        // The lintel has shed part of its lower face and shows the broken brick beneath.
        for(int i=0;i<7;i++)
        {
            float x=-1.59f+i*.45f, drop=i is 1 or 2 ? .18f : (i==3 ? .10f : .015f);
            float h=.44f-drop;
            Block(wall,Finish.Concrete,x+.225f,3.48f-h*.5f,3.57f,.453f,h,.84f);
            if(drop>.02f)Block(wall,Finish.BrokenBrick,x+.225f,3.048f+drop*.50f,3.52f,.46f,drop,.84f);
        }
        Block(wall,Finish.Timber,0,3.18f,-3.21f,3.58f,.32f,.50f);
        canopy.Gable(At(0,3.42f,3.49f),side,forward);
        canopy.Gable(At(0,3.32f,-3.29f),side,forward);
        canopy.Vault(At(0,3.32f,.18f),side,forward,3.42f,.82f,.29f,7.38f,false);
        wall.Vault(At(0,2.77f,3.97f),side,forward,1.48f,.29f,.21f,.17f,true);
        foreach(int s in new[]{-1,1})
            Block(wall,Finish.Concrete,s*1.58f,2.08f,3.96f,.23f,1.45f,.18f);
        foreach(float z in new[]{-3.45f,-.05f,3.75f})
            canopy.Vault(At(0,3.30f,z),side,forward,3.35f,.76f,.32f,.36f,true);
        int seed=(int)cx;
        TurfCap(canopy,At(0,0,.18f),side,forward,seed);
        foreach(int s in new[]{-1,1})
        {
            // The taper stops at both corners, clear of the aperture and the service doors.
            wall.Bank(At(s*3.65f,-.12f,-.18f),forward,side*s,6.15f,.86f,.48f,1.48f+.23f*s,.10f,seed+s);
            canopy.Bank(At(s*3.05f,3.44f,.19f),forward,side*s,6.45f,.62f,.41f,.63f,.94f,seed+11*s);
            Scar(wear,At(2.65f*s,3.18f,3.924f),side,forward,1.05f,.83f,seed+5*s);
            Scar(wear,At(s*3.625f,2.87f,1.68f),forward,side*s,1.26f,.65f,seed+8*s);
            // Partly detached bricks and fallen corner stones break the straight wall plinth.
            for(int row=0;row<4;row++)for(int n=0;n<4-row;n++)
            {
                float x=s*(2.30f+(row%2)*.14f+n*.30f);
                Block(wear,(row+n)%3==0?Finish.BrokenBrick:Finish.Brick,x,1.34f+row*.127f,
                    3.933f+n*.003f,.277f,.108f,.088f);
            }
            for(int i=0;i<7;i++)
            {
                float z=-2.5f+i*.74f;
                Vector3 p=At(s*(3.82f+.32f*Mathf.Sin(i*29+seed)),.16f+.18f*Mathf.Cos(i*5),z);
                var rot=Quaternion.AngleAxis(15f*Mathf.Sin(i*7f),forward)*Quaternion.AngleAxis(i*27f,U);
                wear.Box(i%3==0?Finish.Concrete:Finish.BrokenBrick,p,rot*side*.39f,rot*U*.16f,rot*forward*.21f);
            }
        }
        for(int row=0;row<3;row++)for(int n=0;n<5-row;n++)
            Block(canopy,(n+row)%3==0?Finish.BrokenBrick:Finish.Brick,-1.95f+n*.32f-row*.14f,
                4.06f+row*.14f,3.57f,.305f,.135f,.48f);
    }

    static void Gallery(Masonry lower,Masonry upper,Masonry canopy,Masonry bank,Masonry wear,float approach)
    {
        float x=46,z=1.1f;
        upper.Block(Finish.Concrete,V(x,Deck-.17f,z),6.6f,.34f,7.5f);
        // The gallery sits on the old bank above the tunnel crown.
        bank.Block(Finish.Brick,V(x+.36f,1.81f,z),4.12f,2.98f,5.56f);
        foreach(int s in new[]{-1,1})
        {
            bank.Bank(V(x+.36f+s*2.04f,.12f,z),F,R*s,6.59f,.99f,.79f,2.78f,.15f,19+s);
            bank.Bank(V(x+.36f,.12f,z+s*2.77f),R,F*s,5.4f,.85f,.67f,2.55f,.13f,42+s);
        }
        foreach(int end in new[]{-1,1})
        {
            foreach(int s in new[]{-1,1})
            {
                var pier=V(x+2.95f*s,Deck*.5f,z+3.46f*end);
                lower.Block(Finish.Brick,pier,1.04f,Deck,1.0f);
                lower.Block(Finish.Concrete,pier+U*(Deck*.5f-.26f),1.18f,.24f,1.13f);
            }
            upper.Block(Finish.Brick,V(x,Deck+.40f,z+3.59f*end),6.6f,.80f,.32f);
            upper.Block(Finish.Concrete,V(x,Deck+.84f,z+3.59f*end),6.7f,.12f,.46f);
        }
        upper.Block(Finish.Brick,V(x+3.14f,Deck+.43f,z),.32f,.86f,7.42f);
        upper.Block(Finish.Concrete,V(x+3.14f,Deck+.90f,z),.44f,.13f,7.5f);
        foreach(int sign in new[]{-1,1})
        {
            upper.Block(Finish.Brick,V(x-3.16f,Deck+.38f,z+sign*2.45f),.34f,.76f,2.60f);
            upper.Block(Finish.Concrete,V(x-3.16f,Deck+.80f,z+sign*2.45f),.45f,.12f,2.64f);
            foreach(float dz in new[]{-3.42f,0f,3.42f})
            {
                if(sign==-1 && dz==0)continue;
                upper.Block(Finish.Brick,V(x+sign*3f,Deck+1.47f,z+dz),.38f,2.94f,.48f);
            }
        }
        // A low lopsided turf cap returns into the side walls. Wide timber fascia is exposed at the worn eave.
        var roofBase=V(x,Deck+2.89f,z);
        for(int i=0;i<10;i++)
        {
            float dz=-3.91f+i*.78f;
            var left=roofBase+V(-3.48f,.07f+.05f*Mathf.Cos(i),dz);
            var right=roofBase+V(3.46f,.04f*Mathf.Sin(i),dz);
            var back=roofBase+V(.57f,.77f+.07f*Mathf.Sin(i+1),dz);
            foreach(var end in new[]{left,right})
            {
                var delta=back-end;
                canopy.Box(Finish.Timber,(end+back)*.5f+F*.39f,delta,U*.18f,F*.79f);
                canopy.Box((i==1 && end==left)?Finish.Earth:Finish.Turf,(end+back)*.5f+F*.39f+U*.15f,
                    delta,U*(.17f+.05f*Mathf.Cos(i*1.9f)),F*.79f);
            }
            if(i==0 || i==9)
            {
                var off=i==0?Vector3.zero:F*.78f;
                canopy.Prism(Finish.Timber,new[]{left+off,right+off,back+off},F*.15f);
            }
        }
        upper.Block(Finish.Brick,V(x+3.02f,Deck+1.86f,z+1.62f),.35f,2.12f,2.94f);
        upper.Block(Finish.Brick,V(x+3.01f,Deck+1.23f,z-.97f),.35f,.82f,2.52f);
        Scar(wear,V(x-3.34f,Deck+.68f,z-2.30f),F,-R,1.22f,.37f,17);
        foreach(float k in new[]{-3.35f,-1.67f,0f,1.67f,3.35f})
            canopy.Block(Finish.Timber,V(x,Deck+2.81f,z+k),6.88f,.20f,.20f);
        var start=V(OsowiecGalleryStairs.FootX,approach,z);
        var endRamp=V(OsowiecGalleryStairs.HeadX,Deck,z);
        var slope=endRamp-start;
        const int steps=OsowiecGalleryStairs.Treads;
        float run=slope.x/steps, rise=slope.y/steps;
        // A recessed supporting slab carries separate level masonry treads. The
        // exposed risers, bevelled stone lips and undercut sides read from above.
        upper.Box(Finish.Concrete,(start+endRamp)*.5f-U*.22f,slope,U*.25f,F*2.03f);
        for(int i=0;i<steps;i++)
        {
            float front=start.x+i*run, back=front+run, top=approach+(i+1)*rise;
            var treadCentre=V((front+back)*.5f,top-rise*.5f-.045f,z);
            upper.Block(Finish.Brick,treadCentre,run,rise+.085f,2.02f);
            float edge=front-.025f;
            // The two slabs share a flush walking surface with staggered bedding joints.
            float seam=z+((i&1)==0?.24f:-.18f);
            foreach(var interval in new[]{(z-1.064f,seam-.006f),(seam+.006f,z+1.064f)})
            {
                Vector3 p=V(edge,top,interval.Item1);
                upper.Prism(Finish.Concrete,new[]{p+U*(-.068f)+R*.01f,p-U*.013f,p+R*.017f,
                    V(back,top,interval.Item1),V(back,top-.068f,interval.Item1)},F*(interval.Item2-interval.Item1));
            }
        }
        foreach(float t in new[]{.27f,.55f,.83f})
        {
            var top=Vector3.Lerp(start,endRamp,t);
            var baseY=Ground(top.x,top.z)-.12f;
            float h=Mathf.Max(.15f,top.y-baseY-.14f);
            lower.Block(Finish.Brick,V(top.x,baseY+h*.5f,top.z),.53f,h,2.02f);
            lower.Block(Finish.Concrete,top-U*.23f,.72f,.22f,2.13f);
        }
        // The open central bay is the guard's standing space. The log table sits under the rear eave.
        var table=V(48.12f,Deck+.88f,1.65f);
        upper.Block(Finish.Timber,table,.72f,.08f,1.68f);
        foreach(int s in new[]{-1,1})
        {
            upper.Block(Finish.Timber,table+V(0,-.45f,.66f*s),.49f,.83f,.14f);
            upper.Block(Finish.Brass,table+V(-.24f,.044f,.59f*s),.06f,.012f,.19f);
        }
        upper.Block(Finish.Timber,table+V(-.04f,.078f,-.28f),.36f,.065f,.46f);
        foreach(int s in new[]{-1,1})
        {
            var offset=F*(1.08f*s);
            for(int i=1;i<=8;i++)
            {
                var foot=Vector3.Lerp(start,endRamp,i/8f)+offset;
                upper.Block(Finish.Iron,foot+U*.40f,.055f,.80f,.055f);
            }
            upper.Box(Finish.Timber,(start+endRamp)*.5f+offset+U*.82f,slope,U*.09f,F*.085f);
        }
    }

    static void WearMaterials()
    {
        var shader=Shader.Find("Steel and Ashes/Battery surface stains");
        if(!shader)throw new InvalidOperationException("Battery surface stain shader unavailable");
        void Set(string name,float kind,float opacity)
        {
            string path=Art+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.shader=shader;m.renderQueue=-1;
            m.SetFloat("_Kind",kind);m.SetFloat("_Opacity",opacity);
            EditorUtility.SetDirty(m);
        }
        Set("Battery wet floor",0,.42f);
        Set("Battery limewash",1,.18f);
        Set("Battery seepage",2,.19f);
    }

    static void PassageWear(Masonry wear,float south,float north)
    {
        for(int i=0;i<16;i++)
        {
            float z=-8f+i*1.13f, x=TunnelX+.88f*Mathf.Sin(i*4.82f);
            float y=PassageHeight(z,south,north);
            var axis=F+U*(PassageHeight(z+.1f,south,north)-y)*10f;
            if(Mathf.Abs(z+4.5f)<.79f || Mathf.Abs(z-5.5f)<.79f)continue;
            WetPatch(wear,V(x,y+.010f,z),R*(.17f+.20f*Mathf.Abs(Mathf.Sin(i))),axis*(.38f+.32f*Mathf.Abs(Mathf.Cos(i))),11+i);
        }
        foreach(int sign in new[]{-1,1})
        {
            for(int panel=0;panel<4;panel++)
            {
                var p=V(TunnelX+sign*1.516f,Bottom+1.53f,-3.58f+panel*2.24f+.26f*sign);
                Scar(wear,p,F,-R*sign,.85f+.2f*Mathf.Sin(panel+sign),.53f+.21f*Mathf.Cos(panel*5),panel+5*sign);
            }
            for(int i=0;i<9;i++)
            {
                float x=TunnelX+sign*(1.26f-.31f*Mathf.Sin(i*2.7f)),z=-7.3f+i*.32f;
                var centre=V(x,PassageHeight(z,south,north)+.018f,z);
                var rot=Quaternion.AngleAxis(i*27,U);
                wear.Box(i%3==0?Finish.BrokenBrick:Finish.Concrete,centre,rot*R*.095f,U*.045f,rot*F*.14f);
            }
        }
    }

    static void Lamp(Transform root,Vector3 p,float strength,float range)
    {
        var lamp=new GameObject("Свет крепостного фонаря").AddComponent<Light>();
        lamp.transform.SetParent(root,false);lamp.transform.position=p;
        lamp.color=new Color(1f,.77f,.53f);lamp.range=range;lamp.intensity=strength;lamp.shadows=LightShadows.None;
    }

    public static Renderer Install(Transform fort)
    {
        var old=fort.Find(Name);
        if(old)Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();
        // Sample intact terrain beyond the excavated triangles; repeated bakes yield the same ramps.
        float south=Ground(TunnelX,-13)+.025f, north=Ground(TunnelX,15)+.025f, approach=Ground(35.3f,1.1f)+.04f;
        var root=new GameObject(Name).transform;root.SetParent(fort,false);
        Excavate(fort,south,north);
        WearMaterials();
        var lower=new Masonry();var upper=new Masonry();var canopy=new Masonry();var wear=new Masonry();
        var northCover=new Masonry();var southCover=new Masonry();var galleryCover=new Masonry();var bank=new Masonry();
        Corridor(lower,canopy,south,north);
        PassageWear(wear,south,north);
        Chamber(lower,northCover,wear,32,9,1.19f);
        Chamber(lower,southCover,wear,40,-8,2.10f);
        Gallery(lower,upper,galleryCover,bank,wear,approach);
        void Lantern(Vector3 anchor,Vector3 outwards)
        {
            var p=anchor+outwards*.26f;
            var side=Vector3.Cross(U,outwards);
            wear.Box(Finish.Iron,anchor+U*.18f,side*.18f,U*.32f,outwards*.045f);
            wear.Box(Finish.Iron,(anchor+p)*.5f+U*.20f,side*.043f,U*.045f,outwards*.32f);
            wear.Block(Finish.Iron,p+U*.19f,.29f,.10f,.28f);
            wear.Block(Finish.Glass,p,.16f,.28f,.16f);
            wear.Block(Finish.Brass,p-U*.16f,.22f,.055f,.22f);
            foreach(int s in new[]{-1,1})wear.Block(Finish.Iron,p+V(s*.081f,0,-.082f),.018f,.28f,.018f);
            Lamp(root,p+outwards*.15f,4.50f,6.4f);
        }
        var first=V(Mathf.Cos(1.19f),0,Mathf.Sin(1.19f));
        var second=V(Mathf.Cos(2.10f),0,Mathf.Sin(2.10f));
        Lantern(V(42.80f,Deck+1.72f,-2.32f),-R);
        Lantern(V(40,2.16f,-8)-Vector3.Cross(U,second)*2.12f+second*3.92f,second);
        Lantern(V(32,2.02f,9)+Vector3.Cross(U,first)*3.63f-first*.72f,Vector3.Cross(U,first));
        lower.Save(root,"Battery passage and chambers").gameObject.AddComponent<OsowiecAcousticSurface>().surface=OsowiecGroundSurface.Brick;
        upper.Save(root,"Battery gallery and ramp").gameObject.AddComponent<OsowiecAcousticSurface>().surface=OsowiecGroundSurface.Brick;
        wear.Save(root,"Battery limewash seepage and rubble",false);
        var roof=canopy.Save(root,"Battery vaults and turf");
        northCover.Save(root,"Battery north pavilion roof");
        southCover.Save(root,"Battery south pavilion roof");
        galleryCover.Save(root,"Battery gallery roof");
        bank.Save(root,"Battery gallery earthwork");
        InstallStores(root);
        InstallRimDebris(fort,root);
        OsowiecBatteryFinish.Install(fort,root);
        var cutaway=fort.GetComponent<OsowiecFortCutaway>();
        if(cutaway)
        {
            cutaway.surfaces=cutaway.surfaces.Where(r=>r && r!=roof).Append(roof).ToArray();
            cutaway.batteryRoof=roof.GetComponent<MeshCollider>();
            OsowiecCeilings.Bake(cutaway);
        }
        foreach(float z in new[]{-3.5f,4.5f})Lamp(root,V(TunnelX+1.20f,Bottom+1.84f,z),3.6f,6.5f);
        Physics.SyncTransforms();
        return roof;
    }

    public static void VerifyPaths()
    {
        Vector3 Nav(Vector3 p)
        {
            if(!NavMesh.SamplePosition(p,out var hit,.75f,NavMesh.AllAreas) || Mathf.Abs(hit.position.y-p.y)>.62f)
                throw new InvalidOperationException("Missing battery floor at "+p);
            return hit.position;
        }
        void Path(Vector3 a,Vector3 b)
        {
            var route=new NavMeshPath();
            if(!NavMesh.CalculatePath(Nav(a),Nav(b),NavMesh.AllAreas,route) || route.status!=NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException($"Battery route disconnected: {a} -> {b}");
        }
        var inner=V(TunnelX,Bottom,.5f);
        Path(V(TunnelX,Ground(TunnelX,-12),-12),inner);
        Path(inner,V(TunnelX,Ground(TunnelX,14),14));
        Path(OsowiecOperation.Battery,GalleryPost);
        Path(OsowiecOperation.Battery,OsowiecOperation.NorthLine);
        Path(OsowiecOperation.Battery,OsowiecOperation.SouthLine);
        foreach(var exhibit in new[]{(32f,9f,1.19f),(40f,-8f,2.10f)})
        {
            var centre=V(exhibit.Item1,0,exhibit.Item2);
            var forward=V(Mathf.Cos(exhibit.Item3),0,Mathf.Sin(exhibit.Item3));
            var side=Vector3.Cross(U,forward);
            var front=centre+forward*5.2f+U*2.70f;
            var visible=Physics.RaycastAll(front,-forward,2f).OrderBy(h=>h.distance).FirstOrDefault();
            if(!visible.collider || visible.collider.name!="Battery_statuary")
                throw new InvalidOperationException("Barrel exhibit is hidden at "+centre);
            bool MasonryAt(float offset) => Physics.RaycastAll(front+side*offset,-forward,2.2f)
                .Any(h=>h.collider.name=="Battery passage and chambers");
            if (MasonryAt(.90f) || !MasonryAt(2.45f))
                throw new InvalidOperationException("Aperture is closed or lacks a real pier at "+centre);
        }
        var shell=Physics.RaycastAll(inner+U*.35f,U,3);
        if(!shell.Any(h=>h.collider.name=="Battery vaults and turf"))throw new InvalidOperationException("Missing tunnel ceiling");
        for(float z=-4.5f;z<=5.5f;z+=.25f)
            if(Physics.CheckCapsule(V(TunnelX,Bottom+.36f,z),V(TunnelX,Bottom+1.47f,z),.28f,~0,QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Soldier capsule blocked in lower passage at "+z);
        VerifyController();
        Debug.Log("PASS battery fort: both entrances, lower corridor capsule clearance, gallery, both barrel apertures and terminals.");
    }

    static void VerifyController()
    {
        var host=new GameObject("Temporary battery route walker");
        try
        {
            var walker=host.AddComponent<CharacterController>();
            walker.height=1.78f;walker.radius=.29f;walker.center=U*.9f;
            walker.stepOffset=.29f;walker.skinWidth=.035f;walker.minMoveDistance=0;
            void Place(float x,float z)
            {
                walker.enabled=false;
                host.transform.position=V(x,Ground(x,z)+.05f,z);
                walker.enabled=true;Physics.SyncTransforms();
            }
            float Walk(Vector3 target,bool gravity=false)
            {
                float lowest=host.transform.position.y, fall=-2f;
                const float dt=.033f/1.6f;
                for(int i=0;i<2600;i++)
                {
                    var delta=target-host.transform.position;delta.y=0;
                    if(delta.magnitude<.055f)return lowest;
                    if(gravity)fall=walker.isGrounded ? -2f : Mathf.Max(-12f,fall-9.81f*dt);
                    walker.Move(delta.normalized*.033f+U*(gravity ? fall*dt : -.013f));
                    lowest=Mathf.Min(lowest,host.transform.position.y);
                }
                throw new InvalidOperationException($"Battery capsule stopped at {host.transform.position} heading to {target}");
            }
            Place(TunnelX,-12.15f);
            float lowest=Walk(V(TunnelX,0,14.3f));
            float exit=host.transform.position.y;
            Place(35.08f,1.1f);
            Walk(GalleryPost);
            if(lowest>Bottom+.20f || exit<1.45f || Mathf.Abs(host.transform.position.y-Deck)>.2f)
                throw new InvalidOperationException($"Wrong physical floor: passage {lowest}, exit {exit}, gallery {host.transform.position}");
            float galleryY=host.transform.position.y;
            Walk(V(34.55f,0,1.1f),true);
            // Allow for a final descending step and the finite capsule radius at the toe.
            for(int i=0;i<30 && !walker.isGrounded;i++)walker.Move(-U*(.012f+.004f*i));
            float yard=Ground(host.transform.position.x,host.transform.position.z);
            if(!walker.isGrounded || Mathf.Abs(host.transform.position.y-yard)>.23f)
                throw new InvalidOperationException($"Descending gallery stairs left the walker at {host.transform.position}, terrain {yard:F3}, grounded={walker.isGrounded}.");
            Debug.Log($"PASS battery physical floors: passage {lowest:F3}, exit {exit:F3}, gallery {galleryY:F3}, stair descent {host.transform.position.y:F3}.");
        }
        finally {Object.DestroyImmediate(host);}
    }

    public static void PlaceObserver()
    {
        var guard=Object.FindObjectsByType<Unit>(FindObjectsInactive.Include)
            .Single(u=>u.name=="Дозор у бастиона");
        var cc=guard.GetComponent<CharacterController>();
        if(!NavMesh.SamplePosition(GalleryPost,out var floor,.5f,NavMesh.AllAreas))
            throw new InvalidOperationException("Upper observer has no walkable gallery.");
        var feet=Physics.RaycastAll(floor.position+U*.30f,-U,.75f,~0,QueryTriggerInteraction.Ignore)
            .Single(h=>h.collider.name=="Battery gallery and ramp").point+U*.035f;
        bool previous=cc.enabled;cc.enabled=false;
        guard.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,-88f,0));
        cc.enabled=previous;
        var grid=Object.FindAnyObjectByType<ChessGrid>();
        if(grid)guard.currentGridPosition=grid.WorldToGridCoords(feet);
        EditorUtility.SetDirty(guard);EditorUtility.SetDirty(guard.transform);
        Debug.Log($"PASS gallery post: {guard.name}, {guard.chessType}, {feet}, active={guard.gameObject.activeSelf}.");
    }

    [MenuItem("Steel and Ashes/Bake eastern battery and navigation")]
    public static void BakeCurrentScene()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=Mission) scene=EditorSceneManager.OpenScene(Mission);
        var fort=GameObject.Find("Osowiec · Fort I");
        var surface=Object.FindObjectsByType<NavMeshSurface>().Single(s=>s.enabled);
        Install(fort.transform);
        OsowiecApproachClosures.RebuildNavigation(surface, VerifyPaths);
        PlaceObserver();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    [MenuItem("Steel and Ashes/Apply playtest environment revisions")]
    public static void RefreshPlaytestEnvironment()
    {
        OsowiecHeadquartersBuilder.RefreshDugoutLightAndGrain();
        var scene=EditorSceneManager.OpenScene(Mission,OpenSceneMode.Single);
        var fort=GameObject.Find("Osowiec · Fort I").transform;
        var battery=fort.Find(Name);
        if(!battery)throw new InvalidOperationException("Eastern battery root is missing.");
        Physics.SyncTransforms();
        InstallApproachCover(battery);
        var surface=Object.FindObjectsByType<NavMeshSurface>().Single(s=>s.enabled);
        OsowiecApproachClosures.RebuildNavigation(surface,VerifyPaths);
        OsowiecHeadquartersBuilder.UpdateDeploymentSheet();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save revised approach cover.");
        AssetDatabase.SaveAssets();
        Debug.Log("Refreshed dugout lamps, approach cover, deployment plan and navigation.");
    }
}
