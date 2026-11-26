using UnityEngine;

// Survey coordinates are shared with the authoring mesh and boot placement.
public static class OsowiecGalleryStairs
{
    public const float FootX=35.3f, HeadX=42.95f, CentreZ=1.1f;
    public const int Treads=22;
    const float Run=(HeadX-FootX)/Treads;

    public static bool OnFlight(Vector3 position)
    {
        if(position.x<FootX-.18f || position.x>HeadX+.18f || Mathf.Abs(position.z-CentreZ)>1.08f)return false;
        // The enclosed passage under the gallery is a different floor.
        return position.y>-.9f;
    }

    public static Vector3 SeatBoot(Vector3 p)
    {
        if(p.x>=FootX && p.x<HeadX && Mathf.Abs(p.z-CentreZ)<1.14f)
        {
            float lip=FootX+Mathf.Floor((p.x-FootX)/Run)*Run;
            p.x=Mathf.Clamp(p.x,lip+.09f,lip+Run-.075f);
        }
        return p;
    }
}
