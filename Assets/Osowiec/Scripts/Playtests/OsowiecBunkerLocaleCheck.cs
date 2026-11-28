using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class OsowiecBunkerLocaleCheck
{
    const string Preference="Osowiec.Language";
    const string LanguageButton="Pause overlay/Field desk/Options page/Language";

    // Unity Localization releases its temporary operation after completion. Hold
    // a separate reference while the assertion waits for the UI and renderers.
    internal sealed class Artwork : IDisposable
    {
        public readonly AsyncOperationHandle<Texture> Loading;
        public Artwork(string key)
        {
            Loading=LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<Texture>(OsowiecMenuLanguage.Art,key);
            Addressables.ResourceManager.Acquire(Loading);
        }
        public void Dispose()=>Addressables.Release(Loading);
    }

    static IEnumerator Until(Func<bool> test,string message)
    {
        float deadline=Time.realtimeSinceStartup+20f;
        while(!test())
        {
            if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(message);
            yield return null;
        }
    }

    static Button Control()
    {
        var menu=Object.FindFirstObjectByType<OsowiecMenuLanguage>();
        if(!menu)throw new InvalidOperationException("Missing scene language controller");
        var control=menu.transform.Find(LanguageButton)?.GetComponent<Button>();
        if(!control)throw new InvalidOperationException("Missing settings language button");
        return control;
    }

    static IEnumerator Select(Button control,string code)
    {
        yield return LocalizationSettings.InitializationOperation;
        if(LocalizationSettings.SelectedLocale.Identifier.Code!=code)control.onClick.Invoke();
        yield return Until(()=>LocalizationSettings.SelectedLocale.Identifier.Code==code &&
            OsowiecMenuLanguage.Text("Расстановка")==(code=="en"?"Deployment":"Расстановка"),
            "Language control or string database failed to select "+code);
    }

    static void RestorePreference(bool existed,string value)
    {
        if(existed)PlayerPrefs.SetString(Preference,value);
        else PlayerPrefs.DeleteKey(Preference);
        PlayerPrefs.Save();
    }

    public static IEnumerator Check()
    {
        bool hadPref=PlayerPrefs.HasKey(Preference);
        string preference=PlayerPrefs.GetString(Preference);
        var roster=Object.FindFirstObjectByType<OsowiecPaperPlacement>();
        if(!roster)throw new InvalidOperationException("Deployment sheet is unavailable");
        var desk=roster.transform.Find("Deployment survey/Survey desk");
        var map=desk.Find("Plan of the operation").GetComponent<RawImage>();
        var planner=Object.FindFirstObjectByType<OsowiecPlanner>(FindObjectsInactive.Include);
        var roll=desk.Find("Personnel roll");
        TMP_Text Label(string path)=>roll.Find(path).GetComponent<TMP_Text>();
        var title=Label("Placement title");
        var description=Label("Role particulars");
        var supply=Label("Supply and strength");
        var role=Label("Roster 1/Assignment");
        var control=Control();
        int originalOffer=roster.SelectedOffer;
        try
        {
            string initialCounts=null;
            foreach(string code in new[]{"ru","en","ru"})
            {
                bool en=code=="en";
                yield return Select(control,code);
                using var asset=new Artwork("SurveySheet");
                yield return asset.Loading;
                var expected=asset.Loading.Result;
                if(!expected || expected.name!=(en?"SurveySheet-en":"SurveySheet"))
                    throw new InvalidOperationException("Wrong surveyed artwork in "+code+" asset table.");
                roster.SelectOffer(1);
                bool Correct()=>title.text==(en?"Deployment":"Расстановка") &&
                    role.text==(en?"Observer":"Наблюдатель") &&
                    description.text.Contains(en?"Cyclops · Observer":"Циклоп · Наблюдатель") &&
                    description.text.Contains(en?"periscope":"перископ") &&
                    supply.text.StartsWith(en?"Supply":"Снабжение") &&
                    map.texture==expected && planner && planner.survey==expected;
                yield return Until(Correct,"Deployment did not fully switch to "+code);
                for(int i=0;i<8;i++)yield return null;
                if(!Correct())throw new InvalidOperationException("Locale overwritten on subsequent frames: "+code);
                string counts=Regex.Replace(supply.text,@"[^0-9/]+"," ").Trim();
                if(initialCounts==null)initialCounts=counts;
                if(counts!=initialCounts)throw new InvalidOperationException("Locale switch changed deployment values");
            }
            Debug.Log("PASS bunker deployment localization RU/EN/RU: live language button, native string and art tables, roster, supply and field map.");
        }
        finally
        {
            roster.SelectOffer(originalOffer);
            RestorePreference(hadPref,preference);
        }
    }

    static IEnumerator CheckTitlePhoto(string code)
    {
        var nav=Object.FindFirstObjectByType<OsowiecJournalNavigation>();
        var image=nav.transform.Find("Pause overlay/Field desk/Pause navigation/hq")?.GetComponent<RawImage>();
        if(!image || !image.gameObject.activeInHierarchy)throw new InvalidOperationException("Title photograph is not visible in the welcome menu");
        var binding=image.GetComponent<OsowiecLocalArt>();
        if(!binding || !binding.enabled)throw new InvalidOperationException("Release has no localized title photograph");
        using var asset=new Artwork(OsowiecMenuLanguage.MenuBackdrop);
        yield return asset.Loading;
        var texture=asset.Loading.Result;
        string name=OsowiecMenuLanguage.MenuBackdrop+(code=="en"?"-en":"");
        if(!texture || texture.name!=name || texture.width!=3840 || texture.height!=2160)
            throw new InvalidOperationException("Missing native title photograph for "+code);
        yield return Until(()=>image.enabled && image.texture==texture,"Title menu kept the previous language photograph");
        Canvas.ForceUpdateCanvases();
        var corners=new Vector3[4];image.rectTransform.GetWorldCorners(corners);
        float width=corners[2].x-corners[0].x, height=corners[2].y-corners[0].y;
        float aspect=(float)texture.width/texture.height;
        bool fillsScreen=corners[0].x<=2 && corners[0].y<=2 && corners[2].x>=Screen.width-2 && corners[2].y>=Screen.height-2;
        bool tightCrop=Mathf.Abs(width-Screen.width)<3 || Mathf.Abs(height-Screen.height)<3;
        if(!fillsScreen || !tightCrop || height<=0 || Mathf.Abs(width/height-aspect)>.005f || image.uvRect!=new Rect(0,0,1,1))
            throw new InvalidOperationException($"Title photograph distorted/clipped at {Screen.width}x{Screen.height}: {width}x{height}");
    }

    // Call before opening the HQ order. Test the effective texture bound at each
    // renderer slot; inspecting the paper material asset alone misses property blocks.
    public static IEnumerator CheckHeadquartersArt()
    {
        bool hadPref=PlayerPrefs.HasKey(Preference);
        string preference=PlayerPrefs.GetString(Preference);
        var control=Control();
        var items=new List<(Renderer renderer,int index,string key)>();
        foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            var mats=renderer.sharedMaterials;
            for(int i=0;i<mats.Length;i++)
            {
                var mat=mats[i];
                if(!mat || !mat.HasProperty("_BaseMap"))continue;
                string key=mat.GetTexture("_BaseMap")?.name;
                if(key=="SurveySheet" || key=="Dispatch" || key=="Profiles" || key=="LedgerCover")
                    items.Add((renderer,i,key));
            }
        }
        if(!items.Exists(item=>item.key=="SurveySheet"))throw new InvalidOperationException("HQ table has no physical survey sheet");
        var block=new MaterialPropertyBlock();
        try
        {
            foreach(string code in new[]{"ru","en","ru"})
            {
                yield return Select(control,code);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-draft-backdrop")<0)
                    yield return CheckTitlePhoto(code);
                foreach(var item in items)
                {
                    if(!item.renderer.gameObject.activeInHierarchy)continue;
                    using var asset=new Artwork(item.key);
                    yield return asset.Loading;
                    var expected=asset.Loading.Result;
                    if(!expected || expected.name!=item.key+(code=="en"?"-en":""))
                        throw new InvalidOperationException("Missing "+code+" artwork "+item.key);
                    bool Match()
                    {
                        item.renderer.GetPropertyBlock(block,item.index);
                        return block.GetTexture("_BaseMap")==expected;
                    }
                    yield return Until(Match,"Wrong paper on "+item.renderer.name+" in "+code);
                }
            }
            Debug.Log($"PASS bunker world-art localization RU/EN/RU at {Screen.width}x{Screen.height}: {items.Count} paper material bindings.");
        }
        finally{RestorePreference(hadPref,preference);}
    }
}
