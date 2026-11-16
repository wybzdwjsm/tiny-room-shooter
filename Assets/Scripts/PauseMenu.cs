using UnityEngine;

// Esc 暂停菜单：震屏强度、音量、重开本局、返回标题。
// 只在 Playing 阶段生效——三选一 / 结算 / 标题页按 Esc 不会弹出来抢焦点。
// 由 RunManager 在运行时自动挂载，旧场景不用重建。
[DefaultExecutionOrder(1000)]   // 保证 OnGUI 画在 RunManager 之后，盖住 HUD
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }
    public static bool IsPaused { get { return Instance != null && Instance.paused; } }

    private bool paused;

    private GUIStyle panelStyle, titleStyle, labelStyle, buttonStyle, smallStyle;
    private Texture2D panelTex, dimTex;
    private bool stylesReady;
    private bool guiErrorLogged;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            // 编辑器里 Game 视图失焦时按键收不到，鼠标点面板外也能恢复，
            // 避免出现"暂停了却怎么都点不回去"的情况。
            if (paused && Input.GetMouseButtonDown(0))
            {
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (!PanelRect().Contains(mouse)) Resume();
            }
            return;
        }

        if (paused) { Resume(); return; }
        if (RunManager.HasInstance && RunManager.IsControllable) PauseGame();
    }

    private static Rect PanelRect()
    {
        float w = Mathf.Min(420f, Screen.width - 40f);
        float h = 424f;
        return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
    }

    public void PauseGame()
    {
        if (paused) return;
        paused = true;
        // 不用 Time.timeScale = 0：Unity 会把它当成编辑器暂停，工具栏暂停键亮起。
        // 改为各玩法系统在 Update 里判断 IsPaused 自己停住。
        AudioListener.pause = true;
        Debug.Log("[PauseMenu] 已暂停（Esc / 点面板外 / 点继续 都能恢复）");
    }

    public void Resume()
    {
        paused = false;
        AudioListener.pause = false;
        Debug.Log("[PauseMenu] 已恢复");
    }

    private void OnDisable()
    {
        if (paused) Resume();
    }

    private void OnGUI()
    {
        if (!paused) return;

        try
        {
            DrawPause();
        }
        catch (System.Exception e)
        {
            // 面板画不出来绝不能让"继续"按钮一起消失（那就是永久卡死），
            // 这里退回默认皮肤再画一次最简版：一个能点的继续按钮。
            if (!guiErrorLogged)
            {
                guiErrorLogged = true;
                Debug.LogWarning("[PauseMenu] 暂停面板绘制异常，已退回默认皮肤：" + e.Message);
            }
            stylesReady = false;
            if (GUI.Button(new Rect(20f, 20f, 220f, 44f), "继续游戏")) Resume();
        }
    }

    private void DrawPause()
    {
        EnsureStyles();

        if (dimTex == null || panelStyle == null)
        {
            if (GUI.Button(new Rect(20f, 20f, 220f, 44f), "继续游戏")) Resume();
            return;
        }

        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTex);

        GUILayout.BeginArea(PanelRect(), panelStyle);

        GUILayout.Label("已暂停", titleStyle);
        GUILayout.Label(RunManager.ProgressText, smallStyle);
        GUILayout.Label("按 Esc 继续游戏（点击面板外也可以）", smallStyle);
        GUILayout.Space(14);

        if (GUILayout.Button("继续游戏", buttonStyle, GUILayout.Height(46))) Resume();

        GUILayout.Space(10);
        if (GUILayout.Button("画面震动：" + GameSettings.ShakeNames[GameSettings.ShakeIndex] + "  （点击切换）",
                buttonStyle, GUILayout.Height(38)))
            GameSettings.CycleShake();

        GUILayout.Space(10);
        GUILayout.Label("音量 " + Mathf.RoundToInt(GameSettings.Volume * 100f) + "%", labelStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("－", buttonStyle, GUILayout.Height(34))) GameSettings.ChangeVolume(-0.05f);
        if (GUILayout.Button("＋", buttonStyle, GUILayout.Height(34))) GameSettings.ChangeVolume(0.05f);
        GUILayout.EndHorizontal();

        GUILayout.Space(14);
        if (GUILayout.Button("重开本局（同 seed）", buttonStyle, GUILayout.Height(40)))
        {
            Resume();
            if (RunManager.HasInstance) RunManager.Instance.RestartCurrent();
        }

        GUILayout.Space(8);
        if (GUILayout.Button("返回标题", buttonStyle, GUILayout.Height(40)))
        {
            Resume();
            if (RunManager.HasInstance) RunManager.Instance.ReturnToTitle();
        }

        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (stylesReady) return;

        // 注意：stylesReady 必须最后才置 true。中途抛异常时下一帧还能重建，
        // 否则会留下半套 null 样式，之后每帧在 OnGUI 里刷 NullReferenceException。
        panelTex = MakeTex(new Color(0.09f, 0.10f, 0.14f, 0.98f));
        dimTex = MakeTex(new Color(0.02f, 0.03f, 0.05f, 0.78f));

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTex;
        panelStyle.padding = new RectOffset(24, 24, 18, 18);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 26;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = new Color(0.92f, 0.94f, 1f);

        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.fontSize = 12;
        smallStyle.alignment = TextAnchor.MiddleCenter;
        smallStyle.normal.textColor = new Color(0.66f, 0.72f, 0.82f);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;
        buttonStyle.alignment = TextAnchor.MiddleCenter;

        stylesReady = true;   // 全部建好才置位
    }

    private static Texture2D MakeTex(Color c)
    {
        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void OnDestroy()
    {
        if (paused) { paused = false; AudioListener.pause = false; }
        if (Instance == this) Instance = null;
        if (panelTex != null) Destroy(panelTex);
        if (dimTex != null) Destroy(dimTex);
    }
}
