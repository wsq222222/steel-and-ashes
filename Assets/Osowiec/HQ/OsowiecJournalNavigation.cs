using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class OsowiecJournalNavigation : MonoBehaviour
{
    [SerializeField] private GameObject[] chapters;
    [SerializeField] private bool welcome;
    [SerializeField] GameObject confirmExit, home;
    static OsowiecJournalNavigation current;
    static bool entered;
    void OnEnable()=>current=this;
    void OnDisable(){if(current==this)current=null;}
    public void RequestExit(){home.SetActive(false);confirmExit.SetActive(true);}
    public void CancelExit(){confirmExit.SetActive(false);home.SetActive(true);}
    public static bool ClosePage()
    {
        if(!current || !GameManager.Instance || !GameManager.Instance.IsPaused())return false;
        if(current.confirmExit && current.confirmExit.activeInHierarchy){current.CancelExit();return true;}
        var journal=current.GetComponent<OsowiecObjectiveJournal>();
        if(journal && journal.IsOpen){journal.Close();return true;}
        if(current.chapters!=null && current.chapters.Length>0 && current.chapters[0].transform.parent.gameObject.activeInHierarchy)
        {
            GameManager.Instance.ApplySettings();return true;
        }
        return false;
    }
    public void Bind(GameObject[] pages,bool firstEntry,GameObject confirmation,GameObject navigation)
    {chapters=pages;welcome=firstEntry;confirmExit=confirmation;home=navigation;}
    IEnumerator Start()
    {
        Audio();
        if(welcome && !entered)
        {
            entered=true;
            yield return null;
            GameManager.Instance?.PauseGame();
        }
    }
    public void Audio()=>Tab(0);
    public void Picture()
    {
        Tab(1);
        if(chapters!=null && chapters.Length>1)
            GetComponent<OsowiecVideoOptions>()?.Show(chapters[1].transform);
    }
    public void Controls()=>Tab(2);
    void Tab(int selected)
    {
        if(chapters==null)return;
        for(int i=0;i<chapters.Length;i++)if(chapters[i])chapters[i].SetActive(i==selected);
    }
    void Update()
    {
        if(welcome && home && home.activeInHierarchy && GameManager.Instance && GameManager.Instance.IsPaused() &&
            Keyboard.current?.enterKey.wasPressedThisFrame==true &&
            (chapters==null || chapters.Length==0 || !chapters[0].transform.parent.gameObject.activeInHierarchy))
            GameManager.Instance.ResumeGame();
    }
}
