using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Dress the two existing room renderers in place so their overhead samples and cutaway stay paired.
public static class OsowiecFieldRoofs
{
    const string Art = "Assets/Osowiec/Art/Fort1916/";
    const string ScenePath = OsowiecProjectPaths.OperationScene;
    const string SodAsset = "Main casemate living turf";
    const string WardAsset = "Hospital lashed rain fly";
    const float West = -53.66f, East = -42.72f, Front = -12.80f, Rear = -7f;
    static readonly float[] Shoulder = {-6.2f,-4.5f,-2.6f,-1.6f,0,1.6f,2.6f,4.9f,6.5f};
    static readonly float[] Profile = {.16f,1.9f,3.63f,4.19f,4.37f,4.18f,3.68f,1.48f,.16f};
    static readonly float[] Ties = {-53.46f,-49.87f,-46.40f,-42.94f};
    const int CanvasX=175,CanvasZ=100;
    static readonly RectInt[] Tears={
        new RectInt(17,25,7,6),new RectInt(23,30,5,4),new RectInt(82,68,9,7),
        new RectInt(89,76,5,4),new RectInt(119,19,11,7),new RectInt(143,47,6,5),
        new RectInt(42,76,6,6),new RectInt(154,83,6,4)
    };
    static readonly Vector3 Up = Vector3.up;
    static float Noise(float x,float z) => Mathf.PerlinNoise(31.19f+x,73.67f+z);
    static float Smooth(float a,float b,float value) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
    static T Asset<T>(string file) where T: UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(Art+file) ?? throw new InvalidOperationException("Missing roof art: "+file);

    internal struct Sample
    {
        public Vector3 p; public Vector2 uv; public Color weather;
        public Sample(Vector3 p, Vector2 uv, Color weather) { this.p=p;this.uv=uv;this.weather=weather; }
    }

    internal sealed class Geometry
    {
        readonly List<Vector3> positions = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color32> weather = new();
        readonly List<int>[] triangles;
        internal Geometry(int surfaces) { triangles=Enumerable.Range(0,surfaces).Select(_=>new List<int>()).ToArray(); }
        internal int Vertex(Sample value)
        {
            int i=positions.Count;positions.Add(value.p);uvs.Add(value.uv);weather.Add(value.weather);return i;
        }
        internal void Tri(int slot,int a,int b,int c)
        {
            var t=triangles[slot];t.Add(a);t.Add(b);t.Add(c);
        }
        internal void Grid(int slot,int nx,int nz,Func<float,float,Sample> point,Func<int,int,bool> leaveOpen=null)
        {
            int start=positions.Count;
            for(int j=0;j<=nz;j++)
            for(int i=0;i<=nx;i++)
            {
                int a=Vertex(point(i/(float)nx,j/(float)nz));
                if(i==0||j==0|| (leaveOpen!=null && leaveOpen(i-1,j-1)))continue;
                int left=a-1,back=a-nx-1,corner=back-1;
                if(((i+j)&1)==0) {Tri(slot,corner,left,a);Tri(slot,corner,a,back);}
                else {Tri(slot,corner,left,back);Tri(slot,left,a,back);}
            }
            if(positions.Count-start!=(nx+1)*(nz+1))throw new InvalidOperationException("Incomplete roof panel.");
        }
        internal void Cord(int slot,Func<float,Vector3> path,float radius,int segments,int sides=7)
        {
            Vector3 prior=Vector3.zero;
            for(int s=0;s<=segments;s++)
            {
                float u=s/(float)segments;Vector3 p=path(u);
                Vector3 forward=(path(Mathf.Min(1,u+.002f))-path(Mathf.Max(0,u-.002f))).normalized;
                Vector3 axis=s==0 ? Vector3.Cross(forward,Mathf.Abs(forward.y)<.91f ? Up:Vector3.right)
                    : prior-forward*Vector3.Dot(prior,forward);
                axis.Normalize();prior=axis;
                Vector3 second=Vector3.Cross(forward,axis);
                for(int k=0;k<=sides;k++)
                {
                    float v=k/(float)sides,angle=v*Mathf.PI*2;
                    var radial=radius*(Mathf.Cos(angle)*axis+Mathf.Sin(angle)*second);
                    int i=Vertex(new Sample(p+radial,new Vector2(u*segments*.19f,v),new Color(.3f,.55f,.62f,1)));
                    if(s==0||k==0)continue;
                    int a=i-sides-2;Tri(slot,a,a+1,i);Tri(slot,a,i,i-1);
                }
            }
        }
        internal void Save(MeshRenderer renderer,string label,params Material[] materials)
        {
            if(materials.Length!=triangles.Length || triangles.Any(t=>t.Count==0))throw new InvalidOperationException(label+" has an empty surface.");
            var tf=renderer.transform;var inverse=tf.worldToLocalMatrix;
            string path=Art+label+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh) {mesh=new Mesh{name=label};AssetDatabase.CreateAsset(mesh,path);} else mesh.Clear();
            mesh.indexFormat=positions.Count>65535 ? IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(positions.Select(inverse.MultiplyPoint3x4).ToList());mesh.SetUVs(0,uvs);mesh.SetColors(weather);
            mesh.subMeshCount=triangles.Length;
            for(int s=0;s<triangles.Length;s++)
            {
                var t=triangles[s];
                if(inverse.determinant<0)
                    for(int i=0;i<t.Count;i+=3) (t[i+1],t[i+2])=(t[i+2],t[i+1]);
                mesh.SetTriangles(t,s);
            }
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            var filter=renderer.GetComponent<MeshFilter>();filter.sharedMesh=mesh;
            renderer.sharedMaterials=materials;
            EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);
        }
    }

    static Material Cover(string name,string scan, Color tint,bool earth=false)
    {
        string path=Art+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader=Shader.Find("Steel and Ashes/Weathered roof covering");
        if(!shader)throw new InvalidOperationException("Missing roof covering shader.");
        if(!mat){mat=new Material(shader){name=name};AssetDatabase.CreateAsset(mat,path);}
        mat.shader=shader;mat.SetColor("_Tint",tint);mat.SetFloat("_Earthwork",earth?1:0);mat.SetFloat("_Cull",earth?2:0);
        void Maps(string source,string colour,string normal,string mask)
        {
            string prefix="Surfaces/"+source+"_";
            mat.SetTexture(colour,Asset<Texture2D>(prefix+"colour.jpg"));mat.SetTexture(normal,Asset<Texture2D>(prefix+"normal.jpg"));
            mat.SetTexture(mask,Asset<Texture2D>(prefix+"mask.png"));
        }
        Maps(scan,"_BaseMap","_BumpMap","_Mask");
        if(earth)Maps("Earth","_Substrate","_SubNormal","_SubMask");
        else
        {
            bool patch=scan=="Repair flax";
            mat.SetTexture("_BaseMap",Asset<Texture2D>("Surfaces/"+(patch?"Shelter field patch":"Shelter waxed linen")+".jpg"));
            mat.SetFloat("_ClothRepeat",patch?2f:4f);
        }
        EditorUtility.SetDirty(mat);return mat;
    }

    // Interpolate the surveyed shoulders with continuous tangents. Retain the original
    // section at both masonry portals, where the cut earth edge is a separate mesh.
    static float Vault(float z, float roundness)
    {
        int k=0;while(k<Shoulder.Length-2 && z>Shoulder[k+1])k++;
        float span=Shoulder[k+1]-Shoulder[k],t=Mathf.Clamp01((z-Shoulder[k])/span);
        float Derivative(int i)
        {
            int a=Math.Max(0,i-1), b=Math.Min(Profile.Length-1,i+1);
            return (Profile[b]-Profile[a])/(Shoulder[b]-Shoulder[a]);
        }
        float curve=(2*t*t*t-3*t*t+1)*Profile[k]+(t*t*t-2*t*t+t)*span*Derivative(k)+
                    (-2*t*t*t+3*t*t)*Profile[k+1]+(t*t*t-t*t)*span*Derivative(k+1);
        return Mathf.Lerp(Mathf.Lerp(Profile[k],Profile[k+1],t),curve,roundness);
    }
    static Sample Sod(float x,float z)
    {
        float side=z+12;
        float boundary=Smooth(0,.90f,Mathf.Min(x+9.68f,10.33f-x))*Smooth(0,.62f,Mathf.Min(side+6.2f,6.5f-side));
        float original=.077f*(Mathf.Sin(x*.46f+side*.21f)+.45f*Mathf.Cos(side*1.03f+x*.37f)+.23f*Mathf.Sin(x*2.63f-side*.87f));
        float broad=Noise(x*.24f,side*.33f),fine=Noise(x*1.78f,side*1.64f);
        // Runoff follows slumps on the shoulders, with wider wind-scoured patches on the crown.
        float bend=2.5f*(Noise(x*.19f+8.2f,side*.35f+1.6f)-.5f);
        float slope=Smooth(.70f,3.65f,Mathf.Abs(side));
        float branching=Noise((x+bend)*.71f,side*.28f+2.4f);
        float pockets=Noise(x*.32f+side*.20f+1.3f*broad,side*.46f-.08f*x+5.2f);
        float frayed=Noise(x*2.8f+.8f*broad,side*2.3f);
        float wear=.40f*pockets+.34f*Mathf.Lerp(broad,branching,slope)+.15f*fine+.11f*frayed;
        float bare=.045f+.80f*Smooth(.414f,.66f,wear)+.13f*Smooth(.60f,.77f,branching)*slope;
        float sodLips=Smooth(.33f,.57f,fine)*(.46f+.54f*broad)*(1-bare);
        float y=Vault(side,boundary)+original+boundary*((broad-.45f)*.59f+
            (Noise(x*.87f,side*.91f)-.45f)*.24f+.12f*sodLips-.145f*bare);
        // Water cuts into the peat below the brow of the portal and at the feet
        // of the side slopes. Fade into this surface before the exposed cut begins.
        float endDistance=Mathf.Min(x+9.68f,10.33f-x);
        float sideDistance=Mathf.Min(z+18.2f,-5.5f-z);
        float loose=.19f+.68f*frayed+.25f*pockets;
        float toe=1-Smooth(.02f,.52f+loose,sideDistance);
        float lip=1-Smooth(.02f,.34f+.52f*loose,endDistance);
        float contact=Mathf.Clamp01(Mathf.Max(toe,lip)*(.87f+.13f*frayed));
        bare=Mathf.Lerp(bare,1,contact);
        return new Sample(new Vector3(x,y,z),new Vector2(x*.60f,(z+ .39f*y)*.60f),
                          new Color(bare,.20f+.57f*bare,.57f*broad+.43f*pockets,1-contact));
    }
    static void Casemate(MeshRenderer roof)
    {
        var geo=new Geometry(3);
        geo.Grid(0,145,96,(u,v)=>Sod(Mathf.Lerp(-9.68f,10.33f,u),Mathf.Lerp(-18.2f,-5.5f,v)));
        var rng=new System.Random(191509);float Rand()=>(float)rng.NextDouble();
        // Each tuft has its own crown; broad scars leave peat continuous between islands.
        int tufts=0;
        for(int island=0;island<150;island++)
        {
            float cx=Mathf.Lerp(-9.57f,10.20f,Rand()),cz=Mathf.Lerp(-18.02f,-5.63f,Rand());
            float extent=.30f+Rand()*.70f;
            for(int tuft=0;tuft<8;tuft++)
            {
                float a=Rand()*6.283f,r=extent*Mathf.Sqrt(Rand());
                float x=cx+r*Mathf.Cos(a),z=cz+r*Mathf.Sin(a)*.79f;
                if(x< -9.67f||x>10.32f||z< -18.18f||z> -5.52f)continue;
                var ground=Sod(x,z);if(ground.weather.r>.74f || (ground.weather.r>.41f && Rand()<.62f))continue;
                tufts++;float height=(.14f+.20f*Rand())*(1-.49f*ground.weather.r),phase=Rand()*6.283f;
                int blades=6+(int)(Rand()*6);
                for(int k=0;k<blades;k++)
                {
                    float az=phase+k*2.40f+.3f*Rand();var dir=new Vector3(Mathf.Cos(az),0,Mathf.Sin(az));
                    var across=Vector3.Cross(Up,dir);float spread=.045f*Rand();
                    float bx=x+dir.x*spread,bz=z+dir.z*spread;
                    var foot=Sod(bx,bz).p-Up*.022f;
                    float h=height*(.58f+.61f*Rand()),width=.009f+.010f*Rand();
                    var bend=dir*h*(.53f+.62f*Rand())+Vector3.right*(.026f+.047f*Rand());
                    int start=0;
                    for(int j=0;j<3;j++)
                    {
                        float t=j==2?1f:j*.53f;
                        var mid=foot+Up*(h*t*(1-.23f*t*t))+bend*(t*t);
                        float w=width*(j==2?.023f:j==1?.76f:1f);
                        int i=geo.Vertex(new Sample(mid-across*w,new Vector2(0,t),new Color(t,1,1,1)));
                        geo.Vertex(new Sample(mid+dir*w*.61f,new Vector2(.5f,t),new Color(t,1,1,1)));
                        geo.Vertex(new Sample(mid+across*w,new Vector2(1,t),new Color(t,1,1,1)));
                        if(j==0){start=i;continue;}
                        for(int q=0;q<2;q++){int b=start+(j-1)*3+q;geo.Tri(1,b,b+3,b+1);geo.Tri(1,b+1,b+3,b+4);}
                    }
                }
                if(Rand()>.20f)continue;
                // Photographed oat/sedge heads cast a broken outline above the turf.
                int tile=Rand()>.48f?1:2;
                for(int wing=0;wing<2;wing++)
                {
                    float yaw=phase+wing*Mathf.PI*.53f;
                    var axis=new Vector3(Mathf.Cos(yaw),0,Mathf.Sin(yaw))*(.10f+height*.21f);
                    geo.Grid(2,1,2,(u,v)=>new Sample(ground.p-Up*.02f+axis*(u*2-1)+Up*(v*height*1.40f)+Vector3.right*(v*v*.105f),
                        new Vector2((tile%2+.02f+u*.96f)*.5f,(1-tile/2+.02f+v*.96f)*.5f),new Color(v,1,1,1)));
                }
            }
        }
        if(tufts<250)throw new InvalidOperationException("Insufficient casemate grass coverage: "+tufts);
        geo.Save(roof,SodAsset,Cover("Casemate sod and scoured peat","Sod",new Color(.64f,.66f,.61f),true),
            Asset<Material>("Sedge.mat"),Asset<Material>("Grass.mat"));
        Debug.Log("Field roofs: main casemate turf clumps "+tufts);
    }

    // The fly is tied to the existing post heads. Tubular hems, folded overlaps and
    // stitches follow this same surface so they remain in contact in every trough.
    static Sample Fly(float x,float z)
    {
        float u=Mathf.Clamp01((x-West)/(East-West)),v=Mathf.Clamp01((z-Front)/(Rear-Front));
        float original=1.74f+.26f*u+.16f*v-.195f*Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI*u)),.65f)*
            Mathf.Abs(Mathf.Sin(v*Mathf.PI*2.3f));
        float spine=-10.17f+.075f*Mathf.Sin(x*1.16f)+.045f*Mathf.Cos(x*.64f);
        float along=Mathf.Abs(z-spine), side=Mathf.Clamp01(along/(z<spine?2.64f:3.12f));
        int bay=0;while(bay<Ties.Length-2 && x>Ties[bay+1])bay++;
        float span=Mathf.InverseLerp(Ties[bay],Ties[bay+1],x);
        float offCentre=Mathf.Clamp01(span+(.07f+.08f*Mathf.Cos(bay*2.71f+v))*Mathf.Sin(span*Mathf.PI));
        float sag=Mathf.Sin(Mathf.PI*offCentre);
        float clothBias=Noise(x*.65f,z*.28f);
        float foldPhase=x*6.7f+3.4f*clothBias+side*(1.5f+1.15f*Mathf.Sin(x*.77f));
        float basin=Mathf.Sin(Mathf.PI*side)*sag;
        float slack=.075f+.08f*Noise(bay*6.7f,z*.29f);
        float raised=.105f+.51f*Mathf.Pow(1-side,1.56f)+.068f*(1-sag)*side-slack*basin;
        float crease=Mathf.Sin(foldPhase);
        raised+=(.009f+.045f*clothBias)*crease*side+.043f*(Noise(x*1.12f,z*.44f)-.5f);
        // Creases fan toward the eyelets, then fall away before they reach the ridge.
        for(int i=0;i<Ties.Length;i++)
        {
            float dx=x-Ties[i],edge=Mathf.Min(z-Front,Rear-z);
            float radius=Mathf.Sqrt(dx*dx+edge*edge);
            float fan=Mathf.Pow(Mathf.Max(0,Mathf.Cos(12*Mathf.Atan2(dx,edge+.035f)+radius*.90f)),3);
            raised+=.071f*fan*Smooth(.035f,.19f,radius)*(1-Smooth(.20f,1.49f,radius));
        }
        float streak=Smooth(.15f,.95f,-crease)*side*(.24f+.76f*clothBias);
        float wet=Mathf.Clamp01(.26f+.34f*basin+.13f*clothBias+.35f*streak);
        return new Sample(new Vector3(x,original+raised,z),new Vector2(x*.81f,z*.81f),
                          new Color(Mathf.Clamp01(.72f*Smooth(.51f,.81f,Noise(x*.74f+side*.38f,z*1.63f))*
                              (.32f+.68f*side)+.43f*streak+.21f*side*side),wet,Noise(x*.56f,z*.49f),1));
    }

    static void Hospital(MeshRenderer roof)
    {
        const int main=0,hem=1,repair=2,rope=3,brass=4,thread=5;
        var geo=new Geometry(6);
        float HemZ(float x,bool front) => (front?Front:Rear)+(front?-1:1)*(.09f+.046f*Mathf.Sin(x*3.9f)+.04f*Mathf.Cos(x*1.13f));
        float Depth(float x) => .17f+.089f*Mathf.Sin(x*3.7f)+.042f*Mathf.Cos(x*1.64f);
        // Four salvaged canvas widths overlap across the ridge. Each selvage has
        // its own rolled bead and narrow compressed trough before the next panel.
        const int nx=CanvasX,nz=CanvasZ;
        // Share regular grid vertices at the border. Only the torn inner edge is open.
        bool Puncture(int i,int j)
        {
            var cell=new Vector2Int(i,j);
            foreach(var tear in Tears)if(tear.Contains(cell))return true;
            return false;
        }
        geo.Grid(main,nx,nz,(u,v)=>Fly(Mathf.Lerp(West,East,u),Mathf.Lerp(Front,Rear,v)),Puncture);
        for(int tear=0;tear<Tears.Length;tear++)
        {
            var r=Tears[tear];
            var perimeter=new List<Vector2Int>();
            int xmax=r.xMax,zmax=r.yMax;
            for(int j=r.y;j<zmax;j++)perimeter.Add(new Vector2Int(r.x,j));
            for(int i=r.x;i<xmax;i++)perimeter.Add(new Vector2Int(i,zmax));
            for(int j=zmax;j>r.y;j--)perimeter.Add(new Vector2Int(xmax,j));
            for(int i=xmax;i>r.x;i--)perimeter.Add(new Vector2Int(i,r.y));
            int count=perimeter.Count;
            var lip=new int[count];
            var bevel=new int[count];
            var edge=new Vector3[count];
            Vector2 c=r.center;
            for(int k=0;k<count;k++)
            {
                Vector2 d=perimeter[k]-c;
                d=new Vector2(d.x/r.width,d.y/r.height).normalized;
                float rough=1+.21f*Mathf.Sin(k*2.67f+tear)+.18f*Mathf.Sin(k*1.41f);
                // A few bindings kept the puncture small while adjacent cuts tore along the warp.
                float extent=(tear==1 || tear==3 || tear==7) ? .34f : (tear==5 ? .65f : 1f);
                var rim=c+new Vector2(d.x*r.width*.31f+d.y*.52f,d.y*r.height*(tear%3==1?.15f:.28f))*rough*extent;
                Sample Point(Vector2 grid)=>Fly(Mathf.Lerp(West,East,grid.x/nx),Mathf.Lerp(Front,Rear,grid.y/nz));
                var worn=Point(Vector2.Lerp(rim,perimeter[k],.46f));
                worn.p.y+=.014f*Mathf.Sin(k*.88f+tear);
                worn.weather.r=.76f;
                bevel[k]=geo.Vertex(worn);
                var fray=Point(rim);
                fray.p.y-=.016f+.019f*(.5f+.5f*Mathf.Sin(k*1.04f+tear));
                fray.weather.r=.15f;fray.weather.b=1;fray.weather.g=.12f;
                edge[k]=fray.p;
                lip[k]=geo.Vertex(fray);
            }
            for(int k=0;k<count;k++)
            {
                int next=(k+1)%count;
                int a=perimeter[k].y*(nx+1)+perimeter[k].x;
                int b=perimeter[next].y*(nx+1)+perimeter[next].x;
                geo.Tri(main,a,b,bevel[k]);geo.Tri(main,b,bevel[next],bevel[k]);
                geo.Tri(main,bevel[k],bevel[next],lip[k]);geo.Tri(main,bevel[next],lip[next],lip[k]);
                if(k%4!=0)continue;
                Vector3 end=Vector3.Lerp(edge[k],edge[(k+count/2)%count],.2f)+Vector3.down*.023f;
                geo.Cord(thread,t=>Vector3.Lerp(edge[k],end,t)-Up*(.009f*Mathf.Sin(t*Mathf.PI)),.0027f,3,4);
            }
        }
        var spouts=new List<Vector3>();
        foreach(bool front in new[]{true,false})
        {
            float baseZ=front?Front:Rear;
            Sample Skirt(float x,float amount)
            {
                var s=Fly(x,baseZ);
                s.p.z=Mathf.Lerp(baseZ,HemZ(x,front),Mathf.Sin(Mathf.PI*.55f*amount));
                s.p.y-=Depth(x)*amount;s.uv.y+=amount*.23f;return s;
            }
            // Both faces of the turned-down edge can be seen against the open ward.
            geo.Grid(hem,156,5,(u,v)=>Skirt(Mathf.Lerp(West,East,u),front?1-v:v));
            geo.Grid(hem,156,5,(u,v)=>{var s=Skirt(Mathf.Lerp(West,East,u),front?v:1-v);s.p.z+=front?.008f:-.008f;return s;});
            geo.Cord(rope,t=>Skirt(Mathf.Lerp(West,East,t),1).p,.017f,144);
            // Drain the lowest edge of each suspended span so water falls from the hem.
            for(int bay=0;bay<Ties.Length-1;bay++)
            {
                float left=Ties[bay]+.17f,right=Ties[bay+1]-.17f;
                var low=Skirt(left,1).p;
                for(int k=1;k<=32;k++)
                {
                    var sample=Skirt(Mathf.Lerp(left,right,k/32f),1).p;
                    if(sample.y<low.y)low=sample;
                }
                spouts.Add(roof.transform.InverseTransformPoint(low+Vector3.down*.025f));
            }
        }
        foreach(float seam in new[]{-51.28f,-48.49f,-45.36f})
        {
            float Centre(float z)=>seam+.026f*Mathf.Sin(z*2.1f+seam)+.048f*Mathf.Sin(z*.91f);
            geo.Grid(hem,6,98,(u,v)=>{
                float z=Mathf.Lerp(Front,Rear,v);
                var s=Fly(Centre(z)+(u-.5f)*.14f,z);
                s.p.y+=.008f+.020f*Mathf.Sin(u*Mathf.PI);return s;});
            geo.Cord(thread,t=>{float z=Mathf.Lerp(Front,Rear,t);return Fly(Centre(z)-.045f,z).p+Up*.023f;},.006f,88,6);
        }
        // A folded central lap protects the lash line along the raised purlin.
        geo.Grid(hem,160,8,(u,v)=>{
            float x=Mathf.Lerp(West,East,u),z=-10.17f+.075f*Mathf.Sin(x*1.16f)+.045f*Mathf.Cos(x*.64f)+(v-.5f)*.135f;
            var s=Fly(x,z);s.p.y+=.009f+.019f*Mathf.Sin(v*Mathf.PI);return s;});

        void Patch(float cx,float cz,float width,float length,float yaw,int seed)
        {
            Vector3 Point(float u,float v)
            {
                float a=(u-.5f)*(width+.065f*Mathf.Sin(4*v+seed))+.034f*Mathf.Sin(7*v+seed);
                float b=(v-.5f)*(length+.053f*Mathf.Sin(4*u-seed))+.022f*Mathf.Sin(u*7-seed);
                float x=cx+a*Mathf.Cos(yaw)-b*Mathf.Sin(yaw),z=cz+a*Mathf.Sin(yaw)+b*Mathf.Cos(yaw);
                float edge=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));
                return Fly(x,z).p+Up*(.036f+.026f*Mathf.Exp(-edge*19f)*(.5f+.5f*Mathf.Cos((u+v)*16+seed)));
            }
            geo.Grid(repair,35,28,(u,v)=>{var p=Point(u,v);return new Sample(p,new Vector2(u*width,v*length),new Color(0,.40f+.48f*v,.63f,1));});
            // Short, separate wax-thread bites run through the hemmed patch perimeter.
            for(int k=0;k<4;k++)
            {
                int count=Mathf.CeilToInt((k<2?width:length)/.115f);
                for(int i=0;i<count;i++)
                {
                    float f=(i+.18f)/count;
                    geo.Cord(thread,t=>{
                        float along=Mathf.Lerp(.035f,.965f,f+t*.50f/count),inset=.027f+.012f*t;
                        float u=k<2?along:(k==2?inset:1-inset),v=k>=2?along:(k==0?inset:1-inset);
                        return Point(u,v)+Up*(.006f+.008f*Mathf.Sin(t*Mathf.PI));},.006f,3,5);
                }
            }
        }
        Patch(-50.85f,-10.58f,1.61f,1.19f,-.19f,7);
        Patch(-45.21f,-8.55f,.94f,.72f,.26f,12);
        Patch(-47.86f,-11.73f,.31f,.86f,.54f,17);

        foreach(bool front in new[]{true,false})
        for(int i=0;i<Ties.Length;i++)
        {
            float cx=Ties[i];float edge=front?Front:Rear;float inward=front?1:-1;
            // Triangular layers spread the load of a real eyelet into the cloth.
            geo.Grid(hem,12,13,(u,v)=>{
                float x=cx+(u-.5f)*(.60f*(1-v)+.024f),z=edge+inward*(.025f+v*.52f);
                var s=Fly(x,z);s.p.y+=.032f+.012f*(1-v);return s;});
            var eye=Fly(cx,edge+inward*.10f).p+Up*.061f;
            geo.Cord(brass,t=>eye+new Vector3(Mathf.Cos(t*6.283185f)*.052f,0,Mathf.Sin(t*6.283185f)*.045f),.011f,16);
            // The far end of each hitch wraps the existing rail inside the eave.
            float postZ=front?-12.68f:-7.41f;
            float railY=Mathf.Lerp(1.49f,1.80f,Mathf.InverseLerp(-53.5f,-42.89f,cx));
            Vector3 outer=eye+new Vector3(.14f,-.07f,-inward*.34f);
            Vector3 knot=new Vector3(cx+.19f,railY+.063f,postZ);
            var middle=Vector3.Lerp(outer,knot,.50f)+new Vector3(0,-.08f,-inward*.10f);
            geo.Cord(rope,t=>Vector3.Lerp(eye,outer,t)+Up*(Mathf.Sin(t*Mathf.PI)*.035f),.022f,12);
            geo.Cord(rope,t=>Vector3.Lerp(Vector3.Lerp(outer,middle,t),Vector3.Lerp(middle,knot,t),t),.021f,14);
            geo.Cord(rope,t=>new Vector3(knot.x+t*.15f,railY+Mathf.Cos(t*Mathf.PI*6)*.072f,
                postZ+Mathf.Sin(t*Mathf.PI*6)*.072f),.016f,40);
        }
        var damp=Cover("Hospital rain darkened canvas","Canvas",Color.white);
        var edging=Cover("Hospital folded selvage","Canvas",new Color(.94f,.95f,.94f));
        var flax=Cover("Hospital hand stitched repairs","Repair flax",new Color(.99f,.98f,.96f));
        geo.Save(roof,WardAsset,damp,edging,flax,Asset<Material>("Hemp ties.mat"),Asset<Material>("Brass.mat"),Asset<Material>("Exposed root fibre.mat"));
        var runoff=roof.GetComponent<OsowiecWardEaves>();
        if(!runoff)runoff=roof.gameObject.AddComponent<OsowiecWardEaves>();
        runoff.outlets=spouts.ToArray();
        runoff.droplet=AssetDatabase.LoadAssetAtPath<Material>("Assets/Osowiec/Atmosphere/Rain.mat");
        EditorUtility.SetDirty(runoff);
    }

    static (MeshRenderer sod,MeshRenderer ward) Roofs()
    {
        var fort=UnityEngine.Object.FindAnyObjectByType<OsowiecFortCutaway>();
        if(!fort)throw new InvalidOperationException("Missing Fort I cutaway.");
        var sod=fort.surfaces.Single(r=>r&&r.name=="Roof_sod") as MeshRenderer;
        var ward=fort.surfaces.Single(r=>r&&r.name=="Roof_Ward_torn_oilcloth") as MeshRenderer;
        if(!sod||!ward||!fort.rooms.Single(r=>r.name=="Каземат").hide.Contains(sod)||
           !fort.rooms.Single(r=>r.name=="Лазарет").hide.Contains(ward))
            throw new InvalidOperationException("The main casemate and ward must keep their surveyed cutaways.");
        return (sod,ward);
    }
    public static void Install()
    {
        var roofs=Roofs();Casemate(roofs.sod);Hospital(roofs.ward);Verify();
    }
    static void Verify()
    {
        var roofs=Roofs();
        void Check(MeshRenderer r,string label,Vector3 centre,int slots)
        {
            var mesh=r.GetComponent<MeshFilter>().sharedMesh;
            if(mesh.name!=label||mesh.subMeshCount!=slots||mesh.vertexCount<10000 || !r.bounds.Contains(centre) ||
                r.sharedMaterials.Length!=slots || r.sharedMaterials.Any(m=>!m||!m.shader||
                    (SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null && !m.shader.isSupported)))
                throw new InvalidOperationException("Incomplete surveyed roof: "+r.name+" "+r.bounds);
            Debug.Log($"Field roofs verified {r.name}: {mesh.vertexCount} vertices; {mesh.subMeshCount} surfaces; {r.bounds}");
        }
        Check(roofs.sod,SodAsset,new Vector3(0,3,-12),3);
        Check(roofs.ward,WardAsset,new Vector3(-48.3f,2.02f,-10),6);
        var outlets=roofs.ward.GetComponent<OsowiecWardEaves>();
        if(!outlets || !outlets.droplet || outlets.outlets.Length!=6)
            throw new InvalidOperationException("Ward eave drains must match the six sewn low points.");
        var mesh=roofs.ward.GetComponent<MeshFilter>().sharedMesh;
        var verts=mesh.vertices.Select(roofs.ward.transform.TransformPoint).Select(v=>new Vector2(v.x,v.z)).ToArray();
        int[] faces=mesh.GetTriangles(0);
        bool HasCloth(Vector2 grid)
        {
            var at=new Vector2(Mathf.Lerp(West,East,grid.x/CanvasX),Mathf.Lerp(Front,Rear,grid.y/CanvasZ));
            for(int k=0;k<faces.Length;k+=3)
            {
                var a=verts[faces[k]];var b=verts[faces[k+1]];var c=verts[faces[k+2]];
                float Cross(Vector2 e,Vector2 f)=>e.x*f.y-e.y*f.x;
                float v=Cross(b-a,at-a),w=Cross(c-b,at-b),u=Cross(a-c,at-c);
                if(v>0&&w>0&&u>0 || v<0&&w<0&&u<0)return true;
            }
            return false;
        }
        foreach(var rip in Tears)
            if(HasCloth(rip.center) || !HasCloth(rip.min-new Vector2(.34f,.43f)))
                throw new InvalidOperationException("Incorrect canvas perforation at "+rip);
        Debug.Log("Verified eight through-holes and six canvas eave drains.");
    }
    public static void BakeCurrentScene()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=ScenePath)scene=EditorSceneManager.OpenScene(ScenePath);
        Install();EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save "+ScenePath);
        AssetDatabase.SaveAssets();
    }
    public static void VerifyCurrentScene()
    {
        EditorSceneManager.OpenScene(ScenePath);Verify();
    }
}
