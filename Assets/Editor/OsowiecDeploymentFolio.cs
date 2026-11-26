using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

public static partial class OsowiecHeadquartersBuilder
{
    public static void InstallPaperDeployment(Canvas canvas,ArmyDeploymentController army,Button original,int total)
    {
        var roster=canvas.gameObject.AddComponent<OsowiecPaperPlacement>();
        var survey=Rect(canvas.transform,"Field overview controls",new(.5f,.5f),new(1920,1080));
        var toolbar=Box(survey,"Survey controls",new(.5f,.921f),new(1140,105),Parchment);
        var surveyTitle=Type(toolbar,"Field sector","",.195f,.64f,380,38,29,PaperInk);
        Type(toolbar,"Survey keys","WASD — обзор    Колесо — масштаб",.195f,.31f,380,32,20,GhostInk);
        Link(toolbar,"Next approach","Сменить участок  [Q]",.54f,.5f,316,roster.NextSector);
        Link(toolbar,"Resume paper","К расстановке  [Esc]",.839f,.5f,318,roster.Show,true);
        survey.gameObject.SetActive(false);
        var sheet=Fill(canvas.transform,"Deployment survey",new(.06f,.055f,.04f,1));
        var desk=Rect(sheet,"Survey desk",new(.5f,.5f),new(1920,1080));
        Print(desk,"table",new(.5f,.5f),new(1920,1080));
        var paper=Box(desk,"Personnel roll",new(.232f,.51f),new(520,842),Parchment);
        PaperStock(paper);
        Type(paper,"Personnel rubric","ЛИСТ КОМПЛЕКТОВАНИЯ",.50f,.953f,452,33,19,RedInk);
        Type(paper,"Placement title","Расстановка",.50f,.886f,452,80,58,PaperInk);
        Line(paper,"Folio rule",new(.50f,.832f),new(452,1.5f),GhostInk);
        var supply=Type(paper,"Supply and strength","",.50f,.795f,452,40,24,PaperInk);
        var rows=new Image[3];
        var roles=new[]{"Штурмовик","Наблюдатель","Разведчик"};
        var atlas=OsowiecFieldCast.Art<Texture2D>("FieldPortraitAtlas.png");
        var offers=new SerializedObject(army).FindProperty("offers");
        for(int i=0;i<3;i++)
        {
            var offer=offers.GetArrayElementAtIndex(i);
            var unit=(offer.FindPropertyRelative("unitPrefab").objectReferenceValue as GameObject).GetComponent<Unit>();
            int cost=offer.FindPropertyRelative("costPoints").intValue;
            var row=Box(paper,"Roster "+i,new(.5f,.705f-i*.099f),new(452,74),Parchment,true);
            var button=row.gameObject.AddComponent<Button>();
            rows[i]=row.GetComponent<Image>(); button.targetGraphic=rows[i];
            UnityEventTools.AddIntPersistentListener(button.onClick,roster.SelectOffer,i);
            var drag=row.gameObject.AddComponent<OsowiecPaperDrag>(); drag.board=roster;drag.offer=i;
            var portrait=Print(row,"portraits",new(.10f,.50f),new(67,72));
            portrait.texture=atlas;portrait.uvRect=new Rect(i/3f+.047f,0,.24f,1);
            Type(row,"Role name",unit.GetUnitDisplayName(),.56f,.67f,270,41,31,PaperInk);
            Type(row,"Assignment",roles[i],.56f,.28f,270,33,21,GhostInk);
            Type(row,"Issue cost",cost.ToString(),.935f,.50f,39,39,27,PaperInk);
        }
        var description=Type(paper,"Role particulars","",.5f,.364f,452,140,23,PaperInk);
        description.alignment=TextAlignmentOptions.TopLeft;
        var requirement=Type(paper,"Minimum personnel","Для начала поставьте хотя бы одного бойца.",.5f,.236f,452,44,21,RedInk);
        var go=Link(paper,"Begin operation","Начать операцию    →",.50f,.154f,452,roster.Confirm,true);
        var hq=Link(paper,"Return to headquarters","←   В штаб",.50f,.065f,452,roster.Headquarters);
        hq.GetComponent<RectTransform>().sizeDelta=new(452,52);
        var targets=OsowiecDeploymentSectors.Cells;
        var grid=Object.FindFirstObjectByType<ChessGrid>();
        int slotCount=targets[0].Length*targets.Length;
        var buttons=new Image[slotCount];var portraits=new RawImage[slotCount];var blank=new TMP_Text[slotCount];
        for(int sector=0;sector<targets.Length;sector++)
        {
            var title=Type(desk,"Sector title "+sector,OsowiecDeploymentSectors.Titles[sector],.5f,.5f,184,42,27,RedInk);
            title.alignment=TextAlignmentOptions.Center;
            for(int n=0;n<targets[sector].Length;n++)
            {
                int index=sector*targets[sector].Length+n;
                var tile=Box(desk,"Position "+index,new(.5f,.5f),
                    new(57,57),Parchment,true);
                buttons[index]=tile.GetComponent<Image>();
                var button=tile.gameObject.AddComponent<Button>();button.targetGraphic=buttons[index];
                UnityEventTools.AddIntPersistentListener(button.onClick,roster.UseCell,index);
                var slot=tile.gameObject.AddComponent<OsowiecPaperSlot>();slot.board=roster;slot.index=index;
                var portrait=Print(tile,"portraits",new(.5f,.5f),new(50,50));portrait.texture=atlas;portraits[index]=portrait;portrait.enabled=false;
                blank[index]=Type(tile,"Free position","+",.5f,.5f,50,50,34,PaperInk);
                blank[index].alignment=TextAlignmentOptions.Center;
            }
        }
        var note=Type(desk,"Field note","Выберите бойца и укажите место на карте.",.644f,.235f,848,60,22,PaperInk);
        Link(desk,"Return to ground","Осмотреть местность",.620f,.145f,302,roster.Hide);
        Link(desk,"Reset placement","Сбросить",.816f,.145f,186,roster.ResetRoster);
        var ghost=Print(desk,"portraits",new(.5f,.5f),new(42,48));
        ghost.texture=atlas;
        ghost.raycastTarget=false;ghost.gameObject.SetActive(false);
        roster.Bind(army,sheet.gameObject,go,original,total,supply,description,note,rows,buttons,portraits,blank,ghost,
            survey.gameObject,surveyTitle,requirement);
        FitDeploymentSheet(desk,roster,grid);
        EditorUtility.SetDirty(roster);
    }

    static void FitDeploymentSheet(RectTransform desk,OsowiecPaperPlacement roster,ChessGrid grid)
    {
        foreach(string name in new[]{"fort-ink","Field title","Placement boundary 0","Placement boundary 1",
            "Roster inset 0","Roster inset 1","Plan of the operation"})
        {
            var old=desk.Find(name);
            if(old)Object.DestroyImmediate(old.gameObject);
        }
        var map=PlaceSurvey(desk,CreateOperationMap(),new Vector2(.671f,.605f),1110);
        var projection=OsowiecSurveyProjection.Current;
        void Move(string name,Vector2 anchor)
        {
            var r=(RectTransform)desk.Find(name);
            r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=Vector2.zero;
        }
        Move("Personnel roll",new(.216f,.51f));Move("Field note",new(.671f,.160f));
        Move("Return to ground",new(.606f,.091f));Move("Reset placement",new(.802f,.091f));
        for(int sector=0;sector<OsowiecDeploymentSectors.Cells.Length;sector++)
        {
            float centre=.527f+sector*.282f;
            var inset=Box(desk,"Roster inset "+sector,new(centre,.235f),new(508,83),Parchment,false);
            inset.SetSiblingIndex(map.GetSiblingIndex()+1);
            Line(inset,"Rule",new(.5f,.95f),new(493,1),GhostInk);
            Move("Sector title "+sector,new(centre-.074f,.235f));
            var title=(RectTransform)desk.Find("Sector title "+sector);
            title.sizeDelta=new(184,42);
            title.GetComponent<TMP_Text>().text=OsowiecDeploymentSectors.Titles[sector];
            var cells=OsowiecDeploymentSectors.Cells[sector];
            for(int n=0;n<cells.Length;n++)
            {
                int id=sector*cells.Length+n;
                Move("Position "+id,new(centre+.008f+n*.041f,.235f));
                var tile=desk.Find("Position "+id);
                var outline=tile.GetComponent<Outline>();
                if(!outline)outline=tile.gameObject.AddComponent<Outline>();
                outline.effectColor=new Color(.28f,.23f,.14f,.77f);outline.effectDistance=new Vector2(1.6f,-1.6f);
                var anchor=projection.Anchor(grid.GridToWorldPosition(cells[n].x,cells[n].y));
                var pin=Box(map,"Survey cell "+id,anchor,new(8.6f,8.6f),new(.18f,.29f,.20f),true);
                var b=pin.gameObject.AddComponent<Button>();b.targetGraphic=pin.GetComponent<Image>();
                b.navigation=new Navigation{mode=Navigation.Mode.None};
                UnityEventTools.AddIntPersistentListener(b.onClick,roster.UseCell,id);
                var interactions=pin.gameObject.AddComponent<OsowiecPaperSlot>();interactions.board=roster;interactions.index=id;
            }
        }
    }

    [MenuItem("Steel and Ashes/Update field deployment sheet")]
    public static void UpdateDeploymentSheet()
    {
        const string path="Assets/Osowiec/Osowiec.unity";
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path,UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var roots=scene.GetRootGameObjects();
            var grid=System.Array.Find(roots,r=>r.GetComponentInChildren<ChessGrid>(true)).GetComponentInChildren<ChessGrid>(true);
            var roster=System.Array.Find(roots,r=>r.GetComponentInChildren<OsowiecPaperPlacement>(true)).GetComponentInChildren<OsowiecPaperPlacement>(true);
            FitDeploymentSheet((RectTransform)roster.transform.Find("Deployment survey/Survey desk"),roster,grid);
            foreach(var root in roots)foreach(var picture in root.GetComponentsInChildren<RawImage>(true))
            {
                if(picture.name!="fort-ink")continue;
                picture.name="Journal plan";picture.texture=CreateOperationMap().texture;
                picture.rectTransform.sizeDelta=new(464,277);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene))throw new System.InvalidOperationException("Deployment save failed");
        }
        finally{if(opened)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);}
    }
}
