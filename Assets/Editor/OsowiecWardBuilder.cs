using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class OsowiecWardBuilder
{
    const string Source = "Assets/Osowiec/Art/Ward/";
    const string Materials = "Assets/Osowiec/Art/Cast/Game/";
    const string Sounds = "Assets/Osowiec/Atmosphere/Resources/OsowiecSound/";
    static readonly string[] Actions={"Idle","Walk","Carry","CarryBack","Tend","Lift","Write"};

    static T Asset<T>(string path) where T:Object =>
        AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing ward asset "+path);

    static Transform Part(Transform parent,string name) =>
        parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name)
        ?? throw new InvalidOperationException("Missing ward survey part "+name);

    static RuntimeAnimatorController Motions()
    {
        string path=Source+"WardOrderly.fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        if(importer==null) throw new InvalidOperationException("Export make_ward_orderlies.py before generating the station.");
        if(importer.animationType!=ModelImporterAnimationType.Generic || !importer.importAnimation)
        {
            importer.animationType=ModelImporterAnimationType.Generic;importer.importAnimation=true;
            importer.optimizeGameObjects=false;importer.SaveAndReimport();
        }
        var clips=importer.clipAnimations.Length>0 ? importer.clipAnimations : importer.defaultClipAnimations;
        if(clips.Length==0) throw new InvalidOperationException("No staff motion takes in "+path);
        if(clips.Any(c=>!c.loopTime || !c.lockRootPositionXZ || !c.lockRootHeightY || !c.lockRootRotation))
        {
            foreach(var c in clips)
            {
                c.loopTime=true;c.lockRootPositionXZ=true;c.lockRootHeightY=true;c.lockRootRotation=true;
            }
            importer.clipAnimations=clips;importer.SaveAndReimport();
        }
        const string controllerPath=Source+"Ward staff.controller";
        var animator=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(!animator)animator=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine=animator.layers[0].stateMachine;
        var sourceClips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")).ToArray();
        foreach(string key in Actions)
        {
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==key)??machine.AddState(key);
            var clip=sourceClips.FirstOrDefault(c=>c.name.EndsWith(key,StringComparison.Ordinal));
            if(!clip)throw new InvalidOperationException(key+" missing from "+string.Join(", ",sourceClips.Select(c=>c.name)));
            if(state.motion!=clip)state.motion=clip;
            if(key=="Idle")machine.defaultState=state;
        }
        EditorUtility.SetDirty(animator);
        return animator;
    }

    static GameObject Model(Transform parent,string label,string name)
    {
        var baseNode=new GameObject(label);
        baseNode.transform.SetParent(parent,false);
        var basis=new GameObject("Metric frame").transform;basis.SetParent(baseNode.transform,false);
        var model=(GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(Source+name+".fbx"),baseNode.scene);
        model.transform.SetParent(basis,false);
        var origin=Part(model.transform,"WardOrigin");
        Vector3 localOrigin=baseNode.transform.InverseTransformPoint(origin.position);
        var forward=baseNode.transform.InverseTransformPoint(Part(model.transform,"WardFront").position)-localOrigin;
        var up=baseNode.transform.InverseTransformPoint(Part(model.transform,"WardUp").position)-localOrigin;
        basis.localRotation=Quaternion.Inverse(Quaternion.LookRotation(forward.normalized,up.normalized));
        basis.localScale=Vector3.one/up.magnitude;
        basis.localPosition-=baseNode.transform.InverseTransformPoint(origin.position);
        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Asset<Material>(Materials+m.name.Split('.')[0]+".mat")).ToArray();
            renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true;
        }
        return baseNode;
    }

    // Exposed to the cart builder as well. Move/rotate 'parent' or its 'label' child, leaving the calibrated FBX intact.
    public static Animator MakeAttendant(Transform parent,string label)
    {
        var ctrl=Motions();
        GameObject outer=Model(parent,label,"WardOrderly");
        var animator=outer.GetComponentInChildren<Animator>(true);
        if(!animator)animator=outer.transform.GetChild(0).GetChild(0).gameObject.AddComponent<Animator>();
        animator.applyRootMotion=false;
        animator.runtimeAnimatorController=ctrl;
        animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
        return animator;
    }

    public static void Install(OsowiecReliefColumn relief)
    {
        var root=new GameObject("Западный лазарет").transform;
        var ward=root.gameObject.AddComponent<OsowiecWesternWard>();
        ward.relief=relief;
        string[] names={"Дежурный фельдшер","Санитар с водой","Носильщик у изголовья","Носильщик у ног"};
        ward.bodies=names.Select(n=>MakeAttendant(root,n)).ToArray();
        ward.people=names.Select(n=>root.Find(n)).ToArray();
        foreach(var person in ward.people)
        {
            var body=person.gameObject.AddComponent<CapsuleCollider>();
            body.center=Vector3.up*.88f;body.radius=.235f;body.height=1.73f;
            person.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        }
        ward.people[0].localScale=Vector3.one*.985f; ward.people[1].localScale=Vector3.one*.965f;
        ward.stretcher=Model(root,"Принятые носилки","WardLitter").transform;
        ward.waterPail=Model(root,"Эмалированное ведро","WardPail").transform;
        ward.waterPail.position=new Vector3(-44.18f,.13f,-12.13f);
        ward.occupiedBeds=new GameObject[3];
        var care=root.gameObject.AddComponent<OsowiecWardCare>();
        care.ward=ward;
        care.residents=new MeshCollider[2];
        var originalWool=Asset<Material>(Materials+"HeavyWool.mat");
        const string woolPath=Source+"Damp blankets.mat";
        var damp=AssetDatabase.LoadAssetAtPath<Material>(woolPath);
        if(!damp){damp=new Material(originalWool);AssetDatabase.CreateAsset(damp,woolPath);}
        damp.SetColor("_BaseColor",new Color(.70f,.65f,.50f));
        damp.SetFloat("_Smoothness",.44f);
        EditorUtility.SetDirty(damp);
        for(int i=0;i<5;i++)
        {
            var surveyBed=GameObject.Find("Ward_admitted_"+(i+1))
                ??throw new InvalidOperationException("Re-export the west ward, bed "+(i+1));
            surveyBed.SetActive(false);
            var patient=Model(root,"Раненый на койке "+(i+1),"WardPatient");
            float cot=-52.32f+i*1.82f;
            patient.transform.SetPositionAndRotation(new Vector3(cot,-.025f,-8.98f),Quaternion.Euler(0,180+(i==0?3:i==1?-4:0),0));
            var patientMesh=patient.GetComponentInChildren<MeshFilter>();
            var renderer=patientMesh.GetComponent<Renderer>();
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m==originalWool?damp:m).ToArray();
            if(i<2)
            {
                var contact=patientMesh.gameObject.AddComponent<MeshCollider>();
                contact.sharedMesh=patientMesh.sharedMesh;
                patient.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
                care.residents[i]=contact;
                patient.name=i==0?"Перевязка у западной стены":"Раненый под наблюдением";
                if(i==1)patient.transform.localScale=new Vector3(1.03f,.96f,.95f);
                continue;
            }
            var clip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            clip.name="Карточка у изножья";
            Object.DestroyImmediate(clip.GetComponent<Collider>());
            clip.transform.SetPositionAndRotation(new Vector3(-52.32f+i*1.82f,-.104f,-9.92f),Quaternion.Euler(2,0,-4));
            clip.transform.localScale=new Vector3(.245f,.102f,.008f);
            clip.transform.SetParent(patient.transform,true);
            clip.GetComponent<Renderer>().sharedMaterial=Asset<Material>("Assets/Osowiec/HQ/Art/HQ_Dispatch.mat");
            var nameplate=new GameObject("Приёмная бирка").AddComponent<TMPro.TextMeshPro>();
            nameplate.transform.SetPositionAndRotation(clip.transform.position-clip.transform.forward*.006f,clip.transform.rotation);
            nameplate.transform.SetParent(patient.transform,true);
            nameplate.rectTransform.sizeDelta=new Vector2(.24f,.095f);
            nameplate.font=Asset<TMPro.TMP_FontAsset>("Assets/Osowiec/HQ/FieldSans.asset");
            nameplate.fontSize=.34f;
            nameplate.alignment=TMPro.TextAlignmentOptions.Center;
            nameplate.color=new Color(.13f,.085f,.036f);
            nameplate.text=OsowiecWesternWard.PatientName(i-2);
            ward.occupiedBeds[i-2]=patient;
        }
        ward.curtain=GameObject.Find("Ward_final_curtain")??throw new InvalidOperationException("Ward shutter missing");
        ward.curtain.transform.position+=Vector3.right*2.07f;
        ward.responses=Enumerable.Range(1,3).Select(i=>Asset<AudioClip>(Sounds+"WardCall"+i+".wav")).ToArray();
        var roof=GameObject.Find("Roof_Ward_torn_oilcloth").GetComponent<MeshRenderer>();
        const string canopyPath=Source+"Shelter oilcloth.mat";
        var oilcloth=AssetDatabase.LoadAssetAtPath<Material>(canopyPath);
        if(!oilcloth)
        {
            oilcloth=new Material(roof.sharedMaterial){name="Shelter oilcloth"};
            AssetDatabase.CreateAsset(oilcloth,canopyPath);
        }
        oilcloth.SetFloat("_Cull",0);
        oilcloth.SetColor("_BaseColor",new Color(.36f,.33f,.24f));
        ApplyWovenSurface(oilcloth,"Canvas",.9f);
        oilcloth.doubleSidedGI=true;roof.sharedMaterial=oilcloth;
        EditorUtility.SetDirty(oilcloth);
        Material Cloth(string key,string fabric,Color tint,float sheen)
        {
            string path=Source+key+".mat";
            var result=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!result){result=new Material(Asset<Material>("Assets/Osowiec/Art/Fort1916/Canvas.mat"));AssetDatabase.CreateAsset(result,path);}
            result.SetColor("_BaseColor",tint);
            ApplyWovenSurface(result,fabric,sheen);
            result.SetFloat("_Cull",0);result.doubleSidedGI=true;
            EditorUtility.SetDirty(result);
            return result;
        }
        var lining=Cloth("Shelter canvas lining","Canvas",new Color(.86f,.78f,.61f),.64f);
        var burlap=Cloth("Field litter burlap","Repair flax",new Color(.92f,.78f,.51f),.70f);
        GameObject.Find("Roof_Ward_canvas_lining").GetComponent<MeshRenderer>().sharedMaterial=lining;
        GameObject.Find("Roof_Ward_burlap_patch").GetComponent<MeshRenderer>().sharedMaterial=burlap;
        GameObject.Find("Ward_spare_canvas_litter").GetComponent<MeshRenderer>().sharedMaterial=burlap;
        var fill=new GameObject("Рассеянный свет от входа в лазарет").AddComponent<Light>();
        fill.transform.SetParent(root,false);fill.transform.position=new Vector3(-51.42f,.88f,-10.43f);
        fill.type=LightType.Point;fill.range=6.1f;fill.intensity=2.5f;
        fill.color=new Color(.89f,.87f,.75f);fill.shadows=LightShadows.None;
        foreach(Vector3 point in new[]{new Vector3(-49.06f,1.065f,-12.62f),new Vector3(-50.40f,1.065f,-8.01f)})
        {
            var lamp=new GameObject("Свет фонаря в перевязочной").AddComponent<Light>();
            lamp.transform.SetParent(root,false);lamp.transform.position=point;
            lamp.type=LightType.Point;lamp.range=6.8f;lamp.intensity=3.3f;
            lamp.color=new Color(1f,.69f,.36f);lamp.shadows=LightShadows.Soft;lamp.shadowBias=.045f;
        }
        ward.ResetWard();
        OsowiecFieldRoofs.Install();
    }

    static void ApplyWovenSurface(Material target,string fabric,float smoothness)
    {
        var source=Asset<Material>("Assets/Osowiec/Art/Fort1916/"+fabric+".mat");
        target.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));
        target.SetTexture("_MainTex",source.GetTexture("_BaseMap"));
        target.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));
        var mask=source.GetTexture("_MetallicGlossMap");
        target.SetTexture("_MetallicGlossMap",mask);
        target.SetTexture("_OcclusionMap",mask);
        target.SetFloat("_OcclusionStrength",.45f);
        target.SetFloat("_Smoothness",smoothness);
        target.EnableKeyword("_NORMALMAP");
        target.EnableKeyword("_METALLICSPECGLOSSMAP");
        target.EnableKeyword("_OCCLUSIONMAP");
    }

    [MenuItem("Steel & Ashes/Review/Western ward")]
    public static void Verify()
    {
        var ward=Object.FindFirstObjectByType<OsowiecWesternWard>()??throw new InvalidOperationException("Open the generated operation to inspect the ward.");
        var care=ward.GetComponent<OsowiecWardCare>();
        if(!care || care.residents.Length!=2 || care.residents.Any(c=>!c || !c.gameObject.activeInHierarchy))
            throw new InvalidOperationException("Both resident patients and their blanket surfaces must be present before admissions.");
        if (ward.people.Length!=4 || ward.occupiedBeds.Length!=3 || !ward.relief || ward.bodies.Any(a=>!a || !a.runtimeAnimatorController) ||
            ward.people.Any(t=>t.GetComponentInChildren<Unit>(true)!=null ||
                !t.TryGetComponent<CapsuleCollider>(out var capsule) || capsule.isTrigger || capsule.radius>.25f ||
                !t.GetComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild))
            throw new InvalidOperationException("Staff, service geometry, beds or relief connection are inconsistent.");
        Physics.SyncTransforms();
        var rampPath=new NavMeshPath();
        Vector3 pad=new(-49.4f,.05f,-15.8f), aisle=new(-49.4f,-.53f,-11.66f);
        if (!NavMesh.SamplePosition(pad,out var upper,.65f,NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(aisle,out var lower,.65f,NavMesh.AllAreas) ||
            !NavMesh.CalculatePath(upper.position,lower.position,NavMesh.AllAreas,rampPath) ||
            rampPath.status!=NavMeshPathStatus.PathComplete)
            throw new InvalidOperationException("The ward ramp must join the level floor in the baked navigation.");
        // Use a second hierarchy so an inspection never leaves the actual scene in an admitted state.
        var host=new GameObject("Ward service probe"){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var probe=host.AddComponent<OsowiecWesternWard>();
            Transform Child(string n)
            {
                var node=new GameObject(n);node.hideFlags=HideFlags.HideAndDontSave;node.transform.SetParent(host.transform,false);return node.transform;
            }
            probe.people=Enumerable.Range(0,4).Select(i=>Child("staff "+i)).ToArray();
            probe.stretcher=Child("litter");probe.waterPail=Child("water");
            probe.occupiedBeds=Enumerable.Range(0,3).Select(i=>Child("bed "+i).gameObject).ToArray();
            probe.curtain=Child("curtain").gameObject;probe.ResetWard();
            int carryingFrames=0;
            for(int i=0;i<2400;i++)
            {
                probe.Tick(.05f,Mathf.Min(3,i/60+1),false);
                if(probe.stretcher.gameObject.activeSelf)carryingFrames++;
            }
            if(probe.Admitted!=3 || !probe.curtain.activeSelf || probe.occupiedBeds.Any(b=>!b.activeSelf) || carryingFrames<300)
                throw new InvalidOperationException("Visual admissions must complete once for each of three arrivals.");
            probe.ResetWard();
            var passer=Child("passing unit").gameObject.AddComponent<Unit>();
            passer.transform.position=new Vector3(-49.40f,-.46f,-12.52f);
            Unit[] obstruction={passer};
            for(int i=0;i<400;i++)probe.Tick(.05f,3,false,obstruction);
            var halted=probe.stretcher.position;
            var first=probe.people[2].position;var second=probe.people[3].position;
            for(int i=0;i<100;i++)probe.Tick(.05f,3,false,obstruction);
            if(probe.Admitted!=0 || !probe.stretcher.gameObject.activeSelf ||
                Vector3.Distance(first,probe.people[2].position)>.001f || Vector3.Distance(second,probe.people[3].position)>.001f ||
                Vector3.Distance(halted,probe.stretcher.position)>.001f)
                throw new InvalidOperationException("Obstructed bearers must wait together with their litter.");
            passer.transform.position+=Vector3.left*6;
            for(int i=0;i<2400;i++)probe.Tick(.05f,3,false,obstruction);
            if(probe.Admitted!=3)throw new InvalidOperationException("Queued admissions were lost after the landing cleared.");
            Debug.Log($"[Osowiec ward] Four orderlies, two resident patients, three receiving beds, three complete handoffs, {carryingFrames} carrying samples, occupied landing waits/resumes with 3 admissions, ramps {upper.position} -> {lower.position}.");
        }
        finally{Object.DestroyImmediate(host);}
    }
}
