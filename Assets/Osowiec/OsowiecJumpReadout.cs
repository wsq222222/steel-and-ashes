using UnityEngine;

internal static class OsowiecJumpReadout
{
    static readonly Color Outline=new(.035f,.046f,.045f,.94f);
    static readonly Color Clear=new(.56f,1,.80f);
    static readonly Color Blocked=new(1,.48f,.28f);

    static bool Project(Camera eye,Vector3 point,out Vector2 result)
    {
        var pixel=eye.WorldToScreenPoint(point);
        result=new Vector2(pixel.x,Screen.height-pixel.y);
        return pixel.z>eye.nearClipPlane;
    }
    static void Stroke(Vector2 a,Vector2 b,float width,Color color)
    {
        var d=b-a;
        if(d.sqrMagnitude<.15f)return;
        var matrix=GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,a);
        OsowiecHudPaint.Fill(new Rect(a.x-width*.5f,a.y-width*.5f,d.magnitude+width,width),color);
        GUI.matrix=matrix;
    }
    public static void Draw(Camera eye,Vector3[] arc,int count,bool clear)
    {
        if(Event.current.type!=EventType.Repaint || !eye || count<2)return;
        float scale=OsowiecHudPaint.Scale;
        var ink=clear?Clear:Blocked;
        var lift=Vector3.up*.065f;
        for(int pass=0;pass<2;pass++)
        for(int i=1;i<count;i++)
            if(Project(eye,arc[i-1]+lift,out var a) && Project(eye,arc[i]+lift,out var b))
                Stroke(a,b,(pass==0?7:3)*scale,pass==0?Outline:ink);
        float phase=Mathf.Repeat(Time.unscaledTime*4,4);
        for(float at=phase;at<count-1;at+=4)
        {
            int i=Mathf.FloorToInt(at);
            if(Project(eye,Vector3.Lerp(arc[i],arc[i+1],at-i)+lift,out var pixel))
                OsowiecHudPaint.Fill(new Rect(pixel.x-3*scale,pixel.y-3*scale,6*scale,6*scale),Color.white);
        }
        var end=arc[count-1]+lift;
        if(!Project(eye,end,out var centre))return;
        for(int pass=0;pass<2;pass++)
        {
            float width=(pass==0?7f:3f)*scale;
            var color=pass==0?Outline:ink;
            for(int j=0;j<32;j++)
            {
                float a=j*Mathf.PI/16f,b=(j+1)*Mathf.PI/16f;
                if(Project(eye,end+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.43f,out var p) &&
                   Project(eye,end+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*.43f,out var q))
                    Stroke(p,q,width,color);
            }
            if(!clear)
            {
                var diagonal=new Vector2(9,9)*scale;
                Stroke(centre-diagonal,centre+diagonal,width,color);
                diagonal.x=-diagonal.x;Stroke(centre-diagonal,centre+diagonal,width,color);
            }
        }
        var flat=Vector3.ProjectOnPlane(arc[count-1]-arc[0],Vector3.up).magnitude;
        string label=OsowiecMenuLanguage.Format(clear?"Посадка · {0:0.0} м":"Преграда · {0:0.0} м",flat);
        float w=Mathf.Min(190*scale,Screen.width*.5f);
        OsowiecHudPaint.Plate(new Rect(Mathf.Clamp(centre.x-w*.5f,0,Screen.width-w),
            Mathf.Clamp(centre.y-49*scale,0,Screen.height-36*scale),w,33*scale),label,true);
    }
}
