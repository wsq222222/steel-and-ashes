using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Runs in the player with -osowiec-field-check <directory> -osowiec-battery-watch.
internal static class OsowiecBatteryWatchCheck
{
    internal static IEnumerator Run()
    {
        Time.captureDeltaTime=1f/30f;
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<12;i++)yield return null;
        var placement=Object.FindAnyObjectByType<ArmyDeploymentController>();
        var cell=OsowiecDeploymentSectors.Cells[0][0];
        if(!placement.TryPlaceOfferAtGridCell(2,cell))
            throw new InvalidOperationException("Could not place the scout for the battery check.");
        var scout=placement.DeployedAt(cell);
        Object.FindAnyObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<5;i++)yield return null;
        var game=GameManager.Instance;
        var sentries=Object.FindObjectsByType<OsowiecBatteryPatrol>(FindObjectsSortMode.None);
        if(sentries.Length<2 || game.IsArmyDeploymentPhase() || game.IsBotTurn())
            throw new InvalidOperationException("Battery patrol check needs two active guards on the player turn.");
        uint turn=game.TurnSequence;
        var walked=new bool[sentries.Length];
        for(int frame=0;frame<360;frame++)
        {
            yield return null;
            for(int i=0;i<sentries.Length;i++)
            {
                var animator=sentries[i].GetComponent<OsowiecCastMotion>().body;
                walked[i]|=animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk");
            }
        }
        int onWatch=0;
        for(int i=0;i<sentries.Length;i++)
            if(walked[i] && sentries[i].DistanceWalked>1.2f)onWatch++;
        if(game.TurnSequence!=turn || onWatch<2)
            throw new InvalidOperationException("Guards failed to walk during the player turn: "+
                string.Join(", ",sentries.Select((guard,i)=>$"{guard.name} {guard.DistanceWalked:F2}m clip={walked[i]}")));
        game.PauseGame();
        float[] paused=sentries.Select(s=>s.DistanceWalked).ToArray();
        for(int i=0;i<10;i++)yield return null;
        if(sentries.Where((s,i)=>s.DistanceWalked!=paused[i]).Any())throw new InvalidOperationException("Guard moved while paused.");
        game.ResumeGame();

        var cc=scout.GetComponent<CharacterController>();
        var rig=scout.GetComponent<OsowiecCastMotion>().body;
        var bones=rig.GetComponentsInChildren<Transform>();
        var thigh=bones.Single(b=>b.name=="LThigh");
        var shin=bones.Single(b=>b.name=="LShin");
        var foot=bones.Single(b=>b.name=="LFoot");
        Vector3 entrance=new Vector3(35.0f,0,OsowiecGalleryStairs.CentreZ);
        if(!NavMesh.SamplePosition(entrance,out var floor,2f,NavMesh.AllAreas) ||
            !OsowiecHitBody.TryFloor(floor.position,out var standing))
            throw new InvalidOperationException("No landing at gallery foot.");
        cc.enabled=false;
        scout.transform.position=new Vector3(entrance.x,standing.y+.04f,entrance.z);
        cc.enabled=true;
        Physics.SyncTransforms();
        float baseY=scout.transform.position.y, summit=0;
        for(int leg=0;leg<2;leg++)
        {
            float sign=leg==0?1f:-1f;
            float goal=leg==0 ? OsowiecGalleryStairs.HeadX+.22f : entrance.x;
            scout.transform.rotation=Quaternion.LookRotation(Vector3.right*sign);
            float minAngle=180,maxAngle=0;
            int contacts=0,frames=0;
            while((goal-scout.transform.position.x)*sign>.04f && frames++<380)
            {
                cc.Move(Vector3.right*(sign*.043f)+Vector3.down*.018f);
                yield return null;
                if(!OsowiecGalleryStairs.OnFlight(scout.transform.position))continue;
                float angle=Vector3.Angle(thigh.position-shin.position,foot.position-shin.position);
                minAngle=Mathf.Min(minAngle,angle);
                maxAngle=Mathf.Max(maxAngle,angle);
                if(OsowiecHitBody.TryFloor(foot.position,out var tread))
                {
                    float height=foot.position.y-tread.y;
                    if(height>.045f && height<.21f)contacts++;
                }
            }
            if(frames>=380 || contacts<10 || maxAngle-minAngle<13)
                throw new InvalidOperationException($"Gallery stair leg {leg} stopped at {scout.transform.position}, contacts {contacts}, knee {minAngle:F1}–{maxAngle:F1}.");
            if(leg==0)summit=scout.transform.position.y;
            Debug.Log($"Gallery stair leg {leg}: {frames} frames, {contacts} tread contacts, knee {minAngle:F1}–{maxAngle:F1}");
        }
        if(summit<baseY+2.0f || Mathf.Abs(scout.transform.position.y-baseY)>.28f)
            throw new InvalidOperationException("Scout did not change floors and return to the courtyard.");
        Debug.Log($"PASS battery watch and stairs: {onWatch} animated guards, elevation {summit-baseY:F2}m, {Application.buildGUID}");
    }
}
