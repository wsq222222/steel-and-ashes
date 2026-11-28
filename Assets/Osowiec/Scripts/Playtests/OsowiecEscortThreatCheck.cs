using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OsowiecEscortThreatCheck : MonoBehaviour
{
    static void Require(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); }
    void OnEnable() => Application.logMessageReceived+=Watch;
    void OnDisable() => Application.logMessageReceived-=Watch;
    void Watch(string text,string stack,LogType type) { if(type==LogType.Exception) Application.Quit(2); }

    static void Place(Unit unit,Vector3 at)
    {
        var cc=unit.GetComponent<CharacterController>();
        cc.enabled=false;
        unit.transform.position=at;
        cc.enabled=true;
        unit.RefreshGridPositionFromWorld();
        Physics.SyncTransforms();
    }
    IEnumerator Start()
    {
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        yield return Run(true);
        yield return Run(false);
        Debug.Log("PASS escort threat: cleared garrison, protected arrival, autonomous physical interception, escort and timeout.");
        Application.Quit(0);
    }

    IEnumerator Run(bool bringEscort)
    {
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<8;i++) yield return null;
        var op=OsowiecOperation.Instance;
        var game=GameManager.Instance;
        var c=op.relief;
        var deployment=FindFirstObjectByType<ArmyDeploymentController>();
        Require(deployment.TryPlaceOfferAtGridCell(0,OsowiecDeploymentSectors.Cells[0][0]),"Deployment failed");
        var ally=FindObjectsByType<Unit>(FindObjectsSortMode.None).Single(u=>u.owner==Player.Player1);
        FindFirstObjectByType<OsowiecPaperPlacement>().Confirm();
        yield return null;
        foreach(var defender in op.FieldRoster)
            if(defender && defender.isActiveAndEnabled && defender.owner==Player.Player2) defender.TakeDamage(10000);
        game.EndGame(Player.Player2);
        Require(!game.IsGameOver() && !op.HasActiveDefenders(),"Clearing the garrison ended the mission");
        game.SwitchTurn();
        for(int frame=0;frame<4 && game.IsBotTurn();frame++) yield return null;
        Require(!game.IsBotTurn() && !game.IsGameOver(),"An empty side required a dice presentation");
        uint turn=game.TurnSequence;
        Place(ally,OsowiecOperation.SouthLine);
        c.Begin();
        Require(c.Interact(ally),"First dispatch order was rejected");
        float dispatchDeadline=Time.realtimeSinceStartup+8;
        while(!c.TryGetEscortTarget(out _) && Time.realtimeSinceStartup<dispatchDeadline) yield return null;
        Require(c.TryGetEscortTarget(out _),"First cart did not depart after the road opened");
        var guard=c.reserve[0].GetComponent<Unit>();
        Vector3 listedPost=guard.transform.position;
        Place(ally,listedPost+new Vector3(-2.8f,.05f,3.6f));
        Time.timeScale=3;
        float deadline=Time.time+10;
        while(c.ReservesArrived==0 && Time.time<deadline) yield return null;
        Require(guard.isActiveAndEnabled && c.ReservesArrived==1 && !game.IsBotTurn(),"No escort reserve arrived on the empty map");
        Vector3 entrance=guard.transform.position;
        Require(Vector3.Distance(entrance,listedPost)>3 && Vector3.Distance(entrance,ally.transform.position)>6 &&
            OsowiecHitBody.TryFloor(entrance,out _),"Reserve popped in on a watched or unsupported post");
        Place(ally,new Vector3(-38,.1f,-16));
        deadline=Time.time+53;
        while(!c.Intercepted && Time.time<deadline && !c.NeedsHelp) yield return null;
        Require(c.Intercepted && game.TurnSequence==turn,
            $"No real-time interception. Guard {entrance:F2} -> {guard.transform.position:F2}, cart {c.CartPosition:F2}, phase {c.StageName}, {c.RoadObstruction}");
        Require(Vector3.Distance(guard.transform.position,entrance)>2,"Guard never walked out of cover");
        Vector3 stopped=c.CartPosition;
        yield return new WaitForSeconds(1.1f);
        Require(Vector3.Distance(stopped,c.CartPosition)<.03f,"The intercepted wagon still advanced");
        if(bringEscort) Place(ally,stopped+Vector3.back*3.8f);
        deadline=Time.time+(bringEscort?2:13);
        while(c.Intercepted && Time.time<deadline) yield return null;
        Require(!c.Intercepted,"Guard failed to release the wagon");
        float freed=Time.time;
        if(bringEscort) Place(ally,new Vector3(-38,.1f,-16));
        while(Vector3.Distance(stopped,c.CartPosition)<.3f && Time.time<freed+3)yield return null;
        Require(Vector3.Distance(stopped,c.CartPosition)>=.3f,"The released wagon never resumed");
        Debug.Log($"[Escort threat] release={(bringEscort?"escort":"timeout")}, entry={entrance:F2}, stop={stopped:F2}, turn={turn}.");
        Time.timeScale=1;
    }
}
