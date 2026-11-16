using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 一局生命周期：标题 -> 3 波敌人 -> 每波清场三选一 -> 结算(胜/败) -> 重开。
// 所有随机（平台布局、敌人位置、敌人类型、词条抽取）都走同一个 System.Random(seed)，
// 绝不混用 UnityEngine.Random，这样同 seed 能完整复现。
//
// UI 布局约定（避免任何重叠）：
//   顶部居中 = HUD 条；屏幕正中 = 标题 / 三选一 / 结算卡片；
//   底部居中 = 一行操作提示；左侧中部 = F1 调参面板（模态界面出现时自动隐藏）。
public class RunManager : MonoBehaviour
{
    // 版本标记：标题页右下角和启动日志都会打印，用来确认"跑的是不是最新代码"
    public const string BuildTag = "v1.0";

    public enum Phase { Title, Playing, Choosing, Result }

    // 两种玩法：标准（3 波 + Boss，有明确终点）与无尽（一波接一波，每 5 波一个 Boss）
    public enum Mode { Standard, Endless }

    public static RunManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;
    public static bool IsPlaying => Instance != null && Instance.phase == Phase.Playing;
    public static bool IsModal => Instance != null && Instance.phase != Phase.Playing;
    // 玩家能否操控：战斗中和三选一时都算。
    // v0.14 起三选一不再冻结玩家 —— 以前只要选卡 UI 出问题，玩家就被锁死在原地，
    // 看上去和"游戏暂停"一模一样。现在选卡时你依然能走动，界面崩了也不会被卡住。
    public static bool IsControllable => Instance != null &&
        (Instance.phase == Phase.Playing || Instance.phase == Phase.Choosing);
    // 敌人进场期间冻结游戏内系统（玩家 / 敌人行为 / 子弹）。
    // 注意：不要用 Time.timeScale = 0 —— Unity 会把它当成编辑器暂停，工具栏亮起暂停键且 Game 视图失焦。
    public static bool IsSpawning => Instance != null && Instance.spawning;
    public static RunStats Current => Instance != null ? Instance.stats : RunStats.Base();

    // 房间左右边界（墙的内侧面）。Boss 的移动范围、以后加相机跟随都用它们，
    // 不要再各处写死 ±7.2 这种数字——边界是随相机会变的。
    public static float RoomLeft { get { return Instance != null ? Instance.boundLeft : -10.2f; } }
    public static float RoomRight { get { return Instance != null ? Instance.boundRight : 10.2f; } }

    // 角色整体缩放：玩家原本是 1×1 的方块，占画面高度约 1/12，站在场上很"占地方"。
    // 缩到 0.62 之后相对体积降到约 1/19，同一块场地里可用于走位、跳跃、绕后的空间明显变多，
    // 平台和敌人之间的空隙也更好读。敌人用同一系数，保持体型关系不变。
    private const float CharacterScale = 0.62f;

    [SerializeField] private int roomCount = 3;
    [SerializeField] private int seedOverride = 0;   // 填非 0 可固定 seed 复现

    // 难度曲线：每波的敌人数量、基础血量、类型权重（普通 / 快速 / 冲锋 / 炮台，剩余给精英）
    // 第 1 波只出普通怪练手，第 2 波引入快速怪与冲锋怪，第 3 波才有炮台和精英。
    // 这张表只服务标准模式；无尽模式用下面 WaveSpecFor 里的公式，避免表越写越长。
    private static readonly int[] WaveCount = { 2, 3, 4 };
    private static readonly int[] WaveHp = { 2, 3, 4 };
    private static readonly int[] WaveNormal = { 100, 45, 30 };
    private static readonly int[] WaveFast = { 0, 30, 20 };
    private static readonly int[] WaveCharger = { 0, 25, 20 };
    private static readonly int[] WaveTurret = { 0, 0, 15 };

    // 无尽模式：每 5 波一个 Boss（第 5、10、15… 波）
    private const int BossEvery = 5;

    // 敌人入场：每只随机飞 1.1~1.7 秒，彼此错开 0.12 秒逐个进场
    private const float IntroMin = 1.1f;
    private const float IntroMax = 1.7f;
    private const float IntroDelayStep = 0.12f;

    private RunStats stats;
    private System.Random rng;
    private int seed;
    private int roomIndex;
    private int killCount;
    private float runStart;
    private Phase phase = Phase.Title;
    private Mode mode = Mode.Standard;
    private bool victory;

    // 是否 Boss 波：标准模式最后一波，无尽模式每 BossEvery 波
    private bool IsBossWave
    {
        get
        {
            return mode == Mode.Standard
                ? roomIndex >= roomCount - 1
                : (roomIndex + 1) % BossEvery == 0;
        }
    }

    // 第 index 波（从 0 开始）的敌人配置。标准模式查表，无尽模式按公式递增。
    private void WaveSpecFor(int index, out int count, out int hp,
        out int wNormal, out int wFast, out int wCharger, out int wTurret)
    {
        if (mode == Mode.Standard)
        {
            int i = Mathf.Clamp(index, 0, WaveCount.Length - 1);
            count = WaveCount[i];
            hp = WaveHp[i];
            wNormal = WaveNormal[i];
            wFast = WaveFast[i];
            wCharger = WaveCharger[i];
            wTurret = WaveTurret[i];
            return;
        }

        // 无尽：数量每波 +0.5（封顶 8 只），血量每两波 +1（封顶 14），
        // 权重逐渐把"只会站着射"的普通怪挤掉，换成冲锋、炮台、快速。
        // 封顶是必要的：不封顶的话第 20 波会变成 12 只 20 血的怪，那是卡死而不是难。
        count = Mathf.Clamp(2 + index / 2, 2, 8);
        hp = Mathf.Clamp(2 + index / 2, 2, 14);

        wTurret = index >= 3 ? Mathf.Min(22, 8 + index * 2) : 0;
        wCharger = index >= 1 ? Mathf.Min(30, 15 + index * 2) : 0;
        wFast = Mathf.Min(30, 14 + index * 3);
        wNormal = Mathf.Max(15, 100 - wFast - wCharger - wTurret);
    }

    // 结算页用的统计：命中率与受伤归因
    private int shotsFired;
    private int shotsHit;
    private int damageTaken;
    private string lastDamageSource = "无";
    private bool spawning;
    private float spawnTimer;
    private bool beatRecordShown;      // 无尽模式里"超过最远记录"只提示一次

    private readonly List<Enemy> liveEnemies = new List<Enemy>();
    private readonly List<GameObject> randomPlatforms = new List<GameObject>();   // 地板 + 边界
    private readonly List<GameObject> floatPlatforms = new List<GameObject>();    // 每关重生成的悬浮平台
    private readonly List<GameObject> builtinPlatforms = new List<GameObject>();
    private readonly List<Modifier> picked = new List<Modifier>();
    private readonly Dictionary<string, int> stacks = new Dictionary<string, int>();
    private List<Modifier> choices;
    private Sprite enemySprite;

    private float bannerTimer;
    private string bannerText;
    private bool briefOpen;                          // 标题页的"序章"覆盖层
    private Mode pendingMode = Mode.Standard;
    private float flashTimer;      // 每波开场的闪白，给"新一波"一个明确的起始帧
    private float hurtFlash;       // 受伤时屏幕边缘的红闪（0~1，随时间衰减）

    private GUIStyle panelStyle, titleStyle, labelStyle, hintStyle, buttonStyle, bannerStyle, crosshairStyle;
    private GUIStyle choiceTitleStyle, cardStyle;
    private Texture2D panelTex, dimTex, cardTex, cardHoverTex, barBgTex, barFillTex, flashTex;
    private bool stylesReady;
    private bool styleErrorLogged;
    private bool guiErrorLogged;
    private bool lastSkinOk = true;      // 上一帧 OnGUI 是否拿到了有效的 GUI.skin
    private float choosingSince;
    private bool timeScaleWarned;
    private float spawnElapsed;
    private Phase lastLoggedPhase = Phase.Title;

    // 选卡最多等这么久，超时自动拿第一项继续（防止任何 UI 故障把玩家锁死）
    private const float AutoPickSeconds = 12f;
    // 进场冻结的绝对上限（正常 1.1~1.7 秒，这里只是防异常时永久冻结）
    private const float SpawnHardCap = 4f;

    private void Awake()
    {
        Instance = this;
        enemySprite = MakeSprite();
        stats = RunStats.Base();
        GameAudio.Ensure();          // 提前建好，背景音乐从标题页就开始放
    }

    private void Start()
    {
        // 运行时自动挂载，旧场景无需重建
        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            // 缩小角色本体：碰撞体、精灵都在同一个 transform 上，一起缩放；
            // 落地检测点挂在 Player 子物体上，localPosition 是局部坐标，也会跟着缩，不用另外改。
            player.transform.localScale = Vector3.one * CharacterScale;
            if (player.GetComponent<PlayerHealth>() == null)
                player.AddComponent<PlayerHealth>();
            PlayerVisual.Attach(player);      // 描边 + 眼睛：从一片纯色方块里一眼认出自己
        }
        if (GetComponent<PauseMenu>() == null)
            gameObject.AddComponent<PauseMenu>();

        // 地板压到屏幕底部后，相机重心也下移一点，让房间在画面里居中
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, -0.8f, cam.transform.position.z);
            Backdrop.Ensure(cam);            // 程序生成的渐变背景，垫在所有对象后面
        }

        phase = Phase.Title;
        bannerText = "";
        Debug.Log("[RunManager] " + BuildTag + " 启动（全项目 timeScale 恒为 1，暂停靠系统门控）");
    }

    private void Update()
    {
        if (bannerTimer > 0f) bannerTimer -= Time.unscaledDeltaTime;
        if (flashTimer > 0f) flashTimer -= Time.unscaledDeltaTime;
        if (hurtFlash > 0f) hurtFlash -= Time.unscaledDeltaTime * 2.2f;   // 约 0.45 秒褪掉

        // 阶段切换写一条日志：即使你停在 Scene 视图看不到 HUD，
        // 也能从 Console 判断游戏到底走到哪一步、是不是真的停了（日志不再新增=真的停了）。
        if (phase != lastLoggedPhase)
        {
            lastLoggedPhase = phase;
            Debug.Log("[RunManager] 阶段 → " + phase + " · ts=" + Time.timeScale.ToString("0.00")
                + " · 存活敌人=" + liveEnemies.Count);
        }

        // 暂停：所有游戏内计时和输入都在这一行冻结（不靠 timeScale）
        if (PauseMenu.IsPaused) return;

        // 硬兜底：全项目已经没有任何地方写 timeScale（命中停顿也改成纯震屏了），
        // 所以它必须恒为 1。一旦被旧程序集 / 第三方脚本 / 编辑器残留改成别的值，立刻拉回。
        // 之前只检查 == 0，漏掉了 0.05 这种"极慢放"——画面几乎静止，和暂停根本分不出来。
        if (Time.timeScale != 1f)
        {
            if (!timeScaleWarned)
            {
                timeScaleWarned = true;
                Debug.LogWarning("[RunManager] 检测到 timeScale = " + Time.timeScale
                    + "（不是 1），已强制复位。若反复出现，说明还有别的脚本在改它。");
            }
            Time.timeScale = 1f;
        }
        else timeScaleWarned = false;

        // 拖窗口 / 改 Game 视图分辨率时，墙和地板要跟着贴边（不然墙会跑到画面里或露出缝）
        Camera cam = Camera.main;
        if (cam != null && Mathf.Abs(cam.aspect - lastAspect) > 0.01f)
            RepositionBounds();

        // 进场计时（带绝对上限：万一 spawnTimer 因为异常没被赋值，也只冻结 SpawnHardCap 秒）
        if (spawning)
        {
            spawnTimer -= Time.deltaTime;
            spawnElapsed += Time.deltaTime;
            if (spawnElapsed > SpawnHardCap)
            {
                Debug.LogWarning("[RunManager] 进场超过 " + SpawnHardCap + " 秒仍未结束，强制恢复操控");
                spawning = false;
                spawnTimer = 0f;
                spawnElapsed = 0f;
            }
            else if (spawnTimer <= 0f)
            {
                spawning = false;
                spawnTimer = 0f;
                spawnElapsed = 0f;
            }
        }

        // 选卡保险：就算三张卡一张都没画出来，也绝不允许玩家被永远锁在选卡阶段。
        // 12 秒没选就自动拿第一项继续（键盘 1/2/3 和鼠标点击都在更前面，正常不会走到这里）。
        if (phase == Phase.Choosing && Time.unscaledTime - choosingSince > AutoPickSeconds)
        {
            Debug.LogWarning("[RunManager] 选卡 " + AutoPickSeconds + " 秒未选择，自动选第 1 项继续"
                + "（stylesReady=" + stylesReady + " skinOk=" + lastSkinOk
                + " picks=" + (choices == null ? -1 : choices.Count) + "）");
            if (choices != null && choices.Count > 0) Pick(choices[0]);
            else
            {
                choices = null;
                roomIndex++;
                phase = Phase.Playing;
                StartWave();
            }
        }

        // 快捷操作：标题页空格 / 回车开标准局，E 开无尽；结算页 R 立刻重开本局
        if (phase == Phase.Title)
        {
            if (briefOpen)
            {
                // 序章读完按任意键点灯（鼠标键也算 anyKey）
                if (Input.anyKeyDown) StartPendingRun();
            }
            else if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                OpenBrief(Mode.Standard);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                OpenBrief(Mode.Endless);
            }
        }
        else if (phase == Phase.Result && Input.GetKeyDown(KeyCode.R))
        {
            BeginRun(seed);
        }
        else if (phase == Phase.Choosing && choices != null)
        {
            // 数字键 1/2/3 直接选卡（else-if 保证同一帧只选一次）
            if (Input.GetKeyDown(KeyCode.Alpha1) && choices.Count > 0) Pick(choices[0]);
            else if (Input.GetKeyDown(KeyCode.Alpha2) && choices.Count > 1) Pick(choices[1]);
            else if (Input.GetKeyDown(KeyCode.Alpha3) && choices.Count > 2) Pick(choices[2]);
        }
    }

    // 不带模式的调用（重开本局 / 结算页 R）沿用当前模式
    public void BeginRun(int newSeed)
    {
        BeginRun(newSeed, mode);
    }

    public void BeginRun(int newSeed, Mode m)
    {
        mode = m;
        seed = newSeed;
        rng = new System.Random(seed);
        stats = RunStats.Base();          // struct 值类型，重置不会污染基础值
        roomIndex = 0;
        killCount = 0;
        shotsFired = 0;
        shotsHit = 0;
        damageTaken = 0;
        lastDamageSource = "无";
        runStart = Time.unscaledTime;
        picked.Clear();
        stacks.Clear();
        choices = null;
        victory = false;
        beatRecordShown = false;

        spawning = false;
        spawnTimer = 0f;
        spawnElapsed = 0f;
        GameAudio.SetMusicFocus(1f);       // 结算时被压低的音乐抬回来
        GameAudio.KickMusic();             // WebGL：用这次点击/按键把音频解锁

        ClearEnemies();
        ClearBoss();
        ClearRandomPlatforms();
        SetBuiltinPlatforms(false);          // 关掉场景里的固定平台，改用一个生成出来的大房间
        BuildRoom();

        phase = Phase.Playing;
        ShowBanner(Story.WaveBanner(0));
        StartWave();
        if (PlayerHealth.HasInstance) PlayerHealth.Instance.ResetHealth();
    }

    // HUD 与横幅共用的波次标签：标准模式带总数，无尽模式只显示已到达
    private string WaveLabel()
    {
        int n = roomIndex + 1;
        return mode == Mode.Standard
            ? "第 " + n + " 次涨潮 · 共 " + roomCount
            : "第 " + n + " 次涨潮 · 无尽";
    }

    // 暂停菜单用：当前模式与进度
    public static string ProgressText
    {
        get
        {
            if (Instance == null) return "";
            return Instance.mode == Mode.Standard
                ? "标准模式 · 第 " + (Instance.roomIndex + 1) + " / " + Instance.roomCount + " 波"
                : "无尽模式 · 第 " + (Instance.roomIndex + 1) + " 波";
        }
    }

    // 每一波的开场：重生成悬浮平台 → 挑随机出生点 → 传送 → 冻结操作刷怪 → 全部落地后恢复
    private void StartWave()
    {
        RebuildFloats();

        Vector3 spawn = PickSpawnPoint();
        if (PlayerHealth.HasInstance)
        {
            PlayerHealth.Instance.SetSpawn(spawn);
            PlayerHealth.Instance.RespawnAtSpawn();      // 顺带把速度清零，冻结期间不会滑
        }

        // 只冻结游戏内系统（玩家 / 敌人行为 / 子弹），不动 Time.timeScale：
        // 一旦 timeScale = 0，Unity 会认为编辑器进入暂停态，暂停键亮起、Game 视图失焦。
        spawning = true;
        flashTimer = 0.26f;                            // 开场闪白：让"新一波开始"有个明确的信号
        if (IsBossWave) SpawnBoss();                   // 标准：最后一波；无尽：每 BossEvery 波
        else SpawnRoom();
    }

    // 最后一波：单个 Boss。从房间上方落下入场，1.6 秒后解冻。
    private void SpawnBoss()
    {
        ClearBoss();

        Vector3 to = new Vector3(0f, FloorTop + 3.2f, 0f);
        Vector3 from = new Vector3(0f, FloorTop + 8f, 0f);

        GameObject go = new GameObject("Boss");
        go.transform.position = from;
        go.transform.localScale = Vector3.one * 0.5f;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = enemySprite;
        sr.color = new Color(0.95f, 0.32f, 0.52f, 1f);
        sr.sortingOrder = 6;
        go.AddComponent<BoxCollider2D>();

        Boss boss = go.AddComponent<Boss>();
        // 无尽模式里 Boss 会反复出现，所以血量按"第几个 Boss"往上加，不然第 10 波和第 5 波一样好打
        int bossIndex = mode == Mode.Endless ? roomIndex / BossEvery : 0;
        int hp = 30 + bossIndex * 12 + Mathf.RoundToInt(stats.enemyHpBonus) * 3;
        boss.Init(hp, 1.6f, from, to);

        spawnTimer = 1.6f;
    }

    private void ClearBoss()
    {
        if (Boss.Instance != null) Boss.Instance.Despawn();
    }

    // 由 Boss 在血量归零时调用。标准模式赢下 Boss 即通关；无尽模式里 Boss 只是"第 5n 波"，
    // 打完继续，并且额外回 1 血当作阶段奖励。
    public void BossDefeated()
    {
        if (phase != Phase.Playing) return;

        if (mode == Mode.Standard)
        {
            Finish(true);
            return;
        }

        if (PlayerHealth.HasInstance) PlayerHealth.Instance.Heal(1);
        GameAudio.Play(GameAudio.Sfx.Upgrade);
        ShowBanner(Story.BossDefeated);
        OfferChoices();
    }

    // 出生点：70% 在地板上的随机位置，30% 落在某块悬浮平台顶上
    private Vector3 PickSpawnPoint()
    {
        if (floatPlatforms.Count > 0 && rng.Next(100) < 30)
        {
            GameObject p = floatPlatforms[rng.Next(floatPlatforms.Count)];
            if (p != null)
            {
                float half = Mathf.Max(0.2f, p.transform.localScale.x * 0.5f - 0.6f);
                float px = p.transform.position.x + Mathf.Lerp(-half, half, (float)rng.NextDouble());
                // 落点只抬 0.5（角色缩到 0.62 后半个身位就够），避免开局先自由落体一段
                return new Vector3(px, p.transform.position.y + 0.18f + 0.5f, 0f);
            }
        }

        float x = Mathf.Lerp(boundLeft + 1.2f, boundRight - 1.2f, (float)rng.NextDouble());
        return new Vector3(x, FloorTop + 0.6f, 0f);
    }

    private void ClearEnemies()
    {
        foreach (Enemy e in liveEnemies)
            if (e != null) Destroy(e.gameObject);
        liveEnemies.Clear();
    }

    private void ClearRandomPlatforms()
    {
        foreach (GameObject p in randomPlatforms)
            if (p != null) Destroy(p);
        randomPlatforms.Clear();

        foreach (GameObject p in floatPlatforms)
            if (p != null) Destroy(p);
        floatPlatforms.Clear();
    }

    // 场地：一个大房间，没有坑。地板整块铺满，左右两侧是**看得见的墙**。
    // 墙的位置不再写死，而是按相机可视范围算出来，所以始终贴着画面左右边缘、只露出 WallVisible 那一条，
    // 既看得出"这里是房间边界"，也不会占掉战斗空间；宽高比变了（拖窗口）会自动跟随。
    // 地板贴近屏幕底部（FloorTop = -4），把上方空间全留给平台和跳跃。
    private float boundLeft = -10.2f;        // 墙内侧（玩家撞到这里停住）
    private float boundRight = 10.2f;
    private const float FloorTop = -4f;      // 地板表面高度（相机下边缘约 -6.8）
    private const float GroundY = -4.6f;     // 地板中心（厚度 1.2）
    private const float WallVisible = 0.45f;     // 墙在画面内露出的宽度（很窄，贴边）
    private const float WallThickness = 1.4f;    // 含画面外的部分，保证从任何角度看都不漏缝
    private const float WallHeight = 40f;
    private const float CameraHalfWidthFallback = 10.6f;   // 拿不到相机时的兜底（16:9 / size 6）

    private GameObject floorObj, wallL, wallR;
    private float lastAspect;

    // 只在每局开始时建一次：地板 + 两侧的墙
    private void BuildRoom()
    {
        UpdateBoundsFromCamera();

        floorObj = MakePlatform("Floor", new Vector2(0f, GroundY),
            (boundRight - boundLeft) + 8f, 1.2f,
            new Color(0.24f, 0.27f, 0.38f, 1f), randomPlatforms);

        wallL = MakeWall("Wall_L", -1f);
        wallR = MakeWall("Wall_R", 1f);
    }

    // 从相机算左右边界：墙的内侧面正好落在"画面边缘往里 WallVisible"处
    private void UpdateBoundsFromCamera()
    {
        Camera cam = Camera.main;
        float halfW = CameraHalfWidthFallback;
        if (cam != null && cam.aspect > 0.01f)
            halfW = cam.orthographicSize * cam.aspect;

        boundLeft = -(halfW - WallVisible);
        boundRight = halfW - WallVisible;
        lastAspect = cam != null ? cam.aspect : 0f;
    }

    // 可见的墙：从房间内侧看是一条贴边的竖条，延伸到画面外
    private GameObject MakeWall(string name, float side)
    {
        GameObject go = new GameObject(name);
        go.transform.localScale = new Vector3(WallThickness, WallHeight, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = enemySprite;
        sr.color = new Color(0.36f, 0.41f, 0.54f, 1f);   // 比地板亮一档，读得出来是"墙"
        sr.sortingOrder = 20;                            // 盖住敌人和子弹：从墙后飞进来的效果
        go.AddComponent<BoxCollider2D>();
        go.AddComponent<Solid>();                        // 墙也会挡子弹

        PositionWall(go, side);                          // 内侧面 = boundLeft / boundRight
        randomPlatforms.Add(go);
        return go;
    }

    // 只是把墙/地板挪到新边界，不重建（拖窗口时用）
    private void RepositionBounds()
    {
        UpdateBoundsFromCamera();

        if (floorObj != null)
            floorObj.transform.localScale =
                new Vector3((boundRight - boundLeft) + 8f, 1.2f, 1f);

        if (wallL != null) PositionWall(wallL, -1f);
        if (wallR != null) PositionWall(wallR, 1f);

        // 玩家如果正好被新墙包在外面，拉回房间内
        if (PlayerHealth.HasInstance)
        {
            Vector3 p = PlayerHealth.Instance.transform.position;
            float clamped = Mathf.Clamp(p.x, boundLeft + 0.6f, boundRight - 0.6f);
            if (!Mathf.Approximately(clamped, p.x))
                PlayerHealth.Instance.PlaceAt(new Vector3(clamped, p.y, p.z));
        }
    }

    private void PositionWall(GameObject wall, float side)
    {
        float inner = side < 0f ? boundLeft : boundRight;
        float cx = side < 0f ? inner - WallThickness * 0.5f : inner + WallThickness * 0.5f;
        wall.transform.position = new Vector3(cx, 0f, 0f);
    }

    // 悬浮平台：每个小关卡重新生成，所以每关的天花板布局都不一样。
    // 4~6 块，分低/高两条带（离地 1.4~1.9 / 2.6~3.1），都在跳跃高度 3.2 的可达范围内。
    private void RebuildFloats()
    {
        foreach (GameObject go in floatPlatforms)
            if (go != null) Destroy(go);
        floatPlatforms.Clear();

        float left = boundLeft + 1.2f;
        float right = boundRight - 1.2f;
        float span = Mathf.Max(6f, right - left);

        // 4~5 块。之前是 5~7 块纯随机撒点，重试 10 次放不下就互相重叠，
        // 看起来"很挤"——问题不在数量，而在没有分配水平空间。
        int count = 4 + rng.Next(2);
        float slot = span / count;

        for (int i = 0; i < count; i++)
        {
            // 每块平台占用一个等宽槽位：宽度只占槽位的 55%~73%，
            // 所以无论随机怎么摇，相邻两块之间一定留得下一条能站人的缝。
            float w = Mathf.Clamp(slot * (0.55f + (float)rng.NextDouble() * 0.18f), 1.5f, 2.8f);

            float baseX = left + slot * (i + 0.5f);
            float jitterRange = Mathf.Max(0f, (slot - w) * 0.5f);
            float x = baseX + (float)(rng.NextDouble() - 0.5) * jitterRange * 0.8f;
            x = Mathf.Clamp(x, left + w * 0.5f, right - w * 0.5f);

            // 高低交替：相邻两块不会同高，跳起来有节奏，也不会连成一条挡路的"墙"。
            // 两个高度差 1.1，远小于跳跃高度 3.2；高台顶面离地 2.55，起跳后还有约 0.5 的余量。
            bool high = (i % 2) == 1;
            float y = FloorTop + (high ? 2.55f : 1.45f) + (float)(rng.NextDouble() - 0.5) * 0.18f;

            MakePlatform("Float_" + i, new Vector2(x, y), w, 0.34f,
                new Color(0.28f, 0.36f, 0.5f, 1f), floatPlatforms);
        }
    }

    private GameObject MakePlatform(string name, Vector2 center, float width, float height, Color color,
        List<GameObject> sink)
    {
        GameObject go = new GameObject(name);
        go.transform.position = new Vector3(center.x, center.y, 0f);
        go.transform.localScale = new Vector3(width, height, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = enemySprite;
        sr.color = color;
        go.AddComponent<BoxCollider2D>();
        go.AddComponent<Solid>();      // 标记为地形：子弹会被它挡住
        sink.Add(go);
        return go;
    }

    // 场景里手工摆的 Platform_A/B/C：开局时关掉，让位给随机地图；回标题时恢复
    private void SetBuiltinPlatforms(bool active)
    {
        if (builtinPlatforms.Count == 0)
        {
            foreach (string n in new[] { "Platform_A", "Platform_B", "Platform_C" })
            {
                GameObject go = GameObject.Find(n);
                if (go != null) builtinPlatforms.Add(go);
            }
        }
        foreach (GameObject go in builtinPlatforms)
            if (go != null) go.SetActive(active);
    }

    // 怪物随机：位置在场景内随机且离玩家至少 2.5 单位；类型分普通 / 快速 / 精英，
    // 血量、体型、颜色、射速都不同，全部由同一个 rng 决定。
    private void SpawnRoom()
    {
        int waveHp, count, wNormal, wFast, wCharger, wTurret;
        WaveSpecFor(roomIndex, out count, out waveHp, out wNormal, out wFast, out wCharger, out wTurret);
        int baseHp = Mathf.Max(1, waveHp + stats.enemyHpBonus);

        // 无尽模式里敌人的开火节奏也随波数收紧（最快到 65%，再往下就变成纯弹幕，没有走位空间了）
        float paceScale = mode == Mode.Endless
            ? Mathf.Max(0.65f, 1f - roomIndex * 0.02f)
            : 1f;

        float maxEnd = 0f;   // 最后一只的落地时刻（延迟 + 飞行）

        for (int i = 0; i < count; i++)
        {
            float x = 0f, y = 0f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                // 站位铺满整个房间（原来写死 -7~7，房间边界现在到 ±10 左右）
                x = Mathf.Lerp(boundLeft + 1.2f, boundRight - 1.2f, (float)rng.NextDouble());
                y = GroundY + 1.0f + (float)rng.NextDouble() * 2.6f;
                if (!PlayerHealth.HasInstance ||
                    Vector2.Distance(new Vector2(x, y), PlayerHealth.Instance.transform.position) > 2.5f) break;
            }

            int roll = rng.Next(100);
            int hp;
            float scale, interval;
            Color color;
            string typeName;
            Enemy.Kind kind;

            if (roll < wNormal)
            {
                hp = baseHp; scale = 1f; interval = 2.2f;
                color = new Color(0.85f, 0.25f, 0.3f, 1f);       // 普通：稳定射击
                typeName = "普通怪";
                kind = Enemy.Kind.Shooter;
            }
            else if (roll < wNormal + wFast)
            {
                hp = Mathf.Max(1, baseHp - 1); scale = 0.75f; interval = 1.2f;
                color = new Color(0.95f, 0.6f, 0.2f, 1f);        // 快速：脆但射得快
                typeName = "快速怪";
                kind = Enemy.Kind.Shooter;
            }
            else if (roll < wNormal + wFast + wCharger)
            {
                hp = Mathf.Max(1, baseHp - 1); scale = 0.95f; interval = 999f;
                color = new Color(0.30f, 0.85f, 0.72f, 1f);      // 冲锋：贴地撞人，逼你跳
                typeName = "冲锋怪";
                kind = Enemy.Kind.Charger;
                y = GroundY + 0.9f;                              // 贴地生成
            }
            else if (roll < wNormal + wFast + wCharger + wTurret)
            {
                hp = baseHp + 1; scale = 1.1f; interval = 2.6f;
                color = new Color(0.40f, 0.75f, 1f, 1f);         // 炮台：不动，三连散射
                typeName = "炮台";
                kind = Enemy.Kind.Turret;
            }
            else
            {
                hp = baseHp + 3; scale = 1.4f; interval = 2.8f;
                color = new Color(0.7f, 0.35f, 0.9f, 1f);        // 精英：肉但射得慢
                typeName = "精英怪";
                kind = Enemy.Kind.Shooter;
            }
            float firstDelay = 0.6f + (float)rng.NextDouble() * 1.4f;

            GameObject go = new GameObject("Enemy_" + roomIndex + "_" + i);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = Vector3.one * (scale * CharacterScale);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = enemySprite;
            sr.color = color;
            sr.sortingOrder = 5;
            go.AddComponent<BoxCollider2D>();
            Enemy e = go.AddComponent<Enemy>();
            e.kind = kind;                 // 必须在 Init 之前：Init 会按 kind 决定移动速度
            e.TypeName = typeName;
            e.Init(hp, interval * paceScale, color, firstDelay);

            // 入场：从墙后面飞进来（墙的 sortingOrder 比敌人高，所以会先被墙挡住再"钻"出来）。
            // 每只飞的时长在 1.1~1.7 秒之间随机，并且彼此错开 0.12 秒；
            // 这段时间玩家的操作和敌方子弹都被冻结。
            Vector3 target = new Vector3(x, y, 0f);
            Vector3 from = new Vector3(x < 0f ? boundLeft - 2.2f : boundRight + 2.2f, y, 0f);
            float introDur = Mathf.Lerp(IntroMin, IntroMax, (float)rng.NextDouble());
            float introDelay = IntroDelayStep * i;
            maxEnd = Mathf.Max(maxEnd, introDelay + introDur);
            e.PlayIntro(target, from, introDur, introDelay);

            liveEnemies.Add(e);
        }

        // 冻结时长按实际最后一只落地来算，全部到位才恢复操作
        spawnTimer = maxEnd + 0.05f;
    }

    public void NotifyKill(Enemy enemy)
    {
        if (phase != Phase.Playing) return;
        killCount++;
        liveEnemies.Remove(enemy);
        if (liveEnemies.Count > 0) return;

        // 无尽模式没有终点：清完就进下一波，只有死才是结束
        if (mode == Mode.Endless) { OfferChoices(); return; }

        if (roomIndex + 1 < roomCount) OfferChoices();
        else Finish(true);
    }

    // ---- 一局的统计（结算页用来解释"你为什么输"）----

    public void NotifyShot(int bullets)
    {
        if (phase == Phase.Playing) shotsFired += bullets;
    }

    public void NotifyHit()
    {
        if (phase == Phase.Playing) shotsHit++;
    }

    public void NotifyDamaged(int amount, string source)
    {
        if (phase != Phase.Playing) return;
        damageTaken += amount;
        lastDamageSource = source;
        hurtFlash = 1f;      // 屏幕边缘红闪：挨打的瞬间要给一次整体反馈
    }

    private float Accuracy
    {
        get { return shotsFired > 0 ? (float)shotsHit / shotsFired : 0f; }
    }

    private void OfferChoices()
    {
        List<Modifier> pool = new List<Modifier>();
        foreach (Modifier m in ModifierTable.All)
        {
            int used = stacks.ContainsKey(m.id) ? stacks[m.id] : 0;
            if (used < m.maxStacks) pool.Add(m);   // 防止叠加爆炸
        }

        choices = new List<Modifier>();
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            int idx = rng.Next(pool.Count);
            choices.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        // 池子被叠满了（无尽模式跑得够远一定会遇到）：没有可选项就别弹一张空面板，
        // 直接进下一波并回 1 血。否则玩家要等 12 秒的自动兜底才继续。
        if (choices.Count == 0)
        {
            choices = null;
            roomIndex++;
            phase = Phase.Playing;
            if (PlayerHealth.HasInstance) PlayerHealth.Instance.Heal(1);
            GameAudio.Play(GameAudio.Sfx.Upgrade);
            AnnounceWave();
            StartWave();
            return;
        }

        phase = Phase.Choosing;
        choosingSince = Time.unscaledTime;
    }

    // 进入新一波时统一决定横幅文案：Boss 波 / 破纪录 / 普通波
    private void AnnounceWave()
    {
        string text = IsBossWave ? Story.BossWave : Story.WaveBanner(roomIndex);

        // 无尽模式第一次超过历史最远波数，给一次明确的"破纪录"时刻；
        // 只在刚开始超过时提示一次，之后每波都提示就变成噪音了。
        if (mode == Mode.Endless && !beatRecordShown && GameSettings.BestWave > 0
            && roomIndex + 1 > GameSettings.BestWave)
        {
            beatRecordShown = true;
            text = "★ 超过最远记录 · 第 " + (roomIndex + 1) + " 次涨潮";
            GameAudio.Play(GameAudio.Sfx.Win);
        }

        ShowBanner(text);
    }

    private void Pick(Modifier m)
    {
        Apply(m);
        picked.Add(m);
        stacks[m.id] = (stacks.ContainsKey(m.id) ? stacks[m.id] : 0) + 1;
        choices = null;
        roomIndex++;
        phase = Phase.Playing;

        // 波间补给 1 血；新的出生点与平台布局由 StartWave 处理
        if (PlayerHealth.HasInstance) PlayerHealth.Instance.Heal(1);

        GameAudio.Play(GameAudio.Sfx.Upgrade);
        AnnounceWave();
        StartWave();
    }

    private void Apply(Modifier m)
    {
        ApplyOne(m.target, m.op, m.value);
        if (m.dual) ApplyOne(m.target2, m.op2, m.value2);   // 有代价的取舍：一次改两个字段
    }

    private void ApplyOne(StatTarget target, ModifierOp op, float value)
    {
        switch (target)
        {
            case StatTarget.FireRate:
                stats.fireRate = op == ModifierOp.Add ? stats.fireRate + value : stats.fireRate * value;
                stats.fireRate = Mathf.Clamp(stats.fireRate, 1.5f, 30f);
                break;

            case StatTarget.BulletCount:
                stats.bulletCount = Mathf.Clamp(stats.bulletCount + Mathf.RoundToInt(value), 1, 5);
                break;

            case StatTarget.BulletDamage:
                stats.bulletDamage = Mathf.Clamp(stats.bulletDamage + Mathf.RoundToInt(value), 1, 8);
                break;

            case StatTarget.MoveSpeed:
                stats.moveSpeedMul = op == ModifierOp.Add
                    ? stats.moveSpeedMul + value
                    : stats.moveSpeedMul * value;
                stats.moveSpeedMul = Mathf.Clamp(stats.moveSpeedMul, 0.6f, 1.8f);
                break;

            case StatTarget.MaxHp:
                int delta = Mathf.RoundToInt(value);
                stats.maxHpBonus = Mathf.Clamp(stats.maxHpBonus + delta, -3, 4);
                if (PlayerHealth.HasInstance)
                {
                    if (delta > 0) PlayerHealth.Instance.Heal(delta);
                    PlayerHealth.Instance.ClampToMax();
                }
                break;

            case StatTarget.EnemyHp:
                stats.enemyHpBonus = Mathf.Clamp(stats.enemyHpBonus + Mathf.RoundToInt(value), -5, 5);
                break;
        }
    }

    // public：玩家血量归零时由 PlayerHealth 调用，触发失败结算
    public void Finish(bool win)
    {
        // 无尽模式没有"通关"，只有撑到第几波；标准模式才有胜利
        if (mode == Mode.Endless) win = false;

        victory = win;
        phase = Phase.Result;
        GameAudio.Play(win ? GameAudio.Sfx.Win : GameAudio.Sfx.Fail);
        // 结算时音乐压低，让胜负音效站在前面；回战斗时再抬回来
        GameAudio.SetMusicFocus(win ? 0.9f : 0.35f);

        // 战绩入档：击杀 / 通关时间 / 到达波数。
        // 波数只在无尽模式记录——标准模式最多 3 波，混进"最远波数"会让无尽模式的纪录失去意义。
        float elapsed = Time.unscaledTime - runStart;
        GameSettings.SubmitRun(killCount, win ? elapsed : 0f,
            mode == Mode.Endless ? roomIndex + 1 : 0);
    }

    // 暂停菜单用：重开当前 seed（复现同一局）
    public void RestartCurrent()
    {
        BeginRun(seed);
    }

    // 暂停菜单用：清场回到标题页，保留历史最佳记录
    public void ReturnToTitle()
    {
        spawning = false;
        spawnTimer = 0f;
        spawnElapsed = 0f;
        briefOpen = false;                 // 回标题要看到模式选择，而不是卡在序章
        GameAudio.SetMusicFocus(1f);       // 结算时被压低的音乐抬回来
        GameAudio.KickMusic();             // WebGL：用这次点击/按键把音频解锁

        ClearEnemies();
        ClearBoss();
        ClearRandomPlatforms();
        SetBuiltinPlatforms(true);       // 标题页恢复场景原平台，不至于空荡荡
        choices = null;
        bannerText = "";
        bannerTimer = 0f;
        phase = Phase.Title;
    }

    private void ShowBanner(string text)
    {
        bannerText = text;
        bannerTimer = 1.8f;
    }

    // ---------- UI ----------

    private void OnGUI()
    {
        // Game 视图没有在渲染时（例如切到了 Scene 标签页），GUI.skin 会为 null，
        // 此时任何 GUI 调用都会抛 NullReferenceException。直接跳过这一帧，等回到 Game 视图再画。
        lastSkinOk = GUI.skin != null;
        if (!lastSkinOk) return;

        // 任何绘制异常都不允许反复刷屏，更不允许因此中断游戏 —— 只记一次，继续跑
        try
        {
            DrawGui();
        }
        catch (System.Exception e)
        {
            if (!guiErrorLogged)
            {
                guiErrorLogged = true;
                Debug.LogWarning("[RunManager] OnGUI 绘制异常（已忽略，游戏继续）：" + e);
            }
        }
    }

    private void DrawGui()
    {
        EnsureStyles();

        // 右上角状态角标：用来一眼判断"到底是不是真的暂停"。
        // ts 必须永远是 1.00；phase 在 PLAYING / CHOOSING 之间切换。
        GUI.Label(new Rect(Screen.width - 268f, 6f, 262f, 20f),
            BuildTag + " · " + phase + " · ts " + Time.timeScale.ToString("0.00")
            + (IsSpawning ? " · 进场中" : ""));

        // 样式没建好就只画一行提示：绝不让 OnGUI 抛异常刷屏，也绝不让三选一消失
        if (!stylesReady)
        {
            GUI.Label(new Rect(12f, 12f, Screen.width - 24f, 24f), "UI 样式初始化失败，正在重试…（详见 Console）");

            // 兜底：哪怕样式全挂了也要能选卡，否则玩家会永远卡在选卡阶段。
            // 同样先取快照、点完立刻 return，避免 Pick() 把 choices 置空后回读。
            List<Modifier> list = choices;
            if (phase == Phase.Choosing && list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Modifier m = list[i];
                    if (m == null) continue;
                    Rect r = new Rect(60f, 120f + i * 60f, 300f, 48f);
                    if (!GUI.Button(r, "[" + (i + 1) + "]  " + m.displayName)) continue;
                    Pick(m);
                    return;
                }
            }
            return;
        }

        // 三选一不压暗全屏：卡片放在屏幕下方，尽量少占画面
        if (phase == Phase.Title || phase == Phase.Result)
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTex);

        // 每波开场的闪白：很轻（0.2 透明度），只是让"新一波"有个起始节拍
        if (flashTimer > 0f && flashTex != null)
        {
            float a = Mathf.Clamp01(flashTimer / 0.26f) * 0.20f;
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), flashTex);
            GUI.color = Color.white;
        }

        // 受伤红闪 + 低血量脉动：都只画在画面四周，不挡中间的战场
        DrawEdgeWarning();

        if (phase == Phase.Title)
        {
            DrawTitle();
            return;
        }

        DrawHud();

        if (phase == Phase.Choosing) DrawChoices();
        else if (phase == Phase.Result) DrawResult();
        else
        {
            DrawCrosshair();
            DrawFooter();
            if (bannerTimer > 0f) DrawBanner();
            if (IsSpawning)
                GUI.Label(new Rect(0f, Screen.height * 0.60f, Screen.width, 24f),
                    "敌人进场中…（落地后才能行动）", hintStyle);
        }
    }

    // 屏幕四周的危险提示：受伤瞬间红闪一下，只剩 1 血时持续缓慢脉动。
    // 只画四条边而不是铺满全屏，避免盖住战场和准星，也不影响读血条。
    private void DrawEdgeWarning()
    {
        if (flashTex == null) return;

        float hurt = Mathf.Clamp01(hurtFlash);
        bool lowHp = PlayerHealth.HasInstance && !PlayerHealth.Instance.IsDead
                     && PlayerHealth.Instance.Hp <= 1
                     && (phase == Phase.Playing || phase == Phase.Choosing);

        float pulse = lowHp ? 0.10f + Mathf.Sin(Time.unscaledTime * 4.2f) * 0.05f : 0f;
        float a = Mathf.Max(hurt * 0.55f, pulse);
        if (a <= 0.01f) return;

        float thick = Mathf.Lerp(8f, 30f, hurt);
        GUI.color = new Color(0.92f, 0.14f, 0.18f, a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, thick), flashTex);
        GUI.DrawTexture(new Rect(0f, Screen.height - thick, Screen.width, thick), flashTex);
        GUI.DrawTexture(new Rect(0f, 0f, thick, Screen.height), flashTex);
        GUI.DrawTexture(new Rect(Screen.width - thick, 0f, thick, Screen.height), flashTex);
        GUI.color = Color.white;
    }

    // 点模式按钮 → 先看序章，再真正开局。
    // 没有为剧情单开一个 Phase，是为了不碰状态机：Phase 一变，IsPlaying / IsControllable
    // / 各系统的 return 条件全都要跟着改，收益远小于风险。
    private void OpenBrief(Mode m)
    {
        pendingMode = m;
        briefOpen = true;
    }

    private void StartPendingRun()
    {
        briefOpen = false;
        BeginRun(seedOverride != 0 ? seedOverride : Environment.TickCount, pendingMode);
    }

    private void DrawTitle()
    {
        if (briefOpen)
        {
            DrawBrief();
            return;
        }

        float w = Mathf.Min(560f, Screen.width - 40f);
        GUILayout.BeginArea(CenterRect(w, 408f), panelStyle);
        GUILayout.Label(Story.GameTitle, titleStyle);
        GUILayout.Label(Story.GameSubtitle, hintStyle);
        GUILayout.Space(14);

        if (GUILayout.Button(Story.PlayStandard, buttonStyle, GUILayout.Height(50)))
            OpenBrief(Mode.Standard);
        GUILayout.Space(8);
        if (GUILayout.Button(Story.PlayEndless, buttonStyle, GUILayout.Height(50)))
            OpenBrief(Mode.Endless);
        GUILayout.Space(8);
        GUILayout.Label(Story.HintStandard, hintStyle);
        GUILayout.Label(Story.HintEndless, hintStyle);

        GUILayout.Space(10);
        GUILayout.Label(GameSettings.RecordText(), hintStyle);
        GUILayout.Space(12);
        GUILayout.Label("A / D 移动    Space 跳    J 或鼠标左键开火", hintStyle);
        GUILayout.Label("Esc 暂停与设置（震屏 / 音量）    M 静音", hintStyle);
        GUILayout.Label("构建 " + BuildTag, hintStyle);
        GUILayout.EndArea();
    }

    private void DrawBrief()
    {
        float w = Mathf.Min(540f, Screen.width - 40f);
        GUILayout.BeginArea(CenterRect(w, 344f), panelStyle);
        GUILayout.Label(Story.GameTitle, titleStyle);
        GUILayout.Label(Story.GameSubtitle, hintStyle);
        GUILayout.Space(18);

        foreach (string line in Story.Brief)
            GUILayout.Label(line, labelStyle);

        GUILayout.Space(18);
        if (GUILayout.Button("点灯 · 开始", buttonStyle, GUILayout.Height(52)))
            StartPendingRun();
        GUILayout.Label("按任意键也可以", hintStyle);
        GUILayout.EndArea();
    }

    private void DrawHud()
    {
        float w = Mathf.Min(640f, Screen.width - 40f);
        GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, 14f, w, 64f), panelStyle);
        int hp = PlayerHealth.HasInstance ? PlayerHealth.Instance.Hp : 0;
        int maxHp = PlayerHealth.HasInstance ? PlayerHealth.Instance.MaxHp : 0;

        // 场上还剩几个目标：清场型玩法里"还剩 2 只"和"还剩 0 只"是完全不同的处境，
        // 只显示击杀数玩家没法判断这一波结束没有。
        int remaining = liveEnemies.Count;
        if (Boss.Instance != null && Boss.Instance.Alive) remaining++;

        GUILayout.BeginHorizontal();
        GUILayout.Label(WaveLabel(), labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("HP " + hp + " / " + maxHp, labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(remaining > 0 ? "剩余 " + remaining : "清场中", labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("击杀 " + killCount, labelStyle);
        GUILayout.EndHorizontal();

        string info = "射速 " + stats.fireRate.ToString("0.0")
            + "     伤害 " + stats.bulletDamage
            + " × " + stats.bulletCount + " 弹丸"
            + "     词条 " + picked.Count;
        if (mode == Mode.Endless && GameSettings.BestWave > 0)
            info += "     最远 " + GameSettings.BestWave + " 波";
        GUILayout.Label(info, hintStyle);
        GUILayout.EndArea();

        // Boss 血条：贴在 HUD 下方，只在 Boss 存活时出现
        if (Boss.Instance != null && Boss.Instance.Alive)
        {
            float bw = Mathf.Min(420f, Screen.width - 80f);
            Rect bar = new Rect((Screen.width - bw) * 0.5f, 86f, bw, 12f);
            GUI.DrawTexture(bar, barBgTex);

            float ratio = Mathf.Clamp01((float)Boss.Instance.Hp / Mathf.Max(1, Boss.Instance.MaxHp));
            GUI.DrawTexture(new Rect(bar.x, bar.y, bw * ratio, bar.height), barFillTex);

            // 四分刻度：把"还剩多少"变成一眼能读的四段，不用去看数字
            for (int i = 1; i < 4; i++)
                GUI.DrawTexture(new Rect(bar.x + bw * (i / 4f) - 1f, bar.y, 2f, bar.height), barBgTex);

            GUI.Label(new Rect(0f, 100f, Screen.width, 20f),
                "BOSS   " + Boss.Instance.Hp + " / " + Boss.Instance.MaxHp
                + (Boss.Instance.Enraged ? "   狂暴" : ""), hintStyle);
        }
    }

    // 三选一：屏幕靠下的一排小卡片，不遮挡战斗区域
    private void DrawChoices()
    {
        // 关键：先取局部快照。Pick() 里会把 choices 置为 null，
        // 以前循环条件写成 i < choices.Count，点第一张之后下一轮就 NRE（就是那条 776 行的报错）。
        List<Modifier> list = choices;
        if (list == null || list.Count == 0) return;

        // 样式万一没建成功，先重建一次；仍失败就退回默认皮肤，保证卡片一定画得出来。
        // 卡片画不出来 = 玩家永远卡在选卡阶段，比样式难看严重得多。
        if (choiceTitleStyle == null || cardStyle == null)
        {
            stylesReady = false;
            EnsureStyles();
        }
        bool styled = choiceTitleStyle != null && cardStyle != null;

        float cardW = Mathf.Clamp((Screen.width - 96f) / 3f, 110f, 210f);
        float cardH = 96f;
        float gap = 12f;
        float totalW = cardW * 3f + gap * 2f;
        float x0 = (Screen.width - totalW) * 0.5f;
        float y = Screen.height - cardH - 46f;          // 距底部约 46px

        Rect titleRect = new Rect(0f, y - 30f, Screen.width, 26f);
        if (styled) GUI.Label(titleRect, Story.ChoiceTitle, choiceTitleStyle);
        else GUI.Label(titleRect, Story.ChoiceTitle);

        for (int i = 0; i < 3 && i < list.Count; i++)
        {
            Modifier m = list[i];
            if (m == null) continue;

            Rect r = new Rect(x0 + i * (cardW + gap), y, cardW, cardH);
            string label = "[" + (i + 1) + "]  " + m.displayName;
            bool clicked = styled ? GUI.Button(r, label, cardStyle) : GUI.Button(r, label);
            if (!clicked) continue;

            Pick(m);
            return;     // 立即结束本帧绘制：Pick 之后 choices 已为 null，不能再往下走
        }

        Rect hintRect = new Rect(0f, y + cardH + 4f, Screen.width, 20f);
        if (hintStyle != null) GUI.Label(hintRect, Story.ChoiceHint, hintStyle);
        else GUI.Label(hintRect, Story.ChoiceHint);
    }

    private void DrawResult()
    {
        bool endless = mode == Mode.Endless;
        float w = Mathf.Min(580f, Screen.width - 40f);
        GUILayout.BeginArea(CenterRect(w, 418f), panelStyle);
        GUILayout.Label(victory ? "通关" : "阵亡", titleStyle);
        GUILayout.Label(victory
            ? Story.Victory
            : (endless ? string.Format(Story.EndlessDeath, roomIndex + 1) : Story.Death), hintStyle);

        // 刷新记录时给一句明文提示，而不是让玩家自己去标题页比对
        if (GameSettings.LastRunSetRecord)
            GUILayout.Label(endless
                ? "★ 新纪录（最远第 " + GameSettings.BestWave + " 波）"
                : "★ 新纪录", labelStyle);
        else if (endless && GameSettings.BestWave > 0)
            GUILayout.Label("最远记录：第 " + GameSettings.BestWave + " 波", hintStyle);

        GUILayout.Space(6);

        GUILayout.Label("用时 " + (Time.unscaledTime - runStart).ToString("0.0")
            + " 秒     击杀 " + killCount + "     seed " + seed, labelStyle);

        // 失败要能解释：打空了多少枪、挨了多少下、最后被谁打死
        GUILayout.Label("命中率 " + (Accuracy * 100f).ToString("0")
            + "%（" + shotsHit + " / " + shotsFired + "）     受伤 " + damageTaken + " 点", labelStyle);

        if (victory)
            GUILayout.Label("剩余血量 " + (PlayerHealth.HasInstance ? PlayerHealth.Instance.Hp : 0)
                + " / " + (PlayerHealth.HasInstance ? PlayerHealth.Instance.MaxHp : 0), hintStyle);
        else
            GUILayout.Label("最后一击来自：" + lastDamageSource, hintStyle);

        GUILayout.Space(8);
        GUILayout.Label("本局词条：" + (picked.Count > 0
            ? string.Join(" / ", picked.ConvertAll(p => p.displayName).ToArray())
            : "无"), hintStyle);

        GUILayout.Space(14);
        if (GUILayout.Button("重玩本局（同 seed）", buttonStyle, GUILayout.Height(46))) BeginRun(seed);
        if (GUILayout.Button("开新的一局（新 seed）", buttonStyle, GUILayout.Height(46)))
            BeginRun(Environment.TickCount ^ killCount);
        if (GUILayout.Button("返回标题（换模式）", buttonStyle, GUILayout.Height(40)))
            ReturnToTitle();
        GUILayout.Space(6);
        GUILayout.Label("R 立刻重开本局（同 seed）", hintStyle);
        GUILayout.EndArea();
    }

    private void DrawFooter()
    {
        float w = Mathf.Min(640f, Screen.width - 40f);
        GUI.Label(new Rect((Screen.width - w) * 0.5f, Screen.height - 34f, w, 26f),
            "A/D 移动 · Space 跳 · J/左键 开火 · Esc 暂停 · M 静音 · F1 调参", hintStyle);
    }

    private void DrawBanner()
    {
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(bannerTimer / 0.6f));
        GUI.Label(new Rect(0f, Screen.height * 0.26f, Screen.width, 60f), bannerText, bannerStyle);
        GUI.color = old;
    }

    private void DrawCrosshair()
    {
        Vector3 mp = Input.mousePosition;
        // 鼠标离开 Game 视图时可能是 NaN；NaN 塞进 Rect 会污染 IMGUI 布局，
        // 进而导致编辑器的 UI Layout Error（RectTransform max=nan）。
        if (float.IsNaN(mp.x) || float.IsNaN(mp.y)) return;

        float x = mp.x;
        float y = Screen.height - mp.y;
        GUI.Label(new Rect(x - 20f, y - 20f, 40f, 40f), "+", crosshairStyle);
    }

    private static Rect CenterRect(float w, float h)
    {
        return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
    }

    private void EnsureStyles()
    {
        if (stylesReady) return;

        // 样式构建一旦中途抛异常，绝不能留下"半套 null 样式"然后每帧 NRE：
        // 用 try/catch 包住 + stylesReady 只在成功时置位，失败就下一帧重试。
        try
        {
            BuildStyles();
            stylesReady = true;
            styleErrorLogged = false;
        }
        catch (System.Exception e)
        {
            if (!styleErrorLogged)
            {
                styleErrorLogged = true;
                Debug.LogWarning("[RunManager] UI 样式初始化失败，下一帧重试：" + e.Message);
            }
        }
    }

    private void BuildStyles()
    {
        panelTex = MakeTex(new Color(0.09f, 0.10f, 0.14f, 0.96f));
        dimTex = MakeTex(new Color(0.02f, 0.03f, 0.05f, 0.66f));

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTex;
        panelStyle.padding = new RectOffset(22, 22, 18, 18);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 26;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.wordWrap = true;
        titleStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 15;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = new Color(0.92f, 0.94f, 1f);

        hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.fontSize = 12;
        hintStyle.alignment = TextAnchor.MiddleCenter;
        hintStyle.wordWrap = true;
        hintStyle.normal.textColor = new Color(0.66f, 0.72f, 0.82f);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 15;
        buttonStyle.wordWrap = true;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        buttonStyle.padding = new RectOffset(10, 10, 10, 10);

        bannerStyle = new GUIStyle(GUI.skin.label);
        bannerStyle.fontSize = 36;
        bannerStyle.fontStyle = FontStyle.Bold;
        bannerStyle.alignment = TextAnchor.MiddleCenter;
        bannerStyle.normal.textColor = new Color(1f, 0.88f, 0.5f);

        crosshairStyle = new GUIStyle(GUI.skin.label);
        crosshairStyle.fontSize = 22;
        crosshairStyle.alignment = TextAnchor.MiddleCenter;
        crosshairStyle.normal.textColor = new Color(1f, 0.9f, 0.5f, 0.9f);

        // 底部三选一卡片：比默认按钮更暗、字更亮，小尺寸也不糊
        choiceTitleStyle = new GUIStyle(titleStyle);
        choiceTitleStyle.fontSize = 18;

        cardTex = MakeTex(new Color(0.11f, 0.13f, 0.18f, 0.97f));
        cardHoverTex = MakeTex(new Color(0.19f, 0.23f, 0.31f, 1f));

        cardStyle = new GUIStyle(GUI.skin.button);
        cardStyle.fontSize = 15;
        cardStyle.wordWrap = true;
        cardStyle.alignment = TextAnchor.MiddleCenter;
        cardStyle.padding = new RectOffset(8, 8, 8, 8);
        cardStyle.normal.background = cardTex;
        cardStyle.hover.background = cardHoverTex;
        cardStyle.active.background = cardHoverTex;
        cardStyle.normal.textColor = new Color(0.96f, 0.92f, 0.82f);
        cardStyle.hover.textColor = new Color(1f, 0.88f, 0.45f);
        cardStyle.active.textColor = new Color(1f, 0.88f, 0.45f);

        // Boss 血条
        barBgTex = MakeTex(new Color(0.16f, 0.06f, 0.10f, 0.92f));
        barFillTex = MakeTex(new Color(0.92f, 0.28f, 0.38f, 1f));

        flashTex = MakeTex(Color.white);   // 开场闪白用，靠 GUI.color 调透明度
    }

    private static Texture2D MakeTex(Color c)
    {
        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private static Sprite MakeSprite()
    {
        const int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        // PPU = 8，8x8 像素 = 1 世界单位
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private void OnDestroy()
    {
        // 退出 Play / 场景卸载时兜底：保证时间缩放不会残留在非 1 的状态
        Time.timeScale = 1f;
        if (Instance == this) Instance = null;
        if (panelTex != null) Destroy(panelTex);
        if (dimTex != null) Destroy(dimTex);
        if (cardTex != null) Destroy(cardTex);
        if (cardHoverTex != null) Destroy(cardHoverTex);
        if (barBgTex != null) Destroy(barBgTex);
        if (barFillTex != null) Destroy(barFillTex);
        if (flashTex != null) Destroy(flashTex);
        if (enemySprite != null) Destroy(enemySprite);
    }
}
