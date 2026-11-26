using System;
using UnityEngine;

[Serializable]
public sealed class OsowiecSurveyProjection
{
    [Serializable] public struct Box
    {
        public float x,y,w,h;
    }
    public int width,height;
    public float west,east,north,south;
    public Box plot;
    public Box[] captions;
    static OsowiecSurveyProjection current;
    public static OsowiecSurveyProjection Current => current ??
        (current=JsonUtility.FromJson<OsowiecSurveyProjection>(Resources.Load<TextAsset>("OsowiecJournal/survey-layout").text));
    public float Aspect=>(float)width/height;
    public Rect Scaled(Rect sheet,Box r)=>new(sheet.x+r.x/width*sheet.width,sheet.y+r.y/height*sheet.height,
        r.w/width*sheet.width,r.h/height*sheet.height);
    public Vector2 Point(Rect area,Vector3 world)=>new(area.x+(world.x-west)/(east-west)*area.width,
        area.y+(north-world.z)/(north-south)*area.height);
    public Vector3 World(Rect area,Vector2 cursor)=>new(west+(cursor.x-area.x)/area.width*(east-west),0,
        north-(cursor.y-area.y)/area.height*(north-south));
    public Vector2 Anchor(Vector3 world)
    {
        var point=Point(Scaled(new Rect(0,0,1,1),plot),world);
        return new Vector2(point.x,1-point.y);
    }
}
