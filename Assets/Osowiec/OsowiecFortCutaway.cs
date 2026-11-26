using UnityEngine;

public sealed class OsowiecFortCutaway : MonoBehaviour
{
    [System.Serializable]
    public sealed class Room
    {
        public string name;
        public Renderer[] hide;
        public Vector2 origin;
        public int width,depth;
        public float spacing,minFoot;
        public float[] ceiling;

        public float Overhead(Vector3 feet)
        {
            if(feet.y<minFoot)return float.PositiveInfinity;
            int x=Mathf.FloorToInt((feet.x-origin.x)/spacing);
            int z=Mathf.FloorToInt((feet.z-origin.y)/spacing);
            if(x<0 || z<0 || x>=width || z>=depth)return float.PositiveInfinity;
            float clearance=ceiling[z*width+x]-feet.y;
            return clearance>.72f && clearance<5.6f ? clearance : float.PositiveInfinity;
        }
    }

    public Renderer[] surfaces;
    public MeshCollider batteryRoof;
    public MeshCollider[] batteryRoofs;
    public Room[] rooms;
    static OsowiecFortCutaway field;
    Unit lastUnit;
    Vector3 lastFeet;
    Room open;
    void OnEnable(){field=this;lastUnit=null;open=null;}
    void OnDisable(){Show(null);lastUnit=null;if(field==this)field=null;}

    internal static bool Sheltered(Vector3 p)
    {
        if(!field)return false;
        bool Under(MeshCollider shell)
        {
            if(!shell)return false;
            var box=shell.bounds;
            if(p.y>=box.max.y || p.y<box.min.y-3.2f || p.x<box.min.x || p.x>box.max.x || p.z<box.min.z || p.z>box.max.z)return false;
            return shell.Raycast(new Ray(p,Vector3.up),out _,box.size.y+3.25f);
        }
        if(field.batteryRoofs!=null)foreach(var roof in field.batteryRoofs)if(Under(roof))return true;
        return Under(field.batteryRoof);
    }

    internal static bool HiddenForPlanning(Collider collider)
    {
        var mesh=collider.GetComponent<Renderer>();
        return mesh && !mesh.enabled && field && field.open!=null && System.Array.IndexOf(field.open.hide,mesh)>=0;
    }

    internal Room Covering(Vector3 feet)
    {
        Room closest=null;
        float distance=float.PositiveInfinity;
        if(rooms!=null)foreach(var room in rooms)
        {
            float height=room.Overhead(feet);
            if(height<distance){closest=room;distance=height;}
        }
        return closest;
    }

    void Show(Room room)
    {
        if(open==room)return;
        if(open!=null)foreach(var roof in open.hide)if(roof)roof.enabled=true;
        open=room;
        if(open!=null)foreach(var roof in open.hide)if(roof)roof.enabled=false;
    }

    void LateUpdate()
    {
        var camera=CameraManager.Instance;
        var planner=OsowiecPlanner.Instance;
        var unit=camera && !camera.IsActionMode() && planner?planner.SelectedUnit:null;
        if(!unit || !unit.isActiveAndEnabled || unit.GetHealth()<=0)
        {
            Show(null);lastUnit=null;return;
        }
        var feet=unit.transform.position;
        if(unit==lastUnit && (feet-lastFeet).sqrMagnitude<.0004f)return;
        lastUnit=unit;lastFeet=feet;Show(Covering(feet));
    }
}
