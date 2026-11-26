using System;
using UnityEngine;

public static partial class OsowiecBatteryFort
{
    static void Round(Masonry g,Finish finish,Vector3 a,Vector3 b,float r,float end,int sides=10)
    {
        var axis=(b-a).normalized;
        var cross=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?U:F).normalized;
        var flat=Vector3.Cross(axis,cross);
        var low=new Vector3[sides];var high=new Vector3[sides];
        for(int i=0;i<sides;i++)
        {
            float t=i*Mathf.PI*2/sides;
            var d=cross*Mathf.Cos(t)+flat*Mathf.Sin(t);
            low[i]=a+d*r;high[i]=b+d*end;
        }
        for(int i=0;i<sides;i++)
        {
            int next=(i+1)%sides;
            g.Skin(finish,low[i]-a,low[i],low[next],high[next],high[i]);
        }
        g.Skin(finish,-axis,low);g.Skin(finish,axis,high);
    }
    static void Rope(Masonry g,Vector3 at,Vector3 along,Vector3 across,float radius,int turns)
    {
        Vector3 previous=default;
        int spans=28*turns;
        for(int i=0;i<=spans;i++)
        {
            float a=i*Mathf.PI/14,r=radius*(.55f+.45f*i/spans);
            var p=at+(along*Mathf.Cos(a)+across*(.75f*Mathf.Sin(a)))*r+U*(.017f+.008f*Mathf.Cos(a*3));
            if(i>0)Round(g,Finish.Rope,previous,p,.012f,.013f,6);
            previous=p;
        }
        var loose=previous+along*.16f+across*.10f;
        Round(g,Finish.Rope,previous,loose,.012f,.012f,6);
        Round(g,Finish.Rope,loose,loose+across*.12f,.012f,.007f,6);
    }
    static void CanvasSheet(Masonry g,Vector3 at,Vector3 span,Vector3 down,Vector3 fold,float bow)
    {
        const int nx=14,ny=8;
        Vector3 P(int x,int y)
        {
            float u=x/(float)nx,v=y/(float)ny;
            float pleat=Mathf.Sin(u*22+v*1.7f)*.58f+Mathf.Sin(u*39-v)*.26f;
            float sag=Mathf.Sin(u*Mathf.PI)*Mathf.Sin(v*Mathf.PI);
            return at+span*(u-.5f)+down*v+fold*(bow*(pleat*(.27f+.73f*v)+sag*.7f))+
                U*(.017f*Mathf.Cos(u*21)*v);
        }
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            bool patch=x>=3&&x<=6&&y>=3&&y<=4;
            var mat=patch?Finish.Patch:Finish.Canvas;
            var a=P(x,y);var b=P(x+1,y);var c=P(x+1,y+1);var d=P(x,y+1);
            var normal=Vector3.Cross(span,down).normalized;
            g.Skin(mat,normal,a,b,c);g.Skin(mat,normal,a,c,d);
            g.Skin(mat,-normal,a,c,b);g.Skin(mat,-normal,a,d,c);
            if(patch && (x==3 || x==6))Round(g,Finish.Rope,a,a+(d-a)*.41f,.005f,.005f,5);
        }
    }

    sealed class Store
    {
        readonly Masonry g;readonly Vector3 origin,side,front;
        internal Store(Masonry mesh,Vector3 p,Vector3 along,Vector3 outward)
        {g=mesh;origin=p;side=along;front=outward;}
        Vector3 P(float x,float y,float z)=>origin+side*x+U*y+front*z;
        void Box(Finish m,float x,float y,float z,float w,float h,float d)=>
            g.Box(m,P(x,y,z),side*w,U*h,front*d);
        internal void Crate(float x,float y,float z,float width,bool open)
        {
            const float h=.43f,d=.48f;
            Box(Finish.SplitWood,x,y+.027f,z,width,.054f,d);
            for(int row=0;row<3;row++)
            {
                float height=y+.105f+row*.127f;
                foreach(int sign in new[]{-1,1})
                {
                    Box(Finish.Timber,x,height,z+sign*d*.47f,width,.117f,.037f);
                    Box(Finish.SplitWood,x+sign*width*.48f,height,z,.041f,.117f,d-.065f);
                }
            }
            foreach(int sign in new[]{-1,1})foreach(int face in new[]{-1,1})
            {
                float bx=x+sign*(width*.5f-.045f),bz=z+face*(d*.5f+.008f);
                Box(Finish.Iron,bx,y+.22f,bz,.042f,.41f,.014f);
                foreach(float sy in new[]{.07f,.34f})
                    Round(g,Finish.Brass,P(bx,y+sy,bz),P(bx,y+sy,bz+face*.013f),.010f,.008f,7);
            }
            if(!open)
            {
                for(int i=0;i<4;i++)Box(Finish.SplitWood,x,y+h,z+(i-1.5f)*.12f,width+.03f,.041f,.115f);
                foreach(int s in new[]{-1,1})Box(Finish.Timber,x+s*width*.32f,y+h+.026f,z,.059f,.025f,.49f);
                return;
            }
            // Painted wooden exhibition blanks remained in their slotted travelling case.
            for(int i=0;i<5;i++)
            {
                var centre=P(x+(i-2)*width*.17f,y+.25f,z);
                var rear=centre-front*.165f;var nose=centre+front*.13f;
                Round(g,Finish.SplitWood,rear,nose,.053f,.050f,12);
                Round(g,Finish.Concrete,nose,nose+front*.03f,.05f,.042f,12);
                Round(g,Finish.SplitWood,nose+front*.03f,nose+front*.074f,.042f,.016f,12);
                Round(g,Finish.Brass,rear+front*.02f,rear+front*.042f,.054f,.054f,12);
            }
        }
        internal void Bench()
        {
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                Box(Finish.SplitWood,sx*.90f,.58f,sz*.25f,.068f,1.16f,.083f);
            foreach(float h in new[]{.14f,.72f,1.15f})
            {
                for(int board=0;board<3;board++)
                    Box(Finish.Timber,0,h,(board-1)*.175f,1.96f,.055f,.164f);
                Box(Finish.SplitWood,0,h-.062f,-.245f,1.90f,.089f,.065f);
            }
            var a=P(-.83f,.20f,-.246f);var b=P(.82f,1.10f,-.246f);
            g.Box(Finish.Timber,(a+b)*.5f,b-a,U*.054f,front*.040f);
            Crate(-.36f,.75f,0,.86f,true);
            Rope(g,P(.46f,1.185f,0),side,front,.22f,3);
            var handle=P(-.55f,1.207f,.06f);var head=handle+side*.43f-front*.12f;
            Round(g,Finish.Timber,handle,head,.021f,.025f);
            g.Box(Finish.Iron,head,side*.075f,U*.057f,front*.16f);
            Crate(.39f,.17f,.015f,.66f,false);
            CanvasSheet(g,P(-.41f,1.186f,.269f),side*.90f,-U*.38f,front,.069f);
        }
        internal void Pot(float x,float y,float z)
        {
            var p=P(x,y,z);
            const int segments=18;
            Vector3 Circle(int i,float r,float h)=>p+U*h+r*(side*Mathf.Cos(i*Mathf.PI*2/segments)+front*Mathf.Sin(i*Mathf.PI*2/segments));
            for(int i=0;i<segments;i++)
            {
                var a=Circle(i,.115f,.025f);var b=Circle(i+1,.115f,.025f);
                var c=Circle(i+1,.161f,.235f);var d=Circle(i,.161f,.235f);
                g.Skin(Finish.Iron,a-p,a,b,c,d);g.Skin(Finish.Tar,p-a,d,c,b,a);
                Round(g,Finish.Brass,c,d,.009f,.009f,6);
            }
            var prev=p-side*.16f+U*.18f;
            for(int i=1;i<=15;i++)
            {
                float t=i*Mathf.PI/15;
                var next=p-side*(.16f*Mathf.Cos(t))+U*(.18f+.19f*Mathf.Sin(t));
                Round(g,Finish.Iron,prev,next,.009f,.009f,6);prev=next;
            }
        }
        internal void Cloak()
        {
            var hook=P(0,1.78f,0);
            Round(g,Finish.Iron,hook-front*.12f,hook,.026f,.018f,8);
            CanvasSheet(g,hook,side*.38f,-U*.25f,front,.11f);
            CanvasSheet(g,hook-U*.23f+front*.045f,side*.64f,-U*.83f,front,.13f);
        }
    }

    static void InstallStores(Transform root)
    {
        YardMaterials();
        var mesh=new Masonry();
        var front=V(Mathf.Cos(2.10f),0,Mathf.Sin(2.10f));var side=Vector3.Cross(U,front);
        var bench=V(40,0,-8)-side*2.52f+front*4.20f;
        bench.y=Ground(bench.x,bench.z)+.025f;
        var workshop=new Store(mesh,bench,side,front);workshop.Bench();
        var pantry=new Store(mesh,V(48.13f,Deck+.94f,.99f),F,-R);pantry.Pot(0,0,0);
        var hooks=new Store(mesh,V(48.80f,Deck,2.94f),F,-R);hooks.Cloak();
        var second=V(Mathf.Cos(1.19f),0,Mathf.Sin(1.19f));var flank=Vector3.Cross(U,second);
        var point=V(32,0,9)+flank*4.32f-second*1.32f;
        point.y=Ground(point.x,point.z)+.06f;
        var stacks=new Store(mesh,point,second,flank);
        stacks.Crate(-.46f,0,0,.83f,false);stacks.Crate(.41f,0,0,.75f,false);
        stacks.Crate(-.42f,.485f,0,.78f,true);
        CanvasSheet(mesh,point+second*.42f+U*.49f,second*.76f,flank*.36f,U,.037f);
        foreach(int wheel in new[]{-1,1})
        {
            var rut=point+flank*.78f+second*(wheel*.30f);
            var end=rut+flank*.68f+second*.06f;
            rut.y=Ground(rut.x,rut.z)+.009f;end.y=Ground(end.x,end.z)+.009f;
            mesh.Stain(Finish.Wet,(rut+end)*.5f,second*.035f,(end-rut)*.5f,U,31+wheel);
        }
        mesh.Save(root,"Battery workshop stores",false);
        InstallYardStores(root);
    }
}
