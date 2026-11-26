using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in clock for functional playthroughs. Ordinary play and benchmark runs keep the engine clock.
[DefaultExecutionOrder(-900)]
public sealed class OsowiecQaClock : MonoBehaviour
{
    float rate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-osowiec-qa-rate");
        if (Application.isEditor || Array.IndexOf(args, "-osowiec-playtest") < 0 ||
            index < 0 || index + 1 >= args.Length || !int.TryParse(args[index+1], out int requested) ||
            requested < 2 || requested > 4) return;
        var clock = new GameObject("QA mission clock").AddComponent<OsowiecQaClock>();
        DontDestroyOnLoad(clock.gameObject);
        clock.rate = requested;
        Debug.Log($"[Osowiec QA] Functional clock x{requested}. Frame timings from this run are excluded from benchmarks.");
    }

    void Update()
    {
        var game = GameManager.Instance;
        if (!game || Time.timeScale == 0 || game.IsGameOver() || game.IsPaused() || OsowiecFieldKit.WheelOpen) return;
        bool inField = game.gameObject.scene.name=="Osowiec" || SceneManager.GetActiveScene().name=="Osowiec";
        Time.timeScale = inField && !game.IsArmyDeploymentPhase() ? rate : 1f;
    }
}
