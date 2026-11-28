using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Windows smoke test for standing posts, reserve activation and movement across sloping terrain.
public sealed class OsowiecSentryGroundCheck : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!Array.Exists(Environment.GetCommandLineArgs(), a => a == "-osowiec-sentry-ground")) return;
        var host = new GameObject("Sentry footing check");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecSentryGroundCheck>();
    }

    IEnumerator Start()
    {
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        while (!OsowiecOperation.Instance || !GameManager.Instance.IsArmyDeploymentPhase()) yield return null;
        var column = FindFirstObjectByType<OsowiecReliefColumn>();
        var sentries = FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(u => u.owner == Player.Player2 &&
                (u.gameObject.activeInHierarchy || column.alarmGuard.Contains(u.gameObject) || column.reserve.Contains(u.gameObject))).ToArray();
        if (sentries.Length != 11) { Fail("expected 11 initial and reserve posts"); yield break; }
        foreach (var unit in sentries)
            if (!StandingOnFloor(unit, .15f, out var error)) { Fail(unit.name + ": " + error); yield break; }

        var reserve = column.alarmGuard[0].GetComponent<Unit>();
        reserve.gameObject.SetActive(true);
        var lowland = column.reserve[0].GetComponent<Unit>();
        lowland.gameObject.SetActive(true);
        yield return null;
        Time.captureDeltaTime = 1f/60;
        foreach (var walker in new[] { reserve, lowland })
        {
            Vector3 post = walker.transform.position;
            Vector3 lower = walker == reserve ? new Vector3(20, 0, -7) : new Vector3(10, 0, 12);
            foreach (Vector3 goal in new[] { lower, post })
            {
                if (!working) yield break;
                yield return Walk(walker, goal, true);
            }
        }

        // Follow the gallery guard through an actual portal to its brick floor under the vaulted roof.
        var gallery = column.reserve[1].GetComponent<Unit>();
        gallery.gameObject.SetActive(true);
        yield return null;
        yield return Walk(gallery, new Vector3(8,0,-12), false);
        if (!working) yield break;
        Vector3 inside = gallery.transform.position;
        if (inside.x >= 10 || inside.y > .3f) { Fail("gallery guard missed the interior floor: "+inside); yield break; }
        foreach (var casualty in new[] {reserve, gallery})
        {
            yield return Fall(casualty);
            if (!working) yield break;
        }
        Debug.Log($"PASS sentry ground: {sentries.Length} posts, four hillside traversals, one gallery entry, two grounded falls. Build={Application.buildGUID}.");
        Application.Quit(0);
    }

    bool working = true;
    IEnumerator Walk(Unit walker, Vector3 goal, bool slope)
    {
        Vector3 start = walker.transform.position;
        walker.SetRemainingMoveMeters(12);
        var motion = StartCoroutine(BotController.Instance.MoveTowardsWorldTargetByBudgetInternal(walker, goal, 12));
        int frames = 0;
        float deadline = Time.time + 6;
        while (Time.time < deadline)
        {
            if (!StandingOnFloor(walker, .3f, out var error))
            {
                StopCoroutine(motion); Fail(walker.name+" moving: "+error); yield break;
            }
            ++frames;
            yield return null;
        }
        Vector3 finish = walker.transform.position;
        float travel = Vector3.Distance(start, finish);
        if (travel < 1f || (slope && Mathf.Abs(finish.y-start.y)<.15f))
        {
            Fail(walker.name+" stayed on its post or never crossed the slope: "+finish); yield break;
        }
        Debug.Log($"[Sentry footing] {walker.name} {start:F2} -> {finish:F2}, {travel:F2} m, {frames} grounded frames.");
    }

    // A failed post can be entirely below the terrain, where a downward cast from the boots misses it.
    // The nearest baked layer distinguishes an underground room from the earth above its ceiling.
    static bool StandingOnFloor(Unit unit, float tolerance, out string error)
    {
        Vector3 feet = unit.transform.position;
        if (!NavMesh.SamplePosition(feet, out var nav, 3.5f, NavMesh.AllAreas))
        {
            error = "post is off the navigation surface at "+feet; return false;
        }
        var hits = Physics.RaycastAll(new Vector3(feet.x, nav.position.y+.55f, feet.z), Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
        var ground = hits.Where(h=> h.normal.y>.52f && !h.collider.GetComponentInParent<Unit>())
            .OrderBy(h=>Mathf.Abs(nav.position.y-h.point.y)).ToArray();
        if (ground.Length==0 || Mathf.Abs(feet.y-ground[0].point.y)>tolerance)
        {
            error = $"feet={feet:F3} nav={nav.position:F3}, floor={(ground.Length>0 ? ground[0].point.y.ToString("F3") : "missing")}";
            return false;
        }
        error=null;
        return true;
    }
    IEnumerator Fall(Unit unit)
    {
        var skin = unit.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.Contains("Uniform"));
        var baked = new Mesh();
        unit.TakeDamage(10000);
        int samples = 0;
        for (int frame = 0; frame < 102; frame++)
        {
            yield return null;
            if (!unit || !skin) { Fail("fall disappeared before animation completed"); yield break; }
            if (frame % 16 != 0) continue;
            skin.BakeMesh(baked,false);
            var vertices = baked.vertices;
            var surface = new System.Collections.Generic.Dictionary<Vector2Int,float>();
            int buried = 0, tested = 0;
            for (int i=0; i<vertices.Length; i+=9)
            {
                var point = skin.transform.TransformPoint(vertices[i]);
                var key = new Vector2Int(Mathf.RoundToInt(point.x*6),Mathf.RoundToInt(point.z*6));
                if (!surface.TryGetValue(key,out float y))
                {
                    if (!OsowiecHitBody.TryFloor(new Vector3(key.x/6f,unit.transform.position.y,key.y/6f),out var ground)) continue;
                    y=ground.y;
                    surface.Add(key,y);
                }
                if (point.y < y-.115f) buried++;
                tested++;
            }
            if (tested<400 || buried/(float)tested>.05f)
            {
                Fail($"{unit.name} fall frame {frame}: {buried} of {tested} vertices buried");
                yield break;
            }
            samples++;
        }
        Destroy(baked);
        Debug.Log($"[Sentry footing] {unit.name} fell on support across {samples} animated meshes.");
    }

    void Fail(string reason) { working=false; Debug.LogError("FAIL sentry ground: "+reason);Application.Quit(2); }
}
