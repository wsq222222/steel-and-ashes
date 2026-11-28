using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class OsowiecBunkerLocalisation
{
    const string Folder="Assets/Osowiec/HQ/Localisation";
    const string Source="Assets/Osowiec/HQ/Art/Source~/bunker-ui.tsv";
    const string MenuPath="Assets/Osowiec/HQ/Art/Textures/"+OsowiecMenuLanguage.MenuBackdrop;
    static readonly string[] Graphics={"SurveySheet","Dispatch","Profiles","LedgerCover",OsowiecMenuLanguage.MenuBackdrop};
    static bool MenuPairExists=>File.Exists(MenuPath+".png") && File.Exists(MenuPath+"-en.png");

    // Called immediately after creating the first English image, before its import.
    // Copy the native resolution importer, with a new GUID independent of the Russian photograph.
    public static void PrepareEnglishMenuMeta()
    {
        string meta=MenuPath+"-en.png.meta";
        if(File.Exists(meta))return;
        string original=File.ReadAllText(MenuPath+".png.meta");
        const string ru="599a66f7b7c60e547adb3c195d149ca8";
        if(!original.Contains(ru))throw new InvalidDataException("Original HQ artwork GUID changed");
        File.WriteAllText(meta,original.Replace(ru,"70d0b75762504206a9de78613ef638c5"));
    }

    public static void FinaliseMenuArtwork()
    {
        if(!MenuPairExists)throw new FileNotFoundException("Both native Russian and English HQ photographs are required for the release.");
        PrepareEnglishMenuMeta();
        BuildTables();
        var table=LocalizationEditorSettings.GetAssetTableCollection(OsowiecMenuLanguage.Art);
        var ru=AssetDatabase.LoadAssetAtPath<Texture2D>(MenuPath+".png");
        var en=AssetDatabase.LoadAssetAtPath<Texture2D>(MenuPath+"-en.png");
        if(!ru || !en || ru.width!=3840 || ru.height!=2160 || en.width!=3840 || en.height!=2160 ||
            ru.imageContentsHash==en.imageContentsHash)
            throw new InvalidDataException("HQ photographs must be distinct 3840 x 2160 images.");
        foreach(string code in new[]{"ru","en"})
        {
            var source=code=="ru"?ru:en;
            var entry=((AssetTable)table.GetTable(code)).GetEntry(OsowiecMenuLanguage.MenuBackdrop);
            if(entry?.Guid!=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)))
                throw new InvalidDataException("Missing HQ backdrop registration for "+code);
        }
    }

    [MenuItem("Steel and Ashes/Localisation/Update bunker and deployment")]
    public static void BuildTables()
    {
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Osowiec/HQ","Localisation");
        var settings=LocalizationEditorSettings.ActiveLocalizationSettings;
        if(!settings)
        {
            settings=ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings,Folder+"/Settings.asset");
            LocalizationEditorSettings.ActiveLocalizationSettings=settings;
        }
        var selectors=settings.GetStartupLocaleSelectors();
        selectors.Clear();
        selectors.Add(new CommandLineLocaleSelector());
        selectors.Add(new PlayerPrefLocaleSelector{PlayerPreferenceKey="Osowiec.Language"});
        selectors.Add(new SpecificLocaleSelector{LocaleId="ru"});
        var locales=new List<Locale>();
        foreach(var code in new[]{"ru","en"})
        {
            var path=Folder+"/"+code+".asset";
            var locale=AssetDatabase.LoadAssetAtPath<Locale>(path);
            if(!locale)
            {
                locale=Locale.CreateLocale(code);
                AssetDatabase.CreateAsset(locale,path);
            }
            LocalizationEditorSettings.AddLocale(locale);
            locales.Add(locale);
        }
        LocalizationSettings.ProjectLocale=locales[0];
        EditorUtility.SetDirty(settings);
        var strings=LocalizationEditorSettings.GetStringTableCollection(OsowiecMenuLanguage.Strings)
            ?? LocalizationEditorSettings.CreateStringTableCollection(OsowiecMenuLanguage.Strings,Folder,locales);
        var art=LocalizationEditorSettings.GetAssetTableCollection(OsowiecMenuLanguage.Art)
            ?? LocalizationEditorSettings.CreateAssetTableCollection(OsowiecMenuLanguage.Art,Folder,locales);
        var russian=(StringTable)strings.GetTable("ru");
        var english=(StringTable)strings.GetTable("en");
        foreach(string line in File.ReadAllLines(Source))
        {
            if(string.IsNullOrWhiteSpace(line))continue;
            var cells=line.Split('\t');
            if(cells.Length!=2)throw new InvalidDataException("Missing translation: "+line);
            string ru=cells[0].Replace("\\n","\n"), en=cells[1].Replace("\\n","\n");
            russian.AddEntry(ru,ru);english.AddEntry(ru,en);
        }
        foreach(var key in Graphics)
        {
            // The intermediate player shoots the localized prints before menu photographs exist.
            if(key==OsowiecMenuLanguage.MenuBackdrop && !MenuPairExists)continue;
            var prefix=key=="SurveySheet"?"Assets/Osowiec/Art/":"Assets/Osowiec/HQ/Art/Textures/";
            foreach(var locale in locales)
            {
                string path=prefix+key+(locale.Identifier.Code=="ru"?"":"-en")+".png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(!importer)throw new FileNotFoundException(path);
                bool menu=key==OsowiecMenuLanguage.MenuBackdrop;
                if(importer.npotScale!=TextureImporterNPOTScale.None || importer.mipmapEnabled || importer.textureCompression!=TextureImporterCompression.Uncompressed || (menu && importer.maxTextureSize<4096))
                {
                    if(menu)importer.maxTextureSize=4096;
                    importer.npotScale=TextureImporterNPOTScale.None;
                    importer.textureCompression=TextureImporterCompression.Uncompressed;
                    importer.mipmapEnabled=false;
                    importer.SaveAndReimport();
                }
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                art.AddAssetToTable(locale.Identifier,key,texture);
            }
        }
        foreach(var item in new Object[]{russian,english,strings,strings.SharedData,art,art.SharedData})EditorUtility.SetDirty(item);
        var addressables=AddressableAssetSettingsDefaultObject.GetSettings(true);
        addressables.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
        EditorUtility.SetDirty(addressables);
        AssetDatabase.SaveAssets();
        OsowiecHeadquartersBuilder.UpdateDeploymentSheet();
        BindScene(OsowiecProjectPaths.HeadquartersScene,russian);
        BindScene(OsowiecProjectPaths.OperationScene,russian);
        AssetDatabase.SaveAssets();
        Debug.Log($"Bunker localisation: {russian.Count} strings, {Graphics.Length-(MenuPairExists?0:1)} artwork pairs, ru/en.");
    }

    static void BindScene(string path,StringTable source)
    {
        var scene=SceneManager.GetSceneByPath(path);
        bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var nav in root.GetComponentsInChildren<OsowiecJournalNavigation>(true))
                    LanguageControl(nav);
                foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    string value=label.text;
                    if(string.IsNullOrEmpty(value))continue;
                    if(label.GetComponentInParent<Button>(true))
                    {
                        if(value=="‹")value="−";
                        else if(value=="›")value="+";
                        else value=value.Trim(' ','‹','›','←','→');
                    }
                    label.text=value;
                    if(label.name=="Hint" || label.name=="Minimum personnel" || label.name=="Field note")
                    {
                        var inherited=label.GetComponent<LocalizeStringEvent>();
                        if(inherited)Object.DestroyImmediate(inherited);
                        continue;
                    }
                    if(source.GetEntry(value)==null)continue;
                    var binding=label.GetComponent<LocalizeStringEvent>()??label.gameObject.AddComponent<LocalizeStringEvent>();
                    binding.StringReference.SetReference(OsowiecMenuLanguage.Strings,value);
                    if(binding.OnUpdateString.GetPersistentEventCount()==0)
                        UnityEventTools.AddPersistentListener(binding.OnUpdateString,label.SetText);
                    EditorUtility.SetDirty(binding);
                }
                foreach(var image in root.GetComponentsInChildren<RawImage>(true))
                {
                    if(image.name=="hq" && image.transform.parent.name=="Pause navigation")
                    {
                        Add<OsowiecBackdropCover>(image.gameObject);
                        // Addressables owns both photographs. Keeping a serialized Texture2D reference
                        // here would duplicate the Russian asset as a player-scene dependency.
                        image.texture=null;
                        image.enabled=false;
                        var backdrop=BindArt(image.gameObject,OsowiecMenuLanguage.MenuBackdrop,image,null,0,null);
                        backdrop.enabled=MenuPairExists;
                        continue;
                    }
                    if(!image.texture)continue;
                    var key=image.texture.name;
                    if(key=="table")
                        Add<OsowiecBackdropCover>(image.gameObject);
                    if(key=="FieldPortraitAtlas")
                    {
                        if(image.transform.parent.name.StartsWith("Roster ") && int.TryParse(image.transform.parent.name.Substring(7),out int index))
                        {
                            image.uvRect=new Rect(index/3f,0,1f/3f,1);
                            image.rectTransform.sizeDelta=new Vector2(60,72);
                        }
                    }
                    if(Array.IndexOf(Graphics,key)>=0) BindArt(image.gameObject,key,image,null,0,null);
                }
                var frame=root.transform.Find("Pause overlay/Field desk");
                if(frame)Add<OsowiecSurveyDeskFit>(frame.gameObject);
                foreach(var planner in root.GetComponentsInChildren<OsowiecPlanner>(true))
                    BindArt(planner.gameObject,"SurveySheet",null,null,0,planner);
                if(scene.name!="OsowiecHQ")continue;
                foreach(var mesh in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats=mesh.sharedMaterials;
                    for(int slot=0;slot<mats.Length;slot++)
                    {
                        var mat=mats[slot];
                        if(!mat || !mat.HasProperty("_BaseMap"))continue;
                        string key=mat.GetTexture("_BaseMap")?.name;
                        if(Array.IndexOf(Graphics,key)>=0)BindArt(mesh.gameObject,key,null,mesh,slot,null);
                    }
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save "+path);
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }

    static T Add<T>(GameObject target) where T:Component =>
        target.GetComponent<T>()??target.AddComponent<T>();

    static OsowiecLocalArt BindArt(GameObject host,string key,RawImage image,Renderer mesh,int index,OsowiecPlanner planner)
    {
        var binding=Array.Find(host.GetComponents<OsowiecLocalArt>(),entry=>entry.Matches(image,mesh,index,planner))
            ??host.AddComponent<OsowiecLocalArt>();
        binding.Bind(key,image,mesh,index,planner);
        EditorUtility.SetDirty(binding);
        return binding;
    }

    static void LanguageControl(OsowiecJournalNavigation nav)
    {
        var controller=Add<OsowiecMenuLanguage>(nav.gameObject);
        var page=nav.transform.Find("Pause overlay/Field desk/Options page");
        if(!page || page.Find("Language"))return;
        var copy=Object.Instantiate(page.Find("Sound tab").gameObject,page,false);
        copy.name="Language";
        var rect=(RectTransform)copy.transform;
        rect.anchorMin=rect.anchorMax=new(.310f,.164f);
        rect.anchoredPosition=Vector2.zero;
        var button=copy.GetComponent<Button>();
        button.onClick=new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(button.onClick,controller.SwitchLanguage);
        copy.GetComponentInChildren<TMP_Text>(true).text="Язык · Русский";
    }
}

