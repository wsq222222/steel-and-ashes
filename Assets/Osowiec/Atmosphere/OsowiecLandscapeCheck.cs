using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class OsowiecLandscapeCheck
{
    internal static IEnumerator Run(string output)
    {
        var end = new WaitForEndOfFrame();
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for (int i=0;i<8;i++) yield return null;
        var atmosphere = UnityEngine.Object.FindFirstObjectByType<OsowiecAtmosphere>();
        var paper = UnityEngine.Object.FindFirstObjectByType<OsowiecPaperPlacement>();
        var deployment = UnityEngine.Object.FindFirstObjectByType<ArmyDeploymentController>();
        var camera = CameraManager.Instance.GetTacticalCamera();
        int original = QualitySettings.GetQualityLevel();
        OsowiecFieldSettings.ReducedEffectsOverride = false;
        try
        {
            QualitySettings.SetQualityLevel(QualitySettings.names.Length-1, false);
            paper.Hide();
            paper.NextSector();
            for (int i=0;i<5;i++) yield return null;
            yield return end;
            if (!atmosphere.DrawingTacticalHaze || !camera.enabled || QualitySettings.names.Length<2 ||
                Shader.GetGlobalFloat("_OsowiecLandscapeLow")>.5f)
                throw new InvalidOperationException("Full quality tactical weather missing");
            OsowiecMapCheck.SaveFrame(output,"01-низина-с-высоты");
            QualitySettings.SetQualityLevel(0,false);
            for (int i=0;i<4;i++) yield return null;
            yield return end;
            if (Shader.GetGlobalFloat("_OsowiecLandscapeLow") != 1 || !atmosphere.DrawingTacticalHaze)
                throw new InvalidOperationException("Low-cost map weather missing");
            OsowiecMapCheck.SaveFrame(output,"02-низина-низкое-качество");
        }
        finally
        {
            QualitySettings.SetQualityLevel(original, false);
            OsowiecFieldSettings.ReducedEffectsOverride = null;
        }
        paper.Show();
        if (!deployment.TryPlaceOfferAtGridCell(2,OsowiecDeploymentSectors.Cells[1][0])) throw new InvalidOperationException("Deployment failed");
        paper.Confirm();
        for (int i=0;i<4;i++) yield return null;
        var actor=UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None).Single(u=>u.owner==Player.Player1);
        var planner=OsowiecPlanner.Instance;
        planner.Select(actor);
        planner.ConfirmOrder();
        for (int i=0;i<5;i++) yield return null;
        yield return end;
        if (!CameraManager.Instance.IsActionMode() || atmosphere.DrawingTacticalHaze)
            throw new InvalidOperationException("Overhead haze reached the first-person camera");
        OsowiecMapCheck.SaveFrame(output,"03-низина-от-первого-лица");
        File.WriteAllText(Path.Combine(output,"Landscape.txt"),
            $"PASS {Application.buildGUID}. Tactical camera high/low, single-sample low cloud map, first-person camera clear. {Screen.width}x{Screen.height}.");
    }
}
