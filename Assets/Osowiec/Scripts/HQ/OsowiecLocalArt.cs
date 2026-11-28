using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

// The same table entry supplies both the paper in the UI and the sheet on a 3D desk.
public sealed class OsowiecLocalArt : MonoBehaviour
{
    [SerializeField] LocalizedTexture artwork=new();
    [SerializeField] RawImage picture;
    [SerializeField] Renderer surface;
    [SerializeField] OsowiecPlanner planner;
    [SerializeField] int slot;
    [SerializeField] bool menuBackdrop;
    string displayedLocale;
    public bool Matches(RawImage image,Renderer renderer,int index,OsowiecPlanner map) => picture==image && surface==renderer && slot==index && planner==map;
    public void Bind(string key, RawImage image, Renderer renderer, int materialIndex, OsowiecPlanner map)
    {
        artwork.SetReference(OsowiecMenuLanguage.Art,key);
        picture=image;surface=renderer;slot=materialIndex;planner=map;
        menuBackdrop=key==OsowiecMenuLanguage.MenuBackdrop;
    }
    void OnEnable()
    {
        if(menuBackdrop && picture)
        {
            picture.enabled=false;
            LocalizationSettings.SelectedLocaleChanged+=HidePreviousLanguage;
        }
        artwork.AssetChanged+=Apply;
    }
    void OnDisable()
    {
        artwork.AssetChanged-=Apply;
        if(menuBackdrop)
        {
            LocalizationSettings.SelectedLocaleChanged-=HidePreviousLanguage;
            displayedLocale=null;
            if(picture){picture.enabled=false;picture.texture=null;}
        }
    }
    void HidePreviousLanguage(Locale selected)
    {
        if(picture && selected && displayedLocale!=selected.Identifier.Code)picture.enabled=false;
    }
    void Apply(Texture texture)
    {
        if(!texture)return;
        if(picture)
        {
            picture.texture=texture;
            if(menuBackdrop)
            {
                displayedLocale=LocalizationSettings.SelectedLocale.Identifier.Code;
                picture.enabled=true;
            }
        }
        if(planner)planner.survey=texture as Texture2D;
        if(surface)
        {
            var properties=new MaterialPropertyBlock();
            surface.GetPropertyBlock(properties,slot);
            properties.SetTexture("_BaseMap",texture);
            properties.SetTexture("_MainTex",texture);
            surface.SetPropertyBlock(properties,slot);
        }
    }
}

