using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Surveyed grid cells above the western and central trench floors.
public sealed class OsowiecDeploymentSectors : MonoBehaviour
{
    public static readonly Vector2Int[][] Cells = {
        new[] { new Vector2Int(23,22), new Vector2Int(25,22), new Vector2Int(24,23) },
        new[] { new Vector2Int(29,34), new Vector2Int(30,33), new Vector2Int(31,34) }
    };
    public static readonly string[] Titles = { "Западный ход", "Центральная траншея" };
    public static readonly string[] Descriptions = { "От лазарета вперёд, до последнего поворота к телефону.", "По настилу до изгиба траншеи, затем направо к телефону." };
    public int Sector { get; private set; }
    public Vector2Int[] Current => Cells[Sector];
    public static int IndexOf(Vector2Int cell)
    {
        for (int i=0; i<Cells.Length; i++)
            if (System.Array.IndexOf(Cells[i],cell)>=0) return i;
        return -1;
    }

    ArmyDeploymentController deployment;
    readonly List<LineRenderer> tracks = new();
    Material lineMaterial, ghostMaterial;
    GameObject markers, ghost;
    Mesh ghostMesh;
    int ghostOffer = -1;
    public int HoverOffer { get; private set; } = -1;
    public string HoverMessage { get; private set; } = "";

    void Awake() { deployment = GetComponent<ArmyDeploymentController>(); }

    public void Switch()
    {
        if (!GameManager.Instance || !GameManager.Instance.IsArmyDeploymentPhase()) return;
        Sector=(Sector+1)%Cells.Length;
        HideGhost();
        ShowPositions();
    }

    public void ShowPositions()
    {
        if (markers) Destroy(markers);
        tracks.Clear();
        markers=new GameObject("Foot positions");
        markers.transform.SetParent(transform,false);
        if (!lineMaterial) lineMaterial=new Material(Shader.Find("Sprites/Default"));
        var bounds=new Bounds(ChessGrid.Instance.GridToWorldPosition(Current[0].x,Current[0].y),Vector3.zero);
        foreach (var cell in Current)
        {
            Vector3 p=Ground(cell);
            bounds.Encapsulate(p);
            foreach (float side in new[]{-.12f,.12f})
            {
                var rail=new GameObject("Heel and toe").AddComponent<LineRenderer>();
                rail.transform.SetParent(markers.transform,false);
                rail.transform.position=p+Vector3.up*.04f;
                rail.transform.rotation=Quaternion.Euler(0,90,0);
                rail.sharedMaterial=lineMaterial;
                rail.useWorldSpace=false;
                rail.loop=true;
                rail.widthMultiplier=.032f;
                rail.positionCount=6;
                rail.SetPositions(new[]{ new Vector3(side-.043f,0,-.13f),new Vector3(side+.043f,0,-.13f),
                    new Vector3(side+.057f,0,.07f),new Vector3(side+.036f,0,.14f),new Vector3(side-.036f,0,.14f),new Vector3(side-.057f,0,.08f)});
                tracks.Add(rail);
            }
        }
        CameraManager.Instance?.FrameDeploymentSector(bounds);
        RefreshPositions();
    }

    public void RefreshPositions()
    {
        for (int i=0;i<tracks.Count;i++)
        {
            bool occupied=deployment.IsGridCellOccupied(Current[i/2]);
            Color color=occupied ? new Color(.42f,.49f,.48f) : new Color(.98f,.81f,.46f);
            tracks[i].startColor=tracks[i].endColor=color;
        }
    }

    static Vector3 Ground(Vector2Int cell)
    {
        var p=ChessGrid.Instance.GridToWorldPosition(cell.x,cell.y);
        if (NavMesh.SamplePosition(p,out var nav,1,NavMesh.AllAreas)) p.y=nav.position.y;
        float best=float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(p+Vector3.up*.65f,Vector3.down,1.3f,~0,QueryTriggerInteraction.Ignore))
            if (hit.normal.y>.6f && !hit.collider.GetComponentInParent<Unit>()) best=Mathf.Max(best,hit.point.y);
        if (!float.IsNegativeInfinity(best)) p.y=best;
        return p;
    }

    public void Preview(int offer, Vector2 screen)
    {
        HoverOffer=offer;
        bool valid = deployment.TryGetOffer(offer,out var entry);
        if (!valid || !ArmyDeploymentController.TryScreenPointToGridCell(screen,out var cell) ||
            IndexOf(cell)!=Sector || (EventSystem.current && EventSystem.current.IsPointerOverGameObject()))
        {
            HoverMessage="Выберите следы на настиле";
            if (ghost) ghost.SetActive(false);
            return;
        }
        HoverMessage=deployment.PlacementIssue(offer,cell,Keyboard.current?.shiftKey.isPressed==true);
        if (HoverMessage.Length>0)
        {
            if (ghost) ghost.SetActive(false);
            return;
        }
        if (!ghost || ghostOffer!=offer) BuildGhost(entry.unitPrefab,offer);
        if (!ghost) return;
        ghost.transform.SetPositionAndRotation(Ground(cell)-Vector3.up*ghostMesh.bounds.min.y,Quaternion.Euler(0,90,0));
        ghost.SetActive(true);
    }

    void BuildGhost(GameObject prefab,int offer)
    {
        ClearGhost();
        var holder=new GameObject("Preview pose");
        holder.SetActive(false);
        var actor=Instantiate(prefab,holder.transform,false);
        var sources=new List<Mesh>();
        var parts=new List<CombineInstance>();
        foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.sharedMesh) continue;
            var pose=new Mesh();
            skin.BakeMesh(pose);
            sources.Add(pose);
            for(int sub=0;sub<pose.subMeshCount;sub++)
                parts.Add(new CombineInstance {mesh=pose,subMeshIndex=sub,transform=actor.transform.worldToLocalMatrix*skin.localToWorldMatrix});
        }
        if(parts.Count>0)
        {
            ghostMesh=new Mesh {name="Deployment preview pose",indexFormat=IndexFormat.UInt32};
            ghostMesh.CombineMeshes(parts.ToArray(),true,true);
            ghost=new GameObject("Life-size placement preview",typeof(MeshFilter),typeof(MeshRenderer));
            ghost.GetComponent<MeshFilter>().sharedMesh=ghostMesh;
            if (!ghostMaterial)
            {
                ghostMaterial=new Material(Resources.Load<Shader>("OsowiecDeploymentFigure"));
            }
            var renderer=ghost.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=ghostMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            ghostOffer=offer;
        }
        Destroy(holder);
        foreach (var mesh in sources) Destroy(mesh);
    }

    public void HideGhost()
    {
        HoverOffer=-1; HoverMessage="";
        if (ghost) ghost.SetActive(false);
    }

    void ClearGhost()
    {
        if (ghost) { ghost.SetActive(false); Destroy(ghost); }
        if (ghostMesh) Destroy(ghostMesh);
        ghost=null; ghostMesh=null; ghostOffer=-1;
    }

    public void Finish()
    {
        if (markers) { markers.SetActive(false); Destroy(markers); }
        tracks.Clear();
        HideGhost();
        ClearGhost();
    }

    void OnDestroy()
    {
        Finish();
        if(lineMaterial) Destroy(lineMaterial);
        if(ghostMaterial) Destroy(ghostMaterial);
    }
}

