using UnityEngine;

public sealed class OsowiecReceivingPanel : MonoBehaviour
{
    [SerializeField] OsowiecSupplyParcel parcel;
    GUIStyle heading, bodyText, button;

    static GUIStyle TextStyle(int size, Color color, FontStyle weight)
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight };
        style.normal.textColor = color;
        return style;
    }

    void OnGUI()
    {
        if (heading == null)
        {
            heading = TextStyle(23, new Color(1f, .91f, .73f), FontStyle.Bold);
            bodyText = TextStyle(16, new Color(.94f, .9f, .83f), FontStyle.Normal);
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }
        var key = Event.current;
        if (key.type == EventType.KeyDown)
        {
            if (key.keyCode == KeyCode.Space) { parcel.DropParcel(); key.Use(); }
            else if (key.keyCode == KeyCode.R) { parcel.ResetParcel(); key.Use(); }
        }

        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        bool wasEnabled = GUI.enabled;
        float scale = Mathf.Min(Screen.width / 720f, Mathf.Clamp(Screen.height / 710f, .8f, 1.55f));
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float x = 20f;
        float y = Screen.height / scale - 184f;
        GUI.color = new Color(.06f, .073f, .065f, .94f);
        GUI.DrawTexture(new Rect(x, y, 533, 164), Texture2D.whiteTexture);
        GUI.color = new Color(.72f, .51f, .25f);
        GUI.DrawTexture(new Rect(x, y, 533, 3), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x+15, y+9, 505, 32), "Полевой склад · Приёмка груза", heading);
        string position = parcel.InZone ? "в зоне" : "вне зоны";
        GUI.Label(new Rect(x+15, y+47, 505, 24),
            $"Датчик: {position}     Входы: {parcel.ZoneEnters}     Выходы: {parcel.ZoneExits}", bodyText);
        GUI.Label(new Rect(x+15, y+72, 505, 24),
            $"Удары о лоток: {parcel.TrayContacts}     Последний: {parcel.LastImpactSpeed:0.0} м/с", bodyText);
        GUI.enabled = wasEnabled && parcel.Ready;
        if (GUI.Button(new Rect(x+15, y+111, 245, 37), "Отпустить груз  [Пробел]", button)) parcel.DropParcel();
        GUI.enabled = wasEnabled && parcel.CanLift;
        if (GUI.Button(new Rect(x+271, y+111, 247, 37), "Поднять на блок  [R]", button)) parcel.ResetParcel();
        GUI.enabled = wasEnabled;
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }
}
