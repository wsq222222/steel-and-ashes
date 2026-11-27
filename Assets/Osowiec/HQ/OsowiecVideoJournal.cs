using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Option=OsowiecVideoOptions.Option;

public sealed class OsowiecVideoJournal : MonoBehaviour
{
    readonly Color ink=new(.17f,.125f,.075f), rust=new(.39f,.14f,.08f), line=new(.39f,.29f,.18f,.37f),
        tint=new(.51f,.38f,.20f,.15f);
    OsowiecVideoOptions video;
    RectTransform sheet,content;
    TMP_Text timer;
    TMP_FontAsset body,title;
    int section;
    readonly List<Action> updates=new();
    public bool Visible=>sheet && sheet.gameObject.activeInHierarchy;
    public void Open(OsowiecVideoOptions owner,Transform page)
    {
        video=owner;
        if(!sheet)
        {
            var old=page.GetComponentsInChildren<TMP_Text>(true);
            title=old.First(t=>t.name=="Chapter title").font;
            body=old.FirstOrDefault(t=>t.name!="Chapter title")?.font ?? title;
            foreach(Transform child in page)child.gameObject.SetActive(false);
            sheet=Rect(page,"Video settings",0,0,524,650);
            Text(sheet,"Video heading","Изображение",10,5,505,63,42,title,ink);
            Graphic(sheet,"Under heading",10,70,495,2,rust);
            Draw(0);
        }
        else Draw(section);
    }

    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);
        r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
    }
    static Image Graphic(Transform p,string n,float x,float y,float w,float h,Color color)
    {
        var im=Rect(p,n,x,y,w,h).gameObject.AddComponent<Image>();im.color=color;im.raycastTarget=false;return im;
    }
    static TMP_Text Text(Transform p,string name,string value,float x,float y,float w,float h,int size,TMP_FontAsset font,Color col,
        TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
    {
        var t=Rect(p,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
        t.font=font;t.text=OsowiecMenuLanguage.Text(value);t.fontSize=size;t.alignment=align;t.color=col;t.raycastTarget=false;
        t.textWrappingMode=TextWrappingModes.NoWrap;return t;
    }
    Button Button(Transform p,string name,string label,float x,float y,float w,float h,Action clicked,bool chosen=false)
    {
        var im=Graphic(p,name,x,y,w,h,chosen?rust:tint);im.raycastTarget=true;
        var button=im.gameObject.AddComponent<Button>();button.targetGraphic=im;
        Text(im.transform,"Label",label,0,0,w,h,22,body,chosen?new Color(.98f,.93f,.79f):ink,TextAlignmentOptions.Center);
        button.onClick.AddListener(()=>clicked());
        var colors=button.colors;colors.highlightedColor=new Color(1,.91f,.70f);
        colors.pressedColor=new Color(.82f,.68f,.46f);colors.disabledColor=new Color(.8f,.75f,.65f,.36f);button.colors=colors;
        return button;
    }
    void Draw(int selected,bool keepDisplay=false)
    {
        if(section!=selected && video.Confirming && !keepDisplay)video.RevertDisplay();
        section=selected;updates.Clear();timer=null;
        if(content){content.gameObject.SetActive(false);Destroy(content.gameObject);}
        content=Rect(sheet,"Current leaf",0,0,524,650);
        string[] names={"Экран","Детализация","Камера"};
        for(int i=0;i<3;i++){int tab=i;Button(content,"Leaf "+i,names[i],10+i*167,85,160,41,()=>Draw(tab),i==section);}
        int row=0;
        void Value(string id,string label,Func<string> get,Action<int> change,Func<int,bool> can=null)
        {
            int y=140+row++*65;
            Text(content,id+" title",label,10,y,253,54,22,body,ink);
            var output=Text(content,id+" value","",289,y,167,54,22,body,ink,TextAlignmentOptions.Center);
            var prev=Button(content,id+" prev","−",252,y+6,38,42,()=>{change(-1);Refresh();});
            var next=Button(content,id+" next","+",464,y+6,38,42,()=>{change(1);Refresh();});
            Graphic(content,id+" rule",10,y+55,493,1,line);
            updates.Add(()=>{output.text=OsowiecMenuLanguage.Text(get());prev.interactable=can?.Invoke(-1)??true;next.interactable=can?.Invoke(1)??true;});
        }
        void Setting(Option key,string label,Func<int,string> format)
        { Value(key.ToString(),label,()=>format(video.Get(key)),s=>video.Step(key,s),s=>video.CanStep(key,s)); }
        string OnOff(int val)=>val==0?"Выкл.":"Вкл.";
        if(section==0)
        {
            bool Editable()=>!OsowiecVideoOptions.DisplayOverridden && !video.Confirming;
            Value("Mode","Режим окна",()=>video.Pending.mode switch{FullScreenMode.Windowed=>"В окне",FullScreenMode.ExclusiveFullScreen=>"Полный экран",_=>"Без рамки"},
                s=>video.StepDisplay(true,s),s=>Editable() && Array.IndexOf(OsowiecVideoOptions.Modes,video.Pending.mode)+s>=0 &&
                Array.IndexOf(OsowiecVideoOptions.Modes,video.Pending.mode)+s<OsowiecVideoOptions.Modes.Length);
            Value("Resolution","Разрешение",()=>$"{video.Pending.width} × {video.Pending.height}",s=>video.StepDisplay(false,s),
                s=>{int i=video.Sizes.IndexOf(new(video.Pending.width,video.Pending.height))+s;return Editable() && i>=0 && i<video.Sizes.Count;});
            Setting(Option.VSync,"Вертик. синхронизация",OnOff);
            Setting(Option.Cap,"Частота кадров",v=>video.Get(Option.VSync)!=0?"По монитору":v<0?"Без лимита":v+" FPS");
            var apply=Button(content,"Apply display","Применить режим",10,425,267,46,()=>{video.PreviewDisplay();Refresh();});
            var keep=Button(content,"Keep display","Оставить",10,425,215,46,()=>{video.ConfirmDisplay();Refresh();},true);
            var undo=Button(content,"Revert display","Вернуть",239,425,196,46,()=>{video.RevertDisplay();Refresh();});
            timer=Text(content,"Display hint","",10,477,491,55,19,body,rust);
            updates.Add(()=>{
                bool verify=video.Confirming;
                keep.gameObject.SetActive(verify);undo.gameObject.SetActive(verify);apply.gameObject.SetActive(!verify);
                apply.interactable=Editable() && !video.Pending.Same(OsowiecVideoOptions.DisplayChoice.Actual());
                if(!verify)timer.text=OsowiecMenuLanguage.Text(OsowiecVideoOptions.DisplayOverridden?"Размер окна задан при запуске.":
                    !video.Pending.Same(OsowiecVideoOptions.DisplayChoice.Actual())?"Новый режим потребует подтверждения.":"");
            });
        }
        else if(section==1)
        {
            Setting(Option.RenderScale,"Масштаб рендера",v=>v+"%");
            Setting(Option.Relief,"Рельеф вблизи",OnOff);
            Setting(Option.ShadowMap,"Тени солнца",v=>v.ToString());
            Setting(Option.ShadowDistance,"Дальность теней",v=>v==0?"Выкл.":OsowiecMenuLanguage.Format("{0} м",v));
            Setting(Option.AntiAliasing,"Сглаживание",v=>v switch{0=>"Выкл.",1=>"FXAA",_=>"SMAA"});
            Setting(Option.Textures,"Текстуры",v=>v switch{0=>"Полные",1=>"½ размера",_=>"¼ размера"});
        }
        else
        {
            var controls=video.controls;
            Value("Fov","Поле зрения",()=>$"{controls.FovControl.value:0}°",s=>controls.FovControl.value+=s*5,
                s=>s<0?controls.FovControl.value>50:controls.FovControl.value<95);
            Setting(Option.Exposure,"Яркость",v=>$"{100+v*10}%");
            Value("Interface","Размер интерфейса",()=>$"{controls.ScaleControl.value:P0}",s=>controls.ScaleControl.value+=s*.03f,
                s=>s<0?controls.ScaleControl.value>controls.ScaleControl.minValue:controls.ScaleControl.value<controls.ScaleControl.maxValue);
            Value("Effects","Эффекты погоды",()=>controls.EffectsControl.isOn?"Облегчённые":"Полные",
                _=>controls.EffectsControl.isOn=!controls.EffectsControl.isOn);
            Value("Shake","Тряска камеры",()=>controls.ShakeControl.isOn?"Слабая":"Обычная",
                _=>controls.ShakeControl.isOn=!controls.ShakeControl.isOn);
        }
        Button(content,"Reset video","По умолчанию",10,580,226,43,()=>{video.ResetAll();if(video.Confirming)Draw(0,true);else Refresh();});
        Text(content,"Leaf number",$"{section+1} / 3",427,580,75,43,20,body,rust,TextAlignmentOptions.MidlineRight);
        Refresh();
    }
    void OnEnable()=>OsowiecMenuLanguage.Changed+=LocaleChanged;
    void OnDisable()=>OsowiecMenuLanguage.Changed-=LocaleChanged;
    void LocaleChanged(){if(!sheet)return;sheet.Find("Video heading").GetComponent<TMP_Text>().text=OsowiecMenuLanguage.Text("Изображение");Draw(section,true);}
    public void Refresh(){foreach(var update in updates)update();}
    void Update(){if(Visible && timer && video.Confirming)timer.text=OsowiecMenuLanguage.Format("Возврат через {0} с",Mathf.CeilToInt(video.ConfirmUntil-Time.realtimeSinceStartup));}
}
