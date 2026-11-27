using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static partial class OsowiecHeadquartersBuilder
{
    static readonly Color PaperInk=new(.16f,.135f,.095f);
    static readonly Color RedInk=new(.39f,.13f,.09f);
    static readonly Color Parchment=new(.83f,.76f,.61f);
    static readonly Color GhostInk=new(.34f,.30f,.22f);
    const string Approaches="От лазарета к телефону ведёт западный ход. Из центральной траншеи идите по настилу до развилки. За телефоном в низине нужна маска. Заряд у двери каземата [E] поднимет тревогу.";

    static RawImage Print(Transform parent,string asset,Vector2 anchor,Vector2 size)
    {
        var area=Rect(parent,asset,anchor,size);
        var print=area.gameObject.AddComponent<RawImage>();
        // The photographed title screen is supplied by the localized asset table.
        if(asset=="hq")print.enabled=false;
        else print.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/OsowiecJournal/"+asset+".png");
        print.raycastTarget=false;
        return print;
    }
    static void PaperStock(RectTransform panel)
    {
        var shadow=panel.gameObject.AddComponent<Shadow>();
        shadow.effectColor=new Color(.10f,.075f,.04f,.48f);
        shadow.effectDistance=new Vector2(5,-8);
        var fiber=Print(panel,"journal",new(.5f,.5f),panel.sizeDelta);
        fiber.uvRect=new Rect(.152f,.15f,.301f,.70f);
        fiber.color=new Color(.99f,.96f,.88f,.76f);
    }

    static TMP_Text Type(Transform parent,string name,string copy,float x,float y,float w,float h,int points,Color color)
    {
        var text=TextAt(parent,name,copy,new(x,y),new(w,h),points,TextAlignmentOptions.MidlineLeft);
        text.color=color;
        text.raycastTarget=false;
        return text;
    }
    static Button Link(Transform parent,string id,string copy,float x,float y,float width,UnityAction action,bool dark=false)
    {
        var button=ButtonAt(parent,id,copy,new(x,y),action,width);
        button.GetComponent<Image>().color=dark?RedInk:new Color(.61f,.51f,.34f,.13f);
        var caption=button.GetComponentInChildren<TMP_Text>();
        caption.color=dark?Ivory:PaperInk;
        caption.fontSize=29;
        caption.alignment=TextAlignmentOptions.Center;
        Line(button.transform,"Hairline",new(.5f,.01f),new(width,1.3f),dark?Brass:GhostInk);
        return button;
    }

    static void InstallFieldOrder(Canvas canvas,MissionSelectUI picker,MissionDefinition definition,Sprite map,OsowiecTableHint terminal)
    {
        var overlay=Fill(canvas.transform,"Dossier overlay",new(.047f,.047f,.035f,.99f));
        var desk=Rect(overlay,"Cartographic table",new(.5f,.5f),new(1920,1080));
        Print(desk,"table",new(.5f,.5f),new(1920,1080));
        PlaceSurvey(desk,map);
        var paper=Box(desk,"Operation order",new(.218f,.529f),new(560,734),Parchment,false);
        PaperStock(paper);
        Line(paper,"Folio rule",new(.5f,.891f),new(478,2),RedInk);
        Type(paper,"Department","ШТАБ КРЕПОСТИ   /   БОЕВОЙ ПРИКАЗ",.5f,.936f,476,34,19,RedInk);
        var title=Type(paper,"Operation name",definition.displayName,.5f,.785f,478,98,71,PaperInk) as TextMeshProUGUI;
        var objective=Type(paper,"Briefing",definition.objectiveShort,.5f,.560f,474,210,26,PaperInk) as TextMeshProUGUI;
        objective.alignment=TextAlignmentOptions.TopLeft;
        objective.enableAutoSizing=true; objective.fontSizeMin=23; objective.fontSizeMax=26;
        objective.lineSpacing=2;
        Line(paper,"Orders divider",new(.5f,.389f),new(478,1),GhostInk);
        Type(paper,"Conditions",Approaches,.5f,.274f,474,146,22,GhostInk);
        var start=Link(paper,"Deploy","К расстановке",.50f,.102f,474,null,true);
        UnityEventTools.AddPersistentListener(start.onClick,terminal.PrepareMission);
        var back=Link(desk,"Close dossier","К штабному столу",.218f,.131f,366,null);
        definition.previewSprite=map;
        EditorUtility.SetDirty(definition);
        picker.Initialize(overlay.gameObject,back,null,null,start,title,objective,null,new[]{definition});
        EditorUtility.SetDirty(picker);
        overlay.gameObject.SetActive(false);
    }

    static RectTransform PlaceSurvey(Transform desk,Sprite map,Vector2? anchor=null,float width=1148)
    {
        if(!desk.GetComponent<OsowiecSurveyDeskFit>())desk.gameObject.AddComponent<OsowiecSurveyDeskFit>();
        var paper=Rect(desk,"Plan of the operation",anchor??new Vector2(.669f,.529f),new(width,width*map.rect.height/map.rect.width));
        var image=paper.gameObject.AddComponent<RawImage>();image.texture=map.texture;image.raycastTarget=false;
        paper.SetSiblingIndex(1);
        var shadow=paper.gameObject.AddComponent<Shadow>();
        shadow.effectColor=new Color(.08f,.065f,.035f,.38f);shadow.effectDistance=new Vector2(4,-6);
        return paper;
    }

    [MenuItem("Steel and Ashes/Update headquarters briefing sheet")]
    public static void UpdateBriefingSheet()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var root=System.Array.Find(scene.GetRootGameObjects(),o=>o.name=="Field dossier canvas");
            var desk=root.transform.Find("Dossier overlay/Cartographic table");
            foreach(string label in new[]{"fort-ink","Survey rubric","Signal post","Casemates","Battery","Plan of the operation"})
            {
                var old=desk.Find(label);
                if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            PlaceSurvey(desk,CreateOperationMap());
            var conditions=desk.Find("Operation order/Conditions").GetComponent<TMP_Text>();
            conditions.text=Approaches;conditions.fontSize=22;conditions.rectTransform.sizeDelta=new(474,146);
            conditions.rectTransform.anchorMin=conditions.rectTransform.anchorMax=new(.5f,.274f);
            foreach(string label in new[]{"Operation order","Close dossier"})
            {
                var rect=(RectTransform)desk.Find(label);
                var anchor=rect.anchorMin;anchor.x=.218f;rect.anchorMin=rect.anchorMax=anchor;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene))throw new System.InvalidOperationException("Briefing save failed");
        }
        finally{if(opened)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);}
    }

    static void InstallJournal(GameManager manager,bool hq)
    {
        if(!manager)return;
        var legacy=new SerializedObject(manager).FindProperty("pausePanel").objectReferenceValue as GameObject;
        if(legacy)legacy.SetActive(false);
        var canvas=CreateCanvas("Field options canvas",43);
        var prefs=canvas.gameObject.AddComponent<OsowiecFieldSettings>();
        var nav=canvas.gameObject.AddComponent<OsowiecJournalNavigation>();
        var overlay=Fill(canvas.transform,"Pause overlay",new(.037f,.039f,.028f,.99f));
        var frame=Rect(overlay,"Field desk",new(.5f,.5f),new(1920,1080));
        var home=Fill(frame,"Pause navigation",Color.clear,false);
        Print(home,hq?"hq":"journal",new(.5f,.5f),new(1920,1080));
        if(hq)
        {
            Type(home,"HQ edition","IRON PROTOCOL",.267f,.818f,710,43,25,Brass);
            Type(home,"Masthead","Steel & Ashes",.285f,.697f,794,146,98,Ivory);
            Type(home,"Place","Осовец  ·  Полевой штаб",.267f,.587f,710,55,35,Ivory);
            foreach(var b in new[]{Link(home,"Continue","К штабному столу",.228f,.418f,525,manager.ResumeGame,true),
                Link(home,"Settings","Настройки",.228f,.332f,525,manager.OpenSettings),
                Link(home,"Quit","Выйти из игры",.228f,.251f,525,nav.RequestExit)})
                b.GetComponentInChildren<TMP_Text>().color=Ivory;
        }
        else
        {
            Type(home,"Pause rubric","ПОЛЕВОЙ ЖУРНАЛ",.314f,.780f,500,44,24,RedInk);
            Type(home,"Pause title","Привал",.314f,.662f,500,125,80,PaperInk);
            var sketch=Rect(home,"Journal plan",new(.306f,.43f),new(464,277));
            var map=sketch.gameObject.AddComponent<RawImage>();map.texture=CreateOperationMap().texture;map.raycastTarget=false;
            Type(home,"Pause place","Форт I   ·   Осовец",.313f,.255f,490,49,26,PaperInk);
            Link(home,"Continue","Продолжить",.687f,.678f,490,manager.ResumeGame,true);
            Link(home,"Settings","Настройки",.687f,.583f,490,manager.OpenSettings);
            Link(home,"Restart","Начать заново",.687f,.488f,490,manager.RestartGame);
            Link(home,"Return","Вернуться в штаб",.687f,.393f,490,prefs.ReturnToHeadquarters);
            Link(home,"Quit","Выйти из игры",.687f,.225f,490,nav.RequestExit);
            var journal=canvas.gameObject.AddComponent<OsowiecObjectiveJournal>();
            Link(home,"Open objectives","Журнал задач   [J]",.687f,.303f,490,journal.Open);
            var spread=Fill(frame,"Mission objectives",Color.clear,false);
            Print(spread,"journal",new(.5f,.5f),new(1920,1080));
            Type(spread,"Objectives heading","Журнал задач",.311f,.78f,530,100,61,PaperInk);
            var objectives=Type(spread,"Live objectives","",.308f,.478f,537,536,27,PaperInk);
            objectives.alignment=TextAlignmentOptions.TopLeft;
            Type(spread,"Trip title","Санитарная колонна",.675f,.765f,555,90,41,PaperInk);
            var status=Type(spread,"Current trip","",.675f,.55f,555,170,29,GhostInk);
            Link(spread,"Close objectives","Закрыть журнал",.67f,.30f,460,journal.Close,true);
            journal.Bind(spread.gameObject,home.gameObject,objectives,status);
            spread.gameObject.SetActive(false);
        }
        var settings=Fill(frame,"Options page",Color.clear,false);
        Print(settings,"journal",new(.5f,.5f),new(1920,1080));
        Type(settings,"Settings rubric","ПОЛЕВОЙ ЖУРНАЛ",.316f,.795f,500,40,22,RedInk);
        Type(settings,"Settings title","Настройки",.316f,.685f,500,106,63,PaperInk);
        Link(settings,"Sound tab","Звук",.310f,.548f,480,nav.Audio);
        Link(settings,"Video tab","Изображение",.310f,.462f,480,nav.Picture);
        Link(settings,"Controls tab","Управление",.310f,.376f,480,nav.Controls);
        Link(settings,"Close settings","Готово",.310f,.237f,475,manager.ApplySettings,true);
        var pages=new GameObject[3];
        var pageNames=new[]{"Громкость","Изображение","Управление"};
        for(int i=0;i<3;i++)
        {
            var page=Rect(settings,pageNames[i],new(.687f,.511f),new(524,650));
            pages[i]=page.gameObject;
            Type(page,"Chapter title",pageNames[i],.5f,.942f,488,68,42,PaperInk);
            Line(page,"Chapter rule",new(.5f,.86f),new(488,1.5f),RedInk);
        }
        var a=pages[0].transform;var v=pages[1].transform;var k=pages[2].transform;
        Slider Setting(Transform page,string name,float y,float low,float high,System.Func<float,string> number)
        {
            Type(page,name,name,.5f,y+.060f,480,43,25,PaperInk);
            var control=SliderAt(page,name,y,low,high);
            var host=(RectTransform)control.transform;
            host.anchorMin=host.anchorMax=new Vector2(.336f,y);
            string readout=name switch { "Угол обзора"=>"FOV","Качество графики"=>"Quality value","Чувствительность мыши"=>"Sensitivity value",_=>name+" value"};
            AddReadout(page,readout,y,control,number);
            return control;
        }
        Toggle Option(Transform page,string text,float y)
        {
            var control=ToggleAt(page,"",y);
            control.name=text;
            var row=(RectTransform)control.transform;
            row.sizeDelta=new Vector2(488,48);
            row.GetComponent<Image>().color=new Color(.54f,.41f,.23f,.13f);
            var label=control.GetComponentInChildren<TMP_Text>();
            label.text=text; label.color=PaperInk; label.fontSize=23;
            label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.45f,.5f);
            label.rectTransform.sizeDelta=new Vector2(405,44);
            return control;
        }
        string Percent(float f)=>f.ToString("P0");
        var music=Setting(a,"Музыка",.715f,0,1,Percent);
        var fx=Setting(a,"Эффекты и шаги",.525f,0,1,Percent);
        var ambience=Setting(a,"Окружение",.335f,0,1,Percent);
        var ui=Setting(a,"Звуки интерфейса",.145f,0,1,Percent);
        var angle=Setting(v,"Угол обзора",.730f,50,95,f=>$"{f:0}°");angle.wholeNumbers=true;
        int max=Mathf.Max(1,QualitySettings.names.Length-1);
        var quality=Setting(v,"Качество графики",.563f,0,max,f=>QualitySettings.names[Mathf.Clamp(Mathf.RoundToInt(f),0,max)]);
        quality.wholeNumbers=true;quality.interactable=QualitySettings.names.Length>1;
        var size=Setting(v,"Масштаб интерфейса",.397f,.85f,1.12f,Percent);
        var full=Option(v,"Полный экран",.264f);
        var reduced=Option(v,"Уменьшить эффекты",.169f);
        var shake=Option(v,"Уменьшить тряску",.074f);
        var look=Setting(k,"Чувствительность мыши",.723f,.35f,2.2f,f=>$"{f:0.0}×");
        var invert=Option(k,"Инверсия обзора по вертикали",.598f);
        var captions=Option(k,"Субтитры полевых событий",.496f);
        prefs.BindAccessibility(invert,captions);
        Type(k,"Movement keys","W A S D    Передвижение\nE    Взаимодействие\nR    Перезарядка\nM    План крепости     J    Журнал задач\nTab    Передать ход",.5f,.248f,490,235,23,PaperInk);
        foreach(var page in pages)
            foreach(var t in page.GetComponentsInChildren<TMP_Text>(true))
                if(t.GetComponent<OsowiecSliderReadout>()) t.color=RedInk;
        var exit = Fill(frame,"Confirm exit",new Color(.08f,.075f,.06f,.88f));
        var card = Box(exit,"Exit order",new(.50f,.50f),new(600,305),Parchment);
        PaperStock(card);
        Type(card,"Exit heading","Завершить игру?",.50f,.76f,504,75,49,PaperInk);
        Link(card,"Cancel quit","Остаться",.5f,.45f,490,nav.CancelExit,true);
        Link(card,"Confirm quit","Выйти из игры",.5f,.18f,490,prefs.QuitGame);
        exit.gameObject.SetActive(false);
        nav.Bind(pages,hq,exit.gameObject,home.gameObject);
        pages[1].SetActive(false);pages[2].SetActive(false);
        prefs.Bind(look,full,quality,reduced,shake,size,angle);
        prefs.BindMix(ambience,ui);
        manager.InitializePauseMenu(overlay.gameObject,settings.gameObject,home.gameObject,music,fx);
        settings.gameObject.SetActive(false);
        overlay.gameObject.SetActive(false);
        EditorUtility.SetDirty(manager);EditorUtility.SetDirty(prefs);
    }
}
