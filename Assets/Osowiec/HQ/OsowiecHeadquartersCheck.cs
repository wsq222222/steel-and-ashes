using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runs only in a player explicitly launched for the headquarters interaction check.
public sealed class OsowiecHeadquartersCheck : MonoBehaviour
{
    const string SavedSwitch="Osowiec.GramophoneOn";
    string folder;
    bool hadSwitch, oldSwitch, restore;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args=Environment.GetCommandLineArgs();
        if (OsowiecUniformViewCheck.Install(args)) return;
        int i=Array.IndexOf(args,"-osowiec-headquarters-check");
        if(Application.isEditor || i<0 || i+1>=args.Length)return;
        var host=new GameObject("Headquarters interaction check"); DontDestroyOnLoad(host);
        host.AddComponent<OsowiecHeadquartersCheck>().folder=args[i+1];
    }

    void Restore()
    {
        if(!restore)return;
        if(hadSwitch) PlayerPrefs.SetInt(SavedSwitch,oldSwitch?1:0); else PlayerPrefs.DeleteKey(SavedSwitch);
        PlayerPrefs.Save(); restore=false;
    }
    void OnEnable()=>Application.logMessageReceived+=Observe;
    void OnDisable(){Application.logMessageReceived-=Observe;Restore();}
    void Observe(string message,string stack,LogType type){if(type==LogType.Exception){Restore(); Application.Quit(2);}}
    static void Check(bool ok,string detail){if(!ok)throw new InvalidOperationException("HQ check: "+detail);}
    static AudioSource Horn()=>FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Single(a=>a.name=="Gramophone horn");
    static int[] CuffVertices(SkinnedMeshRenderer skin,Transform elbow,Transform wrist)
    {
        var mesh=skin.sharedMesh; var bind=mesh.bindposes; var weights=mesh.boneWeights; var verts=mesh.vertices;
        int forearm=Array.IndexOf(skin.bones,elbow), hand=Array.IndexOf(skin.bones,wrist);
        var start=bind[forearm].inverse.MultiplyPoint3x4(Vector3.zero);
        var end=bind[hand].inverse.MultiplyPoint3x4(Vector3.zero); var axis=(end-start).normalized;
        float length=(end-start).magnitude;
        return Enumerable.Range(0,verts.Length).Where(i=>{
            float t=Vector3.Dot(verts[i]-start,axis)/length;
            return weights[i].boneIndex0==forearm && weights[i].weight0>.85f && t>.95f && t<1.10f;
        }).ToArray();
    }
    static void WalkTo(Vector3 point)
    {
        var unit=CameraManager.Instance.GetCurrentControlledUnit(); var controller=unit.GetComponent<CharacterController>();
        if(controller)controller.enabled=false;
        unit.transform.position=point;
        if(controller)controller.enabled=true;
        Physics.SyncTransforms();
    }
    IEnumerator Start()
    {
        Directory.CreateDirectory(folder); Application.runInBackground=true;
        for(int i=0;i<8;i++)yield return null;
        hadSwitch=PlayerPrefs.HasKey(SavedSwitch); oldSwitch=OsowiecHeadquartersAudio.GramophoneOn; restore=true;
        GameManager.Instance.ResumeGame(); yield return null;
        Check(FindFirstObjectByType<OsowiecDeskRain>() && GameObject.Find("DugoutStores"),"desk water or fitted-out room absent");
        var uniform=FindFirstObjectByType<OsowiecStaffBody>();
        var skin=uniform.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.bones.Contains(uniform.leftHand)&&s.bones.Contains(uniform.rightHand));
        Check(skin.enabled && uniform.transform.InverseTransformPoint(uniform.leftArm.position).x<0 && uniform.transform.InverseTransformPoint(uniform.rightArm.position).x>0,"staff shoulders or skinned glove binding");
        var leftCuff=CuffVertices(skin,uniform.leftElbow,uniform.leftHand); var rightCuff=CuffVertices(skin,uniform.rightElbow,uniform.rightHand);
        Check(leftCuff.Length>12 && rightCuff.Length>12,"missing folded sleeve vertices");
        var eye=CameraManager.Instance.GetActionCamera(); var initial=eye.transform.localRotation; float cuffError=0;
        Check(!uniform.transform.Find("Сложенный план форта"),"A handheld plan is still present.");
        var posed=new Mesh();
        foreach(int tilt in new[]{0,19,72})
        {
            eye.transform.localRotation=Quaternion.Euler(tilt,0,0);yield return null;yield return null;
            skin.BakeMesh(posed); var surface=posed.vertices;
            foreach(int side in new[]{-1,1})
            {
                var hand=side<0?uniform.leftHand:uniform.rightHand;
                var band=side<0?leftCuff:rightCuff; Vector3 center=Vector3.zero;
                foreach(int vertex in band)center+=surface[vertex];
                cuffError=Mathf.Max(cuffError,Vector3.Distance(hand.position,skin.transform.TransformPoint(center/band.Length)));
                var local=uniform.transform.InverseTransformPoint(hand.position);
                Check(local.x*side>.10f && local.y<1.22f,"Hands still carry an invisible prop.");
                if(tilt==72) {
                    var view=eye.WorldToViewportPoint(hand.position);
                    Check(view.z>0 && view.x>.01f && view.x<.99f && view.y>.01f && view.y<.99f,$"hidden resting wrist {side}: {view}");
                }
            }
        }
        Destroy(posed); eye.transform.localRotation=initial;
        Check(cuffError<.036f,$"sleeve cuff detached from wrist by {cuffError:F4} m");
        WalkTo(new Vector3(3.18f,.08f,1.18f)); yield return null; yield return null;
        var hint=FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Single(t=>t.name=="Hint");
        Check(hint.text.Contains("граммофон"),"physical stand failed to offer interaction");
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        var key=InputSystem.AddDevice<Keyboard>("HQ test keyboard"); key.MakeCurrent();
        yield return null;
        InputSystem.QueueStateEvent(key,new KeyboardState(Key.E));
        for(int frame=0;frame<5 && OsowiecHeadquartersAudio.GramophoneOn==oldSwitch;frame++)yield return null;
        InputSystem.QueueStateEvent(key,new KeyboardState()); yield return null;
        InputSystem.RemoveDevice(key);
        Check(OsowiecHeadquartersAudio.GramophoneOn!=oldSwitch,$"E on virtual keyboard did not toggle persisted {oldSwitch} (hint '{hint.text}')");
        Check(Horn().isPlaying==!oldSwitch,$"toggled to {!oldSwitch}, AudioSource.isPlaying={Horn().isPlaying}");
        SceneManager.LoadScene("OsowiecHQ"); for(int i=0;i<6;i++)yield return null;
        Check(Horn().isPlaying==!oldSwitch,"playback not restored on re-entering HQ");
        OsowiecHeadquartersAudio.ToggleGramophone(); Restore();
        GameManager.Instance.PauseGame(); GameManager.Instance.OpenSettings();
        FindFirstObjectByType<OsowiecJournalNavigation>().Audio(); yield return null;
        var slider=FindObjectsByType<Slider>(FindObjectsSortMode.None).Single(s=>s.name=="Музыка control");
        Canvas.ForceUpdateCanvases(); var host=(RectTransform)slider.transform; var knob=slider.handleRect;
        Check(host.rect.height>=48 && knob.rect.height<=32 && knob.rect.width>=22,"slider hit or thumb dimensions");
        float before=slider.value;
        var point=RectTransformUtility.WorldToScreenPoint(null,host.TransformPoint(new Vector3(-73,20)));
        var press=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(press,hits);
        Check(hits.Any(hit=>hit.gameObject.GetComponentInParent<Slider>()==slider),"blank part of slider hit area missed pointer");
        slider.OnPointerDown(press); slider.OnPointerUp(press);
        bool moved=slider.value>.20f && slider.value<.32f; slider.value=before; Check(moved,"click away from track did not set value");
        GameManager.Instance.ApplySettings(); GameManager.Instance.ResumeGame();
        var terminal=FindFirstObjectByType<HubComputerTerminal>(FindObjectsInactive.Include);
        var dossier=FindObjectsByType<MissionSelectUI>(FindObjectsSortMode.None).Single(menu=>menu.isActiveAndEnabled);
        var approach=terminal.transform.position+Vector3.back*1.43f;
        approach.y=CameraManager.Instance.GetCurrentControlledUnit().transform.position.y;
        WalkTo(approach); yield return null; yield return null;
        var deskHint=FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Single(t=>t.name=="Hint");
        Check(!dossier.IsOpen && deskHint.text.Contains("карту операций"),"desk approach did not offer the operation map");
        var deskKey=InputSystem.AddDevice<Keyboard>("HQ desk keyboard"); deskKey.MakeCurrent();
        yield return null;
        InputSystem.QueueStateEvent(deskKey,new KeyboardState(Key.E));
        for(int frame=0;frame<8 && !dossier.IsOpen;frame++)yield return null;
        InputSystem.QueueStateEvent(deskKey,new KeyboardState()); yield return null;
        InputSystem.RemoveDevice(deskKey);
        Check(dossier.IsOpen,"E beside the operations table did not open the briefing");
        yield return OsowiecMapCheck.ApplyCaptureSize();
        Canvas.ForceUpdateCanvases();
        var text=FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Single(t=>t.name=="Briefing");
        text.ForceMeshUpdate();
        Check(text.textBounds.min.y>=text.rectTransform.rect.yMin-1 && text.textBounds.max.y<=text.rectTransform.rect.yMax+1,"briefing overflows paragraph");
        var rule=(RectTransform)text.transform.parent.Find("Orders divider");
        var bottom=text.transform.TransformPoint(text.textBounds.min);
        Check(bottom.y>rule.position.y,"order text reaches the rule");
        var desk=text.transform.parent.parent;
        var sheet=desk.Find("Plan of the operation").GetComponent<UnityEngine.UI.RawImage>();
        Check(sheet.texture && sheet.texture.name=="SurveySheet" && sheet.texture.width>=1600,
            "briefing did not open the current field survey");
        var corners=new Vector3[4];sheet.rectTransform.GetWorldCorners(corners);
        var orderCorners=new Vector3[4];((RectTransform)text.transform.parent).GetWorldCorners(orderCorners);
        Check(corners[0].x>orderCorners[2].x && corners[0].y>=0 && corners[2].x<=Screen.width && corners[2].y<=Screen.height,
            $"printed field survey {corners[0]:F1} to {corners[2]:F1}, order ends at {orderCorners[2]:F1}, frame {Screen.width} × {Screen.height}");
        bool onTable=FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Any(r=>r.sharedMaterials.Any(m=>m && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")==sheet.texture));
        Check(onTable,"no physical table surface shares the briefing sheet");
        if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            yield return new WaitForEndOfFrame();
            var picture=ScreenCapture.CaptureScreenshotAsTexture();
            Check(picture && picture.width==Screen.width && picture.height==Screen.height,"native briefing capture failed");
            File.WriteAllBytes(Path.Combine(folder,"Штабной план.png"),picture.EncodeToPNG());
            Destroy(picture);
        }
        string result=$"PASS Headquarters {Screen.width}x{Screen.height} build={Application.buildGUID}. E opens the briefing at the table and toggles gramophone playback across scene reload; thumb={knob.rect.size}, target={host.rect.size}; order lines={text.textInfo.lineCount}. Free wrists beside the coat at 0/19/72 degrees and visible looking down; handheld plan absent; cuff geometry to wrist={cuffError:F4} m ({leftCuff.Length}/{rightCuff.Length} sampled sleeve vertices).";
        Debug.Log(result); File.WriteAllText(Path.Combine(folder,"Headquarters.txt"),result);
        Application.Quit(0);
    }
}
