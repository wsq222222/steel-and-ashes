using UnityEngine;
using UnityEditor;
using Unity.AI.Navigation;

public static partial class OsowiecBatteryFort
{
    static void YardMaterials()
    {
        RefreshYardFinishes();
        string name="Hemp ties";
        if(!AssetDatabase.LoadAssetAtPath<Material>(Art+name+".mat"))
        {
            string path=Art+name+".asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(!texture)
            {
                const int n=192;
                texture=new Texture2D(n,n,TextureFormat.RGBA32,true){name=name,wrapMode=TextureWrapMode.Repeat};
                var pixels=new Color32[n*n];
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    float u=x/(float)n*Mathf.PI*2,v=y/(float)n*Mathf.PI*2;
                    float fibre=.032f*(Mathf.Cos(u*48)+Mathf.Cos(v*48));
                    float age=.048f*Mathf.Cos(u*2+v*3)+.023f*Mathf.Sin(v*2-u*5);
                    var c=new Color(.76f,.62f,.40f)+new Color(fibre+age,fibre+age,fibre+age,0);
                    pixels[y*n+x]=c;
                }
                texture.SetPixels32(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,path);
            }
            string matPath=Art+name+".mat";
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
            material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Smoothness",.13f);
            AssetDatabase.CreateAsset(material,matPath);
        }
    }

    [MenuItem("Steel and Ashes/Refresh courtyard pine and canvas")]
    public static void RefreshYardFinishes()
    {
        var shader=Shader.Find("Steel and Ashes/Quartermaster stores");
        if(!shader)throw new System.InvalidOperationException("Missing courtyard finish shader.");
        UpdateFinish("Washed deal","Timber",false);
        UpdateFinish("Quartermaster linen","Canvas",true);
        void UpdateFinish(string name,string source,bool cloth)
        {
            const int size=512;
            string prefix=Art+"Surfaces/"+source;
            string albedoPath=Art+name+".asset";
            var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            if(!albedo)
            {
                albedo=new Texture2D(size,size,TextureFormat.RGBA32,true){name=name};
                AssetDatabase.CreateAsset(albedo,albedoPath);
            }
            if(albedo.width!=size)albedo.Reinitialize(size,size,TextureFormat.RGBA32,true);
            var sample=new Texture2D(2,2,TextureFormat.RGB24,false);
            Color32[] colours;
            try
            {
                if(!ImageConversion.LoadImage(sample,System.IO.File.ReadAllBytes(prefix+"_colour.jpg")))
                    throw new System.IO.InvalidDataException(prefix);
                var original=sample.GetPixels32();
                colours=new Color32[size*size];int step=sample.width/size;
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    float tone=0;
                    for(int j=0;j<step;j++)for(int k=0;k<step;k++)
                    {
                        var c=original[(y*step+j)*sample.width+x*step+k];
                        tone+=(.45f*c.r+.47f*c.g+.08f*c.b)/255f;
                    }
                    tone/=step*step;
                    float yarn=cloth?.013f*Mathf.Sin(x*Mathf.PI*.5f)*Mathf.Sin(y*Mathf.PI*.5f):0;
                    colours[y*size+x]=cloth ? new Color(.30f+.74f*tone+yarn,.29f+.71f*tone+yarn,.21f+.57f*tone+yarn) :
                        new Color(.198f+.91f*tone,.165f+.86f*tone,.130f+.78f*tone);
                }
            }finally{Object.DestroyImmediate(sample);}
            albedo.wrapMode=TextureWrapMode.Repeat;albedo.filterMode=FilterMode.Trilinear;albedo.anisoLevel=4;
            albedo.SetPixels32(colours);albedo.Apply();EditorUtility.SetDirty(albedo);AssetDatabase.SaveAssetIfDirty(albedo);
            string materialPath=Art+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!mat){mat=new Material(shader){name=name};AssetDatabase.CreateAsset(mat,materialPath);}
            mat.shader=shader;mat.SetTexture("_BaseMap",albedo);
            mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_normal.jpg"));
            mat.SetTexture("_RoughnessMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_mask.png"));
            mat.SetFloat("_Relief",cloth?1.85f:2.15f);
            mat.SetFloat("_Cloth",cloth?1:0);
            EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
        }
    }

    sealed class Cargo
    {
        readonly Masonry mesh;
        readonly Vector3 home,x,z;
        public Cargo(Masonry mesh,Vector3 home,float yaw)
        {
            this.mesh=mesh;this.home=home;
            x=Quaternion.AngleAxis(yaw,U)*R;z=Quaternion.AngleAxis(yaw,U)*F;
        }
        Vector3 P(float a,float h,float b)=>home+x*a+U*h+z*b;
        void Board(Finish finish,Vector3 centre,float width,float height,float depth)=>
            mesh.Box(finish,centre,x*width,U*height,z*depth);
        public void Chest(float a,float h,float b,float w,float d,float height,bool open)
        {
            var centre=P(a,h,b);
            int rows=4;
            Board(Finish.Deal,centre+U*.035f,w,.07f,d);
            for(int row=0;row<rows;row++)foreach(int sign in new[]{-1,1})
            {
                var level=centre+U*((row+.5f)*height/rows);
                Board(Finish.Deal,level+z*sign*(d*.5f-.026f),w,height/rows-.013f,.052f);
                Board(Finish.Deal,level+x*sign*(w*.5f-.027f),.054f,height/rows-.012f,d-.08f);
            }
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
            {
                var batten=centre+x*sx*(w*.5f-.080f)+z*sz*(d*.5f+.013f)+U*height*.5f;
                Board(Finish.Deal,batten,.10f,height+.035f,.052f);
                foreach(float y in new[]{.09f,height-.09f})
                {
                    var pin=batten+U*(y-height*.5f)+z*(sz*.028f);
                    Round(mesh,Finish.Iron,pin,pin+z*(sz*.008f),.011f,.011f,6);
                }
            }
            // Two worn paint bars on the far and near faces remain visible across the yard.
            foreach(int sz in new[]{-1,1})foreach(float y in new[]{.365f,.430f})
                Board(Finish.Tar,centre+z*sz*(d*.5f+.003f)+U*(height*y),w*.25f,.015f,.004f);
            if(!open)
            {
                for(int j=0;j<5;j++)Board(Finish.Deal,centre+U*(height+.025f)+z*((j-2)*d/5),w+.05f,.052f,d/5-.013f);
                foreach(int sign in new[]{-1,1})Board(Finish.Deal,centre+x*sign*(w*.35f)+U*(height+.070f),.095f,.048f,d+.052f);
                return;
            }
            for(int j=0;j<6;j++)
            {
                var blank=centre+x*((j-2.5f)*w*.135f)+U*(height-.110f);
                var nose=blank+z*d*.19f;
                Round(mesh,Finish.SplitWood,blank-z*d*.33f,nose,.083f,.081f,12);
                Round(mesh,Finish.Linen,nose,nose+z*.065f,.082f,.069f,12);
                Round(mesh,Finish.SplitWood,nose+z*.065f,nose+z*.14f,.069f,.017f,12);
                Round(mesh,Finish.Cord,blank-z*.14f,blank-z*.10f,.084f,.084f,12);
            }
            var hinge=centre-z*d*.48f+U*(height+.035f);
            var up=U*.64f-z*.28f;
            for(int j=0;j<4;j++)mesh.Box(Finish.Deal,hinge+up*((j+.5f)/4),x*(w+.03f),up*.235f,z*.045f);
            foreach(int sign in new[]{-1,1})
                mesh.Box(Finish.Tar,hinge+x*sign*w*.32f+up*.22f+z*.030f,x*.037f,up*.41f,z*.015f);
        }
        void Wheel(Vector3 hub,float r)
        {
            Vector3 Rim(int i)=>hub+(z*Mathf.Cos(i*Mathf.PI/12)+U*Mathf.Sin(i*Mathf.PI/12))*r;
            for(int i=0;i<24;i++)
            {
                var a=Rim(i);var b=Rim(i+1);var radial=((a+b)*.5f-hub).normalized;
                mesh.Box(Finish.Deal,(a+b)*.5f,b-a,radial*.066f,x*.062f);
                mesh.Box(Finish.Iron,(a+b)*.5f+radial*.035f,b-a,radial*.02f,x*.085f);
                if(i%3==0)Round(mesh,Finish.Deal,hub,a,.025f,.021f,7);
            }
            Round(mesh,Finish.Tar,hub-x*.086f,hub+x*.086f,.090f,.076f);
        }
        public void Handcart()
        {
            var axle=P(0,.40f,0);
            Round(mesh,Finish.Iron,axle-x*.57f,axle+x*.57f,.039f,.039f,9);
            foreach(int s in new[]{-1,1})
            {
                Wheel(axle+x*s*.51f,.38f);
                var rear=P(s*.38f,.65f,-.57f);var grip=P(s*.52f,.35f,1.39f);
                var length=grip-rear;mesh.Box(Finish.Deal,(rear+grip)*.5f,x*.069f,U*.086f,length);
                Round(mesh,Finish.Timber,grip,grip+length.normalized*.15f,.039f,.032f);
            }
            for(int j=0;j<4;j++)Board(Finish.Deal,P((j-1.5f)*.204f,.58f,-.08f),.195f,.066f,.97f);
            var roll=P(0,.77f,-.12f);
            Round(mesh,Finish.Linen,roll-x*.47f,roll+x*.47f,.166f,.157f,18);
            for(int i=0;i<5;i++)
            {
                float r=.037f+i*.022f;
                for(int j=0;j<19;j++)
                {
                    float a=(j+i*.2f)*Mathf.PI*2/19,b=(j+1+i*.2f)*Mathf.PI*2/19;
                    var end=roll+x*.474f;
                    var p=end+(U*Mathf.Sin(a)+z*Mathf.Cos(a))*r;
                    var q=end+(U*Mathf.Sin(b)+z*Mathf.Cos(b))*r;
                    Round(mesh,Finish.Patch,p,q,.005f,.005f,5);
                }
            }
            foreach(int s in new[]{-1,1})
            {
                var centre=roll+x*s*.29f;
                for(int i=0;i<20;i++)
                {
                    float a=i*Mathf.PI/10,b=(i+1)*Mathf.PI/10;
                    var p=centre+(U*Mathf.Sin(a)+z*Mathf.Cos(a))*.171f;
                    var q=centre+(U*Mathf.Sin(b)+z*Mathf.Cos(b))*.171f;
                    Round(mesh,Finish.Cord,p,q,.013f,.013f,7);
                }
                var knot=centre+z*.16f;
                Round(mesh,Finish.Cord,knot,knot-U*.20f+z*.11f,.015f,.009f,7);
            }
        }
        public void TeaBench()
        {
            foreach(int s in new[]{-1,1})foreach(int t in new[]{-1,1})
            {
                var a=P(.44f*s,.02f,t*.22f);var b=P(.34f*s,.55f,t*.16f);
                mesh.Box(Finish.Deal,(a+b)*.5f,x*.063f,b-a,z*.065f);
            }
            for(int i=-1;i<=1;i++)Board(Finish.Deal,P(0,.55f,i*.145f),1.03f,.08f,.138f);
            var rag=P(-.24f,.606f,.035f);
            Board(Finish.Linen,rag,.39f,.046f,.34f);
            Board(Finish.Linen,rag+U*.037f+x*.006f,.36f,.025f,.326f);
            new Store(mesh,P(.20f,.596f,-.024f),x,z).Pot(0,0,0);
            var tin=P(.43f,.602f,.12f);
            Round(mesh,Finish.Cord,tin,tin+U*.105f,.041f,.043f,12);
        }
    }

    static void InstallYardStores(Transform root)
    {
        Vector3 Soil(float x,float z)=>V(x,Ground(x,z)+.025f,z);
        var mesh=new Masonry();var centre=Soil(35.94f,-2.15f);
        const float freightYaw=162;
        var goods=new Cargo(mesh,centre,freightYaw);
        goods.Chest(-.61f,0,0,1.28f,.80f,.65f,false);
        goods.Chest(-.56f,.73f,-.025f,1.15f,.74f,.49f,true);
        goods.Chest(.68f,0,-.03f,1.03f,.75f,.52f,false);
        goods.Chest(.60f,0,.76f,.82f,.59f,.41f,false);
        var cart=Soil(37.73f,-1.85f);
        new Cargo(mesh,cart,-49).Handcart();
        var stool=Soil(35.76f,2.89f);
        new Cargo(mesh,stool,-18).TeaBench();
        mesh.Save(root,"Battery court unloading and tea bench",false);
        void Solid(string name,Vector3 at,float yaw,Vector3 size)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);
            go.transform.SetPositionAndRotation(at+U*(size.y*.48f),Quaternion.Euler(0,yaw,0));
            go.AddComponent<BoxCollider>().size=size;
            var nav=go.AddComponent<NavMeshModifier>();nav.overrideArea=true;nav.area=1;
        }
        var freightRotation=Quaternion.Euler(0,freightYaw,0);
        Solid("Freight pallet",centre,freightYaw,V(2.5f,.61f,.82f));
        Solid("Upper crate",centre+freightRotation*V(-.56f,.64f,0),freightYaw,V(1.14f,.57f,.74f));
        Solid("Side case",centre+freightRotation*V(.60f,0,.76f),freightYaw,V(.82f,.40f,.59f));
        Solid("Handcart body",cart,-49,V(1.08f,.74f,1.04f));
        Solid("Tea bench",stool,-18,V(.99f,.55f,.45f));
        InstallApproachCover(root);
    }

    internal static void InstallApproachCover(Transform root)
    {
        const string name="Approach cover";
        var old=root.Find(name);
        if(old)Object.DestroyImmediate(old.gameObject);
        var parent=new GameObject(name).transform;
        parent.SetParent(root,false);
        var mesh=new Masonry();
        Vector3 Soil(float x,float z)=>V(x,Ground(x,z)-.06f,z);
        void BoundsFor(string label,Vector3 foot,Quaternion turn,Vector3 size)
        {
            var go=new GameObject(label);
            go.transform.SetParent(parent,false);
            go.transform.SetPositionAndRotation(foot+U*(size.y*.5f),turn);
            go.AddComponent<BoxCollider>().size=size;
            var area=go.AddComponent<NavMeshModifier>();area.overrideArea=true;area.area=1;
        }
        void LogStack(float x,float z,float yaw)
        {
            var foot=Soil(x,z);var turn=Quaternion.Euler(0,yaw,0);
            Vector3 across=turn*R,along=turn*F;
            for(int i=0;i<4;i++)
            {
                var centre=foot+U*(.16f+i*.261f);
                Round(mesh,Finish.SplitWood,centre-across*(1.10f+i*.038f),centre+across*(1.25f-i*.06f),.185f,.16f);
            }
            foreach(float s in new[]{-.77f,.80f})
            {
                var pin=foot+across*s+along*.23f;
                Round(mesh,Finish.Timber,pin-U*.08f,pin+U*1.10f+along*.04f,.075f,.052f);
            }
            BoundsFor("Revetted timber",foot,turn,V(2.24f,1.10f,.39f));
        }
        void BrokenPier(float x,float z,float yaw)
        {
            var foot=Soil(x,z);var turn=Quaternion.Euler(0,yaw,0);
            for(int row=0;row<8;row++)
            {
                int count=row<5?5:row<7?4:2;
                float shift=(row%2)*.13f;
                for(int block=0;block<count;block++)
                {
                    var c=foot+turn*V(-.74f+block*.39f+shift,.10f+row*.172f,0);
                    mesh.Box((row+block)%6==0?Finish.BrokenBrick:Finish.Brick,c,turn*R*.382f,U*.166f,turn*F*.46f);
                }
                var course=foot+turn*V(-.74f+(count-1)*.195f+shift,.017f+row*.172f,0);
                BoundsFor("Pier masonry course",course,turn,V(count*.39f,.172f,.46f));
            }
        }
        LogStack(-20.5f,-4.85f,2);
        LogStack(-5.2f,15.15f,-12);
        BrokenPier(23.7f,-6.0f,-33);
        var freight=Soil(29f,-12f);var rotation=Quaternion.Euler(0,-17f,0);
        var cargo=new Cargo(mesh,freight,-17);
        cargo.Chest(0,0,0,1.38f,.82f,.69f,false);
        cargo.Chest(-.14f,.71f,0,1.00f,.65f,.54f,false);
        BoundsFor("Stores pallet",freight,rotation,V(1.38f,.70f,.82f));
        BoundsFor("Top case",freight+rotation*V(-.14f,.71f,0),rotation,V(1,.54f,.65f));
        mesh.Save(parent,"Transit timber, broken pier and supply pallet",false);
    }
}
