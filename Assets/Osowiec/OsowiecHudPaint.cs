using UnityEngine;

// Contextual world labels follow the same field-document palette as the uGUI screens.
public static class OsowiecHudPaint
{
    static readonly Color Ink = new(.075f, .087f, .069f, .94f);
    static readonly Color Brass = new(.74f, .60f, .34f);
    static readonly Color Ivory = new(.93f, .89f, .77f);
    static Font body;
    static Font display;
    public static Font Body => body ? body : body = Resources.Load<Font>("OsowiecFonts/FiraSansCondensed-Medium");
    public static Font Heading => display ? display : display = Resources.Load<Font>("OsowiecFonts/Spectral-SemiBold");
    static GUIStyle label;
    static GUIStyle action;
    static GUIStyle fieldText;

    public static float Scale => Mathf.Clamp(Screen.height / 1080f, .65f, 2.2f) * OsowiecFieldSettings.InterfaceScale;

    static string caption;
    static float captionUntil;

    public static void Caption(string value)
    {
        if (!OsowiecFieldSettings.CaptionsEnabled) return;
        caption = value;
        captionUntil = Time.time + 2.2f;
    }

    public static void DrawCaption()
    {
        if (!OsowiecFieldSettings.CaptionsEnabled || Time.time >= captionUntil || string.IsNullOrEmpty(caption)) return;
        label ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
        label.font = Body;
        label.fontSize = Mathf.RoundToInt(21 * Scale);
        var content=new GUIContent(caption);
        float inset=22*Scale;
        float width=Mathf.Min(label.CalcSize(content).x+inset,Screen.width*.55f);
        float height=Mathf.Max(36*Scale,label.CalcHeight(content,width-inset)+6*Scale);
        Plate(new Rect((Screen.width-width)*.5f,Screen.height-57*Scale-height,width,height),caption,true);
    }

    public static void Plate(Rect rect, string text, bool centered = false)
    {
        Fill(rect, Ink);
        Fill(new Rect(rect.x, rect.y, rect.width, 1.5f * Scale), Brass);
        label ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
        label.font = Body;
        label.fontSize = Mathf.RoundToInt(21 * Scale);
        label.normal.textColor = Ivory;
        label.alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
        float inset = 11 * Scale;
        GUI.Label(new Rect(rect.x + inset, rect.y, rect.width - inset * 2, rect.height), text, label);
    }

    public static void Panel(Rect rect)
    {
        Fill(rect, Ink);
        Fill(new Rect(rect.x, rect.y, rect.width, Scale), new Color(.43f, .37f, .25f));
        Fill(new Rect(rect.x, rect.y, 36 * Scale, 2 * Scale), Brass);
    }

    public static void Text(Rect rect, string value, int size, Color color, bool bold = false)
    {
        fieldText ??= new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip
        };
        fieldText.font = bold && size >= 23 ? Heading : Body;
        fieldText.fontSize = Mathf.RoundToInt(size * Scale);
        fieldText.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        fieldText.normal.textColor = color;
        GUI.Label(rect, value, fieldText);
    }

    public static void Meter(Rect rect, float fraction, Color color)
    {
        Fill(rect, new Color(.20f, .22f, .17f));
        Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
    }

    public static bool OrderButton(Rect area, string label)
    {
        float s=Scale;
        bool hover=area.Contains(Event.current.mousePosition);
        Fill(area,hover ? new Color(.61f,.29f,.18f):new Color(.39f,.17f,.11f));
        Text(new Rect(area.x+16*s,area.y,area.width-101*s,area.height),label,20,Ivory);
        var key=new Rect(area.xMax-71*s,area.y+8*s,58*s,area.height-16*s);
        Fill(key,new Color(.85f,.76f,.56f));
        Text(new Rect(key.x+8*s,key.y,key.width-8*s,key.height),"Enter",17,Ink);
        return GUI.Button(area,GUIContent.none,GUIStyle.none);
    }

    public static bool Button(Rect rect, string text)
    {
        Fill(rect, rect.Contains(Event.current.mousePosition) ? new Color(.85f, .73f, .48f) : Brass);
        action ??= new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter };
        action.font = Body;
        action.fontSize = Mathf.RoundToInt(20 * Scale);
        action.fontStyle = FontStyle.Bold;
        action.normal.textColor = action.hover.textColor = action.active.textColor = Ink;
        return GUI.Button(rect, text, action);
    }

    public static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
