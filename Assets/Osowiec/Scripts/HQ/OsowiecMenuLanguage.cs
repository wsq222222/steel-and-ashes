using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public sealed class OsowiecMenuLanguage : MonoBehaviour
{
    public const string Strings = "Osowiec UI";
    public const string Art = "Osowiec Artwork";
    public const string MenuBackdrop = "HeadquartersBackdrop";
    static StringTable table;
    public static event Action Changed;
    int generation;

    public static string Text(string source)
    {
        if(string.IsNullOrEmpty(source) || !table) return source;
        return table.GetEntry(source)?.LocalizedValue ?? source;
    }

    public static string Format(string source,params object[] values) =>
        string.Format(Text(source),values);

    IEnumerator Start()
    {
        LocalizationSettings.SelectedLocaleChanged += LoadLocale;
        yield return LocalizationSettings.InitializationOperation;
        if(this && LocalizationSettings.SelectedLocale != null) LoadLocale(LocalizationSettings.SelectedLocale);
    }

    void LoadLocale(Locale locale)
    {
        if(!locale) return;
        int request=++generation;
        var handle=LocalizationSettings.StringDatabase.GetTableAsync(Strings,locale);
        void Use(StringTable loaded)
        {
            if(!this || generation!=request || !loaded)return;
            table=loaded;
            Changed?.Invoke();
        }
        if(handle.IsDone)Use(handle.Result);
        else handle.Completed+=result=>Use(result.Result);
    }

    public void SwitchLanguage()
    {
        var current=LocalizationSettings.SelectedLocale;
        if(!current)return;
        var target=LocalizationSettings.AvailableLocales.GetLocale(current.Identifier.Code=="en"?"ru":"en");
        if(target) LocalizationSettings.SelectedLocale=target;
    }

    void OnDestroy()
    {
        ++generation;
        LocalizationSettings.SelectedLocaleChanged-=LoadLocale;
    }
}
