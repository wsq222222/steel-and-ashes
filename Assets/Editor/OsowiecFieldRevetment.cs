using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-baked breastwork. Each section follows the surveyed cut and uses one mesh
// for its soil, timbers, wicker repairs and bindings. The wagon gate has its own rig.
public static class OsowiecFieldRevetment
{
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    const string Folder = Art + "Field revetment";
    const int Soil = 0, Timber = 1, Sapwood = 2, Withy = 3, Cord = 4, Sedge = 5;
    const float Tau = 2 * Mathf.PI;
    static readonly Vector3 Up = Vector3.up;

    sealed class Section
    {
        readonly Func<Vector3, float> terrain;
        readonly System.Random random;
        readonly Vector2[] knots;
        readonly float[] stations;
        readonly List<Vector3> vertices = new(), normals = new();
        readonly List<Vector2> uv = new();
        readonly List<Color32> paint = new();
        readonly List<int>[] indices = { new(), new(), new(), new(), new(), new() };
        readonly int seed;
        readonly float length;

        public Section(Vector2[] knots, Func<Vector3, float> terrain)
        {
            this.terrain = terrain;
            this.knots = knots;
            seed = Mathf.RoundToInt(2904 + Mathf.Abs(knots[0].y) * 371 + knots[0].x * 11);
            random = new System.Random(seed);
            stations = new float[knots.Length];
            for (int i = 1; i < knots.Length; i++)
                stations[i] = stations[i - 1] + Vector2.Distance(knots[i - 1], knots[i]);
            length = stations[stations.Length - 1];
        }

        float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        float Noise(float s, float rate, float offset) => Mathf.PerlinNoise(s * rate + offset, seed * .013f);

        Vector3 Plan(float s)
        {
            int i = 1;
            while (i < knots.Length - 1 && s > stations[i]) i++;
            var p = Vector2.Lerp(knots[i - 1], knots[i], Mathf.InverseLerp(stations[i - 1], stations[i], s));
            return new Vector3(p.x, 0, p.y);
        }

        Vector3 Front(float s)
        {
            var normal = Vector3.Cross(Up, Plan(s + .28f) - Plan(s - .28f)).normalized;
            return normal.x > 0 ? -normal : normal;
        }

        Vector3 Ground(float s, float lateral)
        {
            var p = Plan(s) + Front(s) * lateral;
            p.y = terrain(p);
            return p;
        }

        float Crown(float s)
        {
            // Smooth gradients through the joints between the original collision bays.
            float baseY = (terrain(Plan(s - .42f)) + 2 * terrain(Plan(s)) + terrain(Plan(s + .42f))) * .25f;
            return baseY + 1.53f + .32f * Noise(s, .32f, 11) + .08f * Noise(s, 1.65f, 4);
        }

        int Vertex(Vector3 p, Vector3 n, Vector2 tex, float sod = .0f)
        {
            int i = vertices.Count;
            vertices.Add(p);
            normals.Add(n.normalized);
            uv.Add(tex);
            paint.Add(new Color32((byte)Mathf.RoundToInt(255 * Mathf.Clamp01(sod)), 255, 255, 255));
            return i;
        }

        void Triangle(int slot, int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), outward) < 0)
                (b, c) = (c, b);
            indices[slot].Add(a);
            indices[slot].Add(b);
            indices[slot].Add(c);
        }

        void Quad(int slot, int a, int b, int c, int d, Vector3 outward)
        {
            Triangle(slot, a, b, c, outward);
            Triangle(slot, a, c, d, outward);
        }

        // Three-quarter logs have uneven longitudinal facets, a slight crook, a
        // skewed axe-cut end and cylindrical UVs aligned with the growth direction.
        void Pole(Vector3 a, Vector3 b, float width, float depth, int slot, int sides = 8, bool hewn = false)
        {
            Vector3 span = b - a;
            float extent = span.magnitude;
            if (extent < .001f) return;
            Vector3 axis = span / extent;
            Vector3 right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .94f ? Vector3.forward : Up).normalized;
            Vector3 across = Vector3.Cross(axis, right).normalized;
            float u = Range(.01f, .9f), v = Range(0, 1), taper = Range(.84f, .97f);
            float phase = Range(-.17f,.17f), bend = Range(-.20f,.20f) * width;
            var rings = new int[4, sides + 1];
            var profile = new float[sides];
            var setbacks = new float[sides];
            for (int j = 0; j < sides; j++)
            {
                profile[j] = Range(.88f, 1.07f);
                setbacks[j] = Range(-.13f, .13f) * width;
            }
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f, bow = Mathf.Sin(Mathf.PI * t);
                var centre = a + span * t + (right * bend + across * depth * .07f) * bow;
                for (int j = 0; j <= sides; j++)
                {
                    int k = j % sides;
                    float angle = phase + Tau * k / sides;
                    float x = Mathf.Cos(angle), y = Mathf.Sin(angle);
                    var n = (right * x / width + across * y / depth).normalized;
                    float radius = profile[k] * Mathf.Lerp(1, taper, t);
                    float sideX = hewn ? Mathf.Clamp(x, -.78f, .78f) : x;
                    var p = centre + (right * (width * sideX) + across * (depth * y)) * radius;
                    if (i == 0 || i == 3) p += axis * setbacks[k] * (i == 0 ? -.7f : 1);
                    // The scan contains several boards side by side. Sample a narrow
                    // stripe across an individual log and propagate the grain along it.
                    rings[i,j] = Vertex(p, n, new Vector2(u + (float)j / sides * width * 1.12f, v + extent * t * .47f));
                }
            }
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < sides; j++)
            {
                int a0 = rings[i,j], b0 = rings[i,j + 1], c0 = rings[i + 1,j + 1], d0 = rings[i + 1,j];
                Quad(slot, a0, b0, c0, d0, normals[a0] + normals[b0]);
            }
            for (int end = 0; end < 2; end++)
            {
                var n = end == 0 ? -axis : axis;
                var c = end == 0 ? a : b;
                int hub = Vertex(c, n, new Vector2(u, v)), first = vertices.Count;
                for (int j = 0; j < sides; j++)
                {
                    var p = vertices[rings[end * 3,j]];
                    Vertex(p, n, new Vector2(u + Vector3.Dot(p - c, right), v + Vector3.Dot(p - c, across)));
                }
                for (int j = 0; j < sides; j++)
                    Triangle(slot == Timber ? Sapwood : slot, hub, first + j, first + (j + 1) % sides, n);
            }
        }

        // Both feet penetrate the actual terrain independently. A lower tongue
        // of washed soil covers individual courses on the exposed western face.
        void Embankment()
        {
            int sections = Mathf.CeilToInt(length / .23f);
            const int columns = 8;
            var profile = new Vector3[sections + 1, columns];
            var cover = new float[sections + 1, columns];
            for (int i = 0; i <= sections; i++)
            {
                float s = length * i / sections;
                float crest = Crown(s);
                float wash = Noise(s, .40f, 37), crumb = Noise(s, 1.65f, 19);
                float frontWidth = .52f + wash * .47f;
                float rearWidth = .77f + .29f * Noise(s, .43f, 59);
                float runout = Mathf.Clamp01(Mathf.Min(s, length - s) / .52f);
                var toe = Ground(s, frontWidth);
                var heel = Ground(s, -rearWidth);
                var p = Ground(s, 0);
                var side = Front(s);
                float face = .106f + .046f * crumb, slump = .22f + .54f * wash + .22f * crumb;
                Vector3 Point(float x, float y) => new(p.x + side.x*x, y, p.z + side.z*x);
                profile[i,0] = toe - Up * .08f;
                profile[i,1] = Point(frontWidth*.66f, Mathf.Lerp(toe.y,p.y+slump,.22f) + .06f*crumb);
                profile[i,2] = Point(.19f + .09f*wash, p.y + slump);
                profile[i,3] = Point(face - .10f, crest - .42f - .15f*wash);
                profile[i,4] = Point(face + .012f, crest - .10f - .09f*crumb);
                profile[i,5] = Point(-.13f - .11f*crumb, crest + .018f*crumb);
                profile[i,6] = Point(-rearWidth*.62f, Mathf.Lerp(heel.y, crest,.54f) + (crumb-.4f)*.14f);
                profile[i,7] = heel - Up*.09f;
                for (int j=1;j<columns-1;j++)
                {
                    // Pin the cross section onto the actual opening, so the earth
                    // cannot hang over the road or protrude from an open end.
                    var q = profile[i,j];
                    q.y = Mathf.Lerp(Mathf.Min(q.y, terrain(q))-.03f, q.y, .63f+.37f*runout);
                    profile[i,j]=q;
                }
                for (int j=0;j<columns;j++)
                    cover[i,j] = (j == 4 || j == 5 ? .56f : j == 6 ? .39f : .08f) + .29f*wash;
            }
            int first = vertices.Count;
            for (int i=0;i<=sections;i++)
            for (int j=0;j<columns;j++)
            {
                var along = profile[Mathf.Min(i+1,sections),j]-profile[Mathf.Max(0,i-1),j];
                var across = profile[i,Mathf.Min(j+1,columns-1)]-profile[i,Mathf.Max(0,j-1)];
                var n = Vector3.Cross(along,across).normalized;
                if (n.y<0) n=-n;
                var p=profile[i,j];
                Vertex(p,n,new Vector2(p.x,p.z),cover[i,j]);
            }
            for (int i=0;i<sections;i++)
            for (int j=0;j<columns-1;j++)
            {
                int a=first+i*columns+j, b=a+columns;
                Quad(Soil,a,b,b+1,a+1,normals[a]+normals[b+1]);
            }
            // Sedge survives in small groups on moist shoulders. Anchor each
            // folded blade to the modelled crest at the exact cross section.
            for(int i=4;i<sections-3;)
            {
                int clump=random.Next(2,5);
                for(int k=0;k<clump && i<sections-3;k++,i++)
                {
                    var root=Vector3.Lerp(profile[i,4],profile[i,5],Range(.28f,.97f));
                    Tuft(root-Up*.034f);
                }
                i+=random.Next(3,12);
            }
            for(int end=0;end<=sections;end+=sections)
            {
                Vector3 n = (Plan(end==0 ? 0 : length)-Plan(end==0 ? .2f : length-.2f)).normalized;
                int fan=vertices.Count;
                for(int j=0;j<columns;j++) Vertex(profile[end,j],n,Vector2.zero,cover[end,j]);
                // Each upper edge lands directly on its foot, including concave sections.
                int floor=vertices.Count;
                for(int j=0;j<columns;j++)
                {
                    var p=profile[end,j]; p.y=Mathf.Min(p.y,terrain(p)-.13f);
                    Vertex(p,n,Vector2.zero);
                }
                for(int j=0;j<columns-1;j++) Quad(Soil,fan+j,fan+j+1,floor+j+1,floor+j,n);
            }
        }

        void Tuft(Vector3 root)
        {
            int leaves=random.Next(8,16);
            float phase=Range(0,Tau), size=Range(.65f,1.08f);
            for(int j=0;j<leaves;j++)
            {
                float angle=phase+j*2.4f+Range(-.30f,.30f);
                var lean=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                var side=Vector3.Cross(Up,lean);
                var foot=root+lean*Range(.008f,.052f);
                float height=Range(.14f,.32f)*size,reach=Range(.08f,.24f)*size;
                float width=Range(.005f,.011f)*size;
                int first=vertices.Count;
                for(int h=0;h<=3;h++)
                {
                    float t=h/3f;
                    var mid=foot+Up*(height*t*(1-.57f*t))+lean*(reach*t*t);
                    var n=Vector3.Cross(side,Up*height*(1-1.14f*t)+lean*(2*reach*t)).normalized;
                    float w=width*(1-.95f*t);
                    Vertex(mid-side*w,n+side*.3f,new Vector2(0,t),t);
                    Vertex(mid+n*w*.34f,n,new Vector2(.5f,t),t);
                    Vertex(mid+side*w,n-side*.3f,new Vector2(1,t),t);
                    if(h==0) continue;
                    int a=first+(h-1)*3;
                    Quad(Sedge,a,a+3,a+4,a+1,n);
                    Quad(Sedge,a+1,a+4,a+5,a+2,n);
                }
            }
        }

        Vector3 Course(float s, float t)
        {
            var g = Ground(s, .163f + .023f * (1-t));
            g.y = Mathf.Lerp(g.y-.10f, Crown(s)-.23f, t);
            return g;
        }

        void Logs(float a, float b)
        {
            const int rows = 8;
            for(int row=0;row<rows;row++)
            {
                float t=(row+.4f)/rows;
                var start = Course(a,t);
                var end = Course(b,t);
                var axis=(end-start).normalized;
                int slot=random.Next(6)==0 ? Sapwood : Timber;
                float thick=(Course((a+b)*.5f,1).y-Course((a+b)*.5f,0).y)/rows;
                Pole(start-axis*Range(.01f,.065f),end+axis*Range(.015f,.078f),
                    Range(.11f,.15f),thick*Range(.60f,.72f),slot,9,true);
            }
        }

        // Flexible willow woven in front of the original sleepers braces a washed-out bay.
        void Wattle(float a, float b)
        {
            int stakes=Mathf.Max(3,Mathf.RoundToInt((b-a)/.34f));
            for(int j=0;j<=stakes;j++)
            {
                float s=Mathf.Lerp(a,b,(float)j/stakes);
                var bottom=Course(s,.06f)+Front(s)*.19f;
                var top=Course(s,Range(.72f,.90f))+Front(s)*.18f;
                Pole(bottom,top,Range(.024f,.035f),.031f,Withy,6);
            }
            for(int row=0;row<10;row++)
            {
                float t=.20f+row*.057f + Range(-.006f,.006f);
                var last=Vector3.zero;
                for(int j=0;j<=2*stakes;j++)
                {
                    float s=Mathf.Lerp(a,b,(float)j/(2*stakes));
                    var q=Course(s,t)+Front(s)*(.19f+.047f*Mathf.Cos(Mathf.PI*(j*.5f+row)));
                    q.y += .026f*Mathf.Sin(j*.73f + row*.32f);
                    if(j>0) Pole(last,q,.026f,.022f,Withy,6);
                    last=q;
                }
            }
        }

        void Upright(float s, bool lash)
        {
            var f=Front(s);
            var tangent= (Plan(s+.25f)-Plan(s-.25f)).normalized;
            var a=Ground(s,.34f)-Up*.10f;
            var b=a-f*Range(.035f,.105f)+tangent*Range(-.079f,.079f);
            b.y=Crown(s)+Range(-.13f,.095f);
            float radius=Range(.086f,.118f);
            Pole(a,b,radius,radius*1.13f,Timber,9,true);
            if(!lash) return;
            float at=Range(.57f,.78f);
            var c=Vector3.Lerp(a,b,at);
            // Lashings pass around both the upright and the sleeper behind it.
            var rear=c-f*.20f;
            for(int turn=0;turn<2;turn++)
            {
                Vector3 last=Vector3.zero;
                for(int j=0;j<=12;j++)
                {
                    float angle=j*Tau/12;
                    var p=rear+f*(.151f+.183f*Mathf.Cos(angle))+tangent*(radius*1.18f*Mathf.Sin(angle));
                    p.y=c.y+turn*.034f+.025f*Mathf.Sin(angle+.3f);
                    if(j>0) Pole(last,p,.013f,.014f,Cord,5);
                    last=p;
                }
            }
        }

        public void Build(Transform run,string id)
        {
            Embankment();
            float a=0;
            int bay=0;
            while(a<length-.001f)
            {
                float b=Mathf.Min(length,a+Range(1.45f,2.60f));
                if(length-b<.92f) b=length;
                Logs(a,b);
                if(bay%5==2 && b-a>1.45f) Wattle(a+.11f,b-.12f);
                Upright(a,bay%3!=1);
                a=b;
                bay++;
            }
            Upright(length,false);
            var path=Folder+"/"+id+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh) {mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear(); mesh.name=id;
            mesh.indexFormat=vertices.Count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetColors(paint);
            mesh.subMeshCount=indices.Length;
            for(int i=0;i<indices.Length;i++) mesh.SetTriangles(indices[i],i,false);
            mesh.RecalculateTangents();mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var art=new GameObject("Дерновая брустверная стенка");
            art.transform.SetParent(run,false); art.isStatic=true;
            art.AddComponent<MeshFilter>().sharedMesh=mesh;
            art.AddComponent<MeshRenderer>().sharedMaterials=Materials();
        }
    }

    static Material[] Materials()
    {
        Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>(Art+name+".mat");
        Material Wood(string name,Color tint,float wetness)
        {
            string path=Folder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                material=new Material(Existing("Adzed timber"));
                AssetDatabase.CreateAsset(material,path);
            }
            material.SetColor("_BaseColor",tint);
            material.SetFloat("_Smoothness",wetness);
            material.SetFloat("_ReliefUV",.003f);
            EditorUtility.SetDirty(material);
            return material;
        }
        return new[] {Existing("Bank growth"),Wood("Water-worn wood",new Color(.69f,.79f,.80f),.63f),
            Wood("Exposed fibres",new Color(.88f,.89f,.83f),.42f),
            Wood("Willow repairs",new Color(.61f,.66f,.56f),.66f),Existing("Hemp ties"),Existing("Sedge")};
    }

    public static void Bake(Transform run,Vector2[] knots,Func<Vector3,float> terrain)
    {
        if(!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder(Art.TrimEnd('/'),"Field revetment");
        string id="Retaining bank "+Mathf.RoundToInt(knots[0].y*100);
        new Section(knots,terrain).Build(run,id);
    }
}
