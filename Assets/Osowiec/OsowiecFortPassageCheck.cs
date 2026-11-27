using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Opt-in regression of the finished courtyard, both ramp heads and the lower tunnel.
public sealed class OsowiecFortPassageCheck : MonoBehaviour
{
    const float X=46;
    static readonly Vector2[] Route={
        new(39.85f,-12.72f),new(X,-12.72f),new(X,-11.75f),new(X,-4.35f),
        new(X,.5f),new(X,5.35f),new(X,13.75f),new(X,14.72f),new(39.85f,14.72f)
    };
    string directory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWhenRequested()
    {
        var args=Environment.GetCommandLineArgs();
        int i=Array.IndexOf(args,"-osowiec-fort-passage");
        if(Application.isEditor || i<0 || i+1>=args.Length)return;
        var go=new GameObject("Courtyard passage test");
        DontDestroyOnLoad(go);
        go.AddComponent<OsowiecFortPassageCheck>().directory=args[i+1];
    }

    static Vector3 OnFloor(Vector2 xz)
    {
        var hits=Physics.RaycastAll(new Vector3(xz.x,9,xz.y),Vector3.down,18,~0,QueryTriggerInteraction.Ignore);
        var ground=hits.FirstOrDefault(h=>h.collider.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal));
        if(!ground.collider)throw new InvalidOperationException("Missing ground at "+xz);
        if(Mathf.Abs(xz.x-X)<1 && xz.y>=-12 && xz.y<=14)
        {
            var floor=hits.FirstOrDefault(h=>h.collider.name=="Battery passage and chambers" && h.normal.y>.78f &&
                Mathf.Abs(h.point.y-ground.point.y)<.3f);
            if(!floor.collider)throw new InvalidOperationException("Missing masonry ramp at "+xz);
            return floor.point;
        }
        return ground.point;
    }

    static void CheckStores()
    {
        var cabinet=GameObject.Find("Battery_line_boxes")?.GetComponent<MeshCollider>();
        var front=new Vector3(36.35f,1.34f,-5.12f);
        if(!cabinet || !cabinet.Raycast(new Ray(front,Vector3.forward),out _,1.12f))
            throw new InvalidOperationException("Southern line cabinet is missing from its new position.");
        var nearby=Physics.OverlapBox(new Vector3(36.35f,1.58f,-4.22f),
            new Vector3(.53f,.40f,.32f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
        if(nearby.Any(c=>c.name=="Battery passage and chambers" || c.name=="Battery_statuary"))
            throw new InvalidOperationException("Southern line cabinet still penetrates the pavilion or its display.");
        var body=GameObject.Find("Handcart body")?.GetComponent<BoxCollider>();
        if(!body)throw new InvalidOperationException("Missing courtyard handcart.");
        foreach(int side in new[]{-1,1})
        {
            var handhold=body.transform.TransformPoint(new Vector3(side*.52f,.35f-body.size.y*.48f,1.39f));
            var touched=Physics.OverlapSphere(handhold,.16f,~0,QueryTriggerInteraction.Ignore);
            if(touched.Any(c=>c.name=="Freight pallet" || c.name=="Upper crate" || c.name=="Side case"))
                throw new InvalidOperationException("The cart handle intersects the freight.");
        }
    }

    public static string Traverse()
    {
        Physics.SyncTransforms();
        CheckStores();
        var floors=Route.Select(OnFloor).ToArray();
        var report=new List<string>{"PASS courtyard cargo and southern line cabinet clearance."};
        var host=new GameObject("Courtyard test capsule");
        try
        {
            var walker=host.AddComponent<CharacterController>();
            walker.height=1.78f;walker.radius=.29f;walker.center=Vector3.up*.90f;
            walker.stepOffset=.29f;walker.skinWidth=.035f;walker.minMoveDistance=0;
            foreach(bool reverse in new[]{false,true})
            {
                var points=reverse ? floors.Reverse().ToArray() : floors;
                string label=reverse ? "north to south" : "south to north";
                walker.enabled=false;host.transform.position=points[0]+Vector3.up*.055f;
                walker.enabled=true;Physics.SyncTransforms();
                float bottom=float.PositiveInfinity;
                Vector3 Nav(Vector3 point)
                {
                    if(!NavMesh.SamplePosition(point,out var hit,.75f,NavMesh.AllAreas) ||
                       Mathf.Abs(hit.position.y-point.y)>.43f)
                        throw new InvalidOperationException("Unreachable tunnel checkpoint "+point);
                    return hit.position;
                }
                float routeLength=0;
                for(int i=1;i<points.Length;i++)
                {
                    var a=Nav(points[i-1]);var b=Nav(points[i]);
                    var path=new NavMeshPath();
                    if(!NavMesh.CalculatePath(a,b,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new InvalidOperationException("Disconnected "+label+" at "+points[i]);
                    var corners=path.corners;
                    float leg=0;
                    for(int c=1;c<corners.Length;c++)leg+=Vector3.Distance(corners[c-1],corners[c]);
                    if(leg>Vector3.Distance(a,b)*1.55f+.8f)
                        throw new InvalidOperationException("Navigation detours around the portal at "+b);
                    routeLength+=leg;
                    bool reached=false;
                    for(int j=0;j<650;j++)
                    {
                        var delta=points[i]-host.transform.position;delta.y=0;
                        if(delta.magnitude<.055f){reached=true;break;}
                        walker.Move(delta.normalized*.044f+Vector3.down*.035f);
                        bottom=Mathf.Min(bottom,host.transform.position.y);
                    }
                    if(!reached || Mathf.Abs(host.transform.position.y-points[i].y)>.31f)
                        throw new InvalidOperationException($"Soldier could not walk {label} at {host.transform.position}; expected {points[i]}");
                }
                if(bottom> -2.59f)throw new InvalidOperationException("Walk did not enter the tunnel.");
                report.Add($"PASS {label}: {points.Length} physical checkpoints, {routeLength:F2} m on navigation, lowest feet {bottom:F2} m.");
            }
        }
        finally
        {
            if(Application.isPlaying)Destroy(host);else DestroyImmediate(host);
        }
        return string.Join(Environment.NewLine,report);
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(directory);
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<8;i++)yield return null;
        try
        {
            string report=Traverse();
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                OsowiecFieldCheck.Shot(directory,"01-south-portal",new Vector3(44.6f,3.8f,-14.1f),new Vector3(X,-1.5f,-4.35f),61);
                OsowiecFieldCheck.Shot(directory,"02-north-portal",new Vector3(44.7f,4.0f,15.0f),new Vector3(X,-1.5f,5.35f),62);
                OsowiecFieldCheck.Shot(directory,"03-yard-stores",new Vector3(33.5f,4.1f,-2.0f),new Vector3(37f,.4f,-3.3f),65);
                OsowiecFieldCheck.Shot(directory,"04-central-trench",new Vector3(-29,6.5f,-2.7f),new Vector3(-21,0,2.1f),62);
            }
            File.WriteAllText(Path.Combine(directory,"passage.txt"),report);
            Debug.Log(report);Application.Quit(0);
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(directory,"passage.txt"),ex.ToString());
            Debug.LogException(ex);Application.Quit(2);
        }
    }
}
