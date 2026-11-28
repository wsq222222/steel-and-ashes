using TMPro;
using static OsowiecMenuLanguage;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class OsowiecPaperPlacement : MonoBehaviour
{
    [SerializeField] ArmyDeploymentController deployment;
    [SerializeField] GameObject sheet;
    [SerializeField] GameObject survey;
    [SerializeField] TMP_Text surveyTitle, requirement;
    [SerializeField] Button confirm, fieldConfirm;
    [SerializeField] TMP_Text budgetLabel, detail, feedback;
    [SerializeField] Image[] rows, tokens;
    [SerializeField] RawImage[] portraits;
    [SerializeField] TMP_Text[] freeLabels;
    [SerializeField] RawImage dragToken;
    [SerializeField] int budget;
    int selected, focusedCell=-1;
    internal int SelectedOffer=>selected;
    int hoveredOffer=-1, focusedOffer=-1;
    bool dragging;
    int lastSupply=-1;

    static readonly Color Paper=new(.83f,.72f,.51f);
    static readonly Color Blank=new(.70f,.60f,.43f,.38f);

    public void Bind(ArmyDeploymentController army,GameObject root,Button go,Button original,int total,
        TMP_Text counter,TMP_Text description,TMP_Text note,Image[] cards,Image[] cells,RawImage[] faces,
        TMP_Text[] blank,RawImage drag,GameObject terrainView,TMP_Text location,TMP_Text startHint)
    {
        deployment=army; sheet=root; confirm=go; fieldConfirm=original; budget=total;
        budgetLabel=counter; detail=description; feedback=note; rows=cards; tokens=cells; portraits=faces;
        freeLabels=blank; dragToken=drag;
        survey=terrainView; surveyTitle=location; requirement=startHint;
    }

    void Start(){
        if(!sheet)return;
        // Invisible button backgrounds still receive remove/replace clicks on the full cell.
        foreach(var token in tokens) token.canvasRenderer.cullTransparentMesh=false;
        SelectOffer(0);
        rows[0].GetComponent<Button>().Select();
    }
    void OnEnable()=>OsowiecMenuLanguage.Changed+=RefreshLanguage;
    void RefreshLanguage()
    {
        if(!sheet || !deployment || !deployment.Sectors)return;
        DescribeOffer(selected);
        surveyTitle.text=Text(OsowiecDeploymentSectors.Titles[deployment.Sectors.Sector]);
        feedback.text=Text("Выберите бойца и укажите место на карте.");
        Refresh();
    }
    void OnDisable(){ OsowiecMenuLanguage.Changed-=RefreshLanguage; dragging=false; if(dragToken) dragToken.gameObject.SetActive(false); }
    public void Show(){
        if(survey)survey.SetActive(false);
        sheet.SetActive(true);
        Refresh();
        rows[selected].GetComponent<Button>().Select();
    }
    public void Hide(){
        EndDrag();
        if(!survey)return;
        sheet.SetActive(false);
        survey.SetActive(true);
        deployment.Sectors.ShowPositions();
        surveyTitle.text=Text(OsowiecDeploymentSectors.Titles[deployment.Sectors.Sector]);
        survey.GetComponentInChildren<Button>().Select();
    }
    public void NextSector(){
        deployment.Sectors.Switch();
        surveyTitle.text=Text(OsowiecDeploymentSectors.Titles[deployment.Sectors.Sector]);
    }
    public void Headquarters(){
        Time.timeScale=1f;
        SceneManager.LoadScene("OsowiecHQ");
    }
    public void Confirm(){
        if(fieldConfirm && fieldConfirm.interactable){fieldConfirm.onClick.Invoke();return;}
        feedback.text=Text("Сначала выберите бойца и укажите свободную позицию на карте.");
    }

    static void StepFocus(GameObject panel,bool backwards){
        var buttons=System.Array.FindAll(panel.GetComponentsInChildren<Button>(),b=>b.navigation.mode!=Navigation.Mode.None);
        if(buttons.Length==0 || !EventSystem.current)return;
        var current=EventSystem.current.currentSelectedGameObject;
        int index=System.Array.FindIndex(buttons,b=>b.gameObject==current);
        buttons[(index+(backwards?-1:1)+buttons.Length)%buttons.Length].Select();
    }

    public void PreviewOffer(int index,bool pointer){
        if(pointer)hoveredOffer=index;else focusedOffer=index;
        DescribeOffer(hoveredOffer>=0?hoveredOffer:focusedOffer>=0?focusedOffer:selected);
    }
    public void FocusCell(int index){
        focusedCell=index;
        var unit=deployment.DeployedAt(Cell(index));
        feedback.text=unit?Format("{0} · правая кнопка или Delete, чтобы снять бойца.",Text(unit.GetUnitDisplayName())):
            Text("Свободное место. Щёлкните или нажмите Enter, чтобы поставить выбранного бойца.");
    }

    public void SelectOffer(int offer)
    {
        selected=offer;
        DescribeOffer(offer);
        Refresh();
    }
    void DescribeOffer(int offer)
    {
        if(deployment.TryGetOffer(offer,out var entry))
        {
            var unit=entry.unitPrefab.GetComponent<Unit>();
            var kit=unit.GetComponent<OsowiecCastMotion>();
            string ability=kit?kit.PerkDescription(unit.chessType):"";
            string role=unit.chessType switch {ChessUnitType.Молот=>"Штурмовик",ChessUnitType.Циклоп=>"Наблюдатель",_=>"Разведчик"};
            detail.text=Format("{0} · {1} · {2:0} м\n{3}",Text(unit.GetUnitDisplayName()),Text(role),OsowiecPlanner.MoveBudget(unit),Text(ability));
        }
    }
    public void BeginDrag(int offer,PointerEventData data)
    {
        SelectOffer(offer); dragging=true; dragToken.uvRect=Portrait(offer);
        dragToken.gameObject.SetActive(true); Drag(data);
    }
    public void Drag(PointerEventData data)
    {
        if(!dragging) return;
        var parent=(RectTransform)dragToken.transform.parent;
        if(RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,data.position,null,out var local))
            dragToken.rectTransform.anchoredPosition=local+new Vector2(23,-23);
    }
    public void EndDrag(){dragging=false; if(dragToken) dragToken.gameObject.SetActive(false);}

    static Vector2Int Cell(int index)
    {
        int per=OsowiecDeploymentSectors.Cells[0].Length;
        return OsowiecDeploymentSectors.Cells[index/per][index%per];
    }
    static Rect Portrait(int offer)
    {
        return new Rect(offer/3f,0,1f/3f,1);
    }

    public void UseCell(int index)
    {
        focusedCell=index;
        var cell=Cell(index);
        bool replace=Keyboard.current?.shiftKey.isPressed==true;
        string issue=deployment.PlacementIssue(selected,cell,replace);
        if(issue.Length>0){feedback.text=Text(issue); return;}
        if(deployment.TryPlaceOfferAtGridCell(selected,cell,replace))
            feedback.text=Format("Позиция занята   ·   {0}",Text(OsowiecDeploymentSectors.Titles[index/OsowiecDeploymentSectors.Cells[0].Length]));
        Refresh();
    }
    public void Remove(int index)
    {
        focusedCell=index;
        var occupant=deployment.DeployedAt(Cell(index));
        if(occupant){deployment.RemoveDeployedUnit(occupant); feedback.text=Text("Боец возвращён в резерв");}
        Refresh();
    }
    public void ResetRoster()
    {
        for(int i=0;i<tokens.Length;i++){
            var occupant=deployment.DeployedAt(Cell(i));
            if(occupant) deployment.RemoveDeployedUnit(occupant);
        }
        feedback.text=Text("Расстановка очищена"); Refresh();
    }
    void Update()
    {
        if(!GameManager.Instance || !GameManager.Instance.IsArmyDeploymentPhase() || GameManager.Instance.IsPaused())return;
        var keys=Keyboard.current;
        if(survey && survey.activeInHierarchy){
            if(keys!=null){
                if(keys.escapeKey.wasPressedThisFrame){Show();return;}
                if(keys.qKey.wasPressedThisFrame)NextSector();
                if(keys.tabKey.wasPressedThisFrame)StepFocus(survey,keys.shiftKey.isPressed);
            }
            Vector2 pan=Vector2.zero;
            if(keys!=null){
                pan.x=(keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0);
                pan.y=(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0);
            }
            float wheel=Mouse.current?.scroll.ReadValue().y??0;
            if(pan!=Vector2.zero || wheel!=0)CameraManager.Instance?.PanDeploymentSurvey(pan.normalized,System.Math.Sign(wheel));
            return;
        }
        if(!sheet.activeInHierarchy)return;
        if(lastSupply!=deployment.GetPointsRemaining()) Refresh();
        if(keys==null)return;
        if(keys.tabKey.wasPressedThisFrame)StepFocus(sheet,keys.shiftKey.isPressed);
        if(keys.digit1Key.wasPressedThisFrame)SelectOffer(0);
        if(keys.digit2Key.wasPressedThisFrame)SelectOffer(1);
        if(keys.digit3Key.wasPressedThisFrame)SelectOffer(2);
        if(keys.deleteKey.wasPressedThisFrame && focusedCell>=0)Remove(focusedCell);
    }
    void Refresh()
    {
        if(!deployment || !GameManager.Instance || !GameManager.Instance.IsArmyDeploymentPhase())return;
        int count=0;
        for(int i=0;i<tokens.Length;i++)
        {
            var unit=deployment.DeployedAt(Cell(i));
            var image=portraits[i]; image.enabled=unit!=null; freeLabels[i].enabled=unit==null;
            tokens[i].color=unit?Color.clear:Color.white;
            var outline=tokens[i].GetComponent<Outline>();
            if(outline)outline.enabled=!unit;
            if(!unit)continue;
            count++;
            int column=unit.chessType switch{ChessUnitType.Молот=>0,ChessUnitType.Циклоп=>1,_=>2};
            image.uvRect=Portrait(column);
        }
        for(int i=0;i<rows.Length;i++)rows[i].color=i==selected?new Color(.69f,.60f,.44f):Blank;
        budgetLabel.text=Format("Снабжение  {0} / {1}     Бойцов  {2}",deployment.GetPointsRemaining(),budget,count);
        lastSupply=deployment.GetPointsRemaining();
        if(requirement)requirement.text=count==0?Text("Для начала поставьте хотя бы одного бойца."):"";
        confirm.targetGraphic.color=count==0?new Color(.37f,.31f,.24f):new Color(.39f,.13f,.09f);
    }
}

