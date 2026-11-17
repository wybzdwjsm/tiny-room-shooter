using UnityEngine;

// 玩家视觉层：本体精灵 + 描边 + 眼睛 + 程序化动画，全部挂在 Visual 子节点下。
//
// 为什么必须放在子节点：根节点的 transform 上有 BoxCollider2D 和 Rigidbody2D，
// 而且 RunManager 会把根节点 scale 设成 CharacterScale=0.62。
// 任何"呼吸/拉伸/压扁/倾斜"如果直接改根节点，会连带把碰撞体一起变形，
// 于是出现"看着跳过了、实际判定没过"这种最难查的 bug。
// 把视觉单独放一层，根节点只管物理，两边彻底解耦。
public class PlayerVisual : MonoBehaviour
{
    private static PlayerVisual instance;

    private Transform visualRoot;      // 承载所有视觉形变的子节点
    private SpriteRenderer body;       // 根节点原精灵（会关掉，避免和 visualRoot 上的重影）
    private SpriteRenderer art;        // visualRoot 上的角色精灵
    private SpriteRenderer outline;
    private SpriteRenderer eyeL;
    private SpriteRenderer eyeR;
    private Camera cam;
    private PlayerMotor motor;

    // 弹簧驱动的动画量（替代原来的线性 MoveTowards，动作之间才有惯性衔接）
    private Spring squashSpring;       // 落地压扁
    private Spring stretchSpring;      // 起跳拉伸
    private Spring recoilSpring;       // 开火/受击后坐
    private Spring leanSpring;         // 奔跑倾斜
    private float runPhase;            // 跑步周期相位（连续起伏，不是一次性倾斜）
    private int lastFaceSign;          // 转身检测

    private bool hasArt;
    private float faceDir = 1f;
    private Vector2 recoilDir;
    private float hurtLock;
    private bool wasGrounded;
    private bool visible = true;
    private Vector3 baseScale = Vector3.one;

    // 暖白 + 近黑：图片加载失败时退回的方块配色
    private static readonly Color BodyColor = new Color(0.98f, 0.94f, 0.85f, 1f);
    private static readonly Color InkColor = new Color(0.07f, 0.09f, 0.14f, 1f);
    private static readonly Color DustColor = new Color(0.75f, 0.82f, 0.95f, 1f);

    private const int BodyOrder = 12;

    public static bool HasInstance { get { return instance != null; } }

    public static void Attach(GameObject player)
    {
        if (player == null || player.GetComponent<PlayerVisual>() != null) return;
        player.AddComponent<PlayerVisual>();
    }

    // 受伤闪烁时由 PlayerHealth 调用
    // Weapon 每次开火调用：枪口闪光 + 后坐力
    public static void FireKick(Vector2 muzzle, Vector2 dir)
    {
        if (instance == null || instance.hurtLock > 0f) return;
        instance.recoilDir = -dir;
        instance.recoilSpring.Kick(-9f);   // 给个冲量，弹簧自己弹回来
        FxRings.Muzzle(muzzle, dir, new Color(1f, 0.86f, 0.42f, 0.9f));
    }

    // PlayerHealth 受伤时调用：视觉层后仰 + 短暂放大，强化"挨了一下"
    public static void Hurt(Vector2 fromDir)
    {
        if (instance == null) return;
        instance.hurtLock = 0.22f;
        instance.recoilDir = new Vector2(-fromDir.x, 0.35f).normalized;
        instance.recoilSpring.Kick(-16f);  // 受击冲量比开火大
        HitParticles.Spawn(instance.transform.position, new Color(1f, 0.45f, 0.5f, 1f), 8);
    }

    public static void SetVisible(bool v)
    {
        if (instance != null) instance.ApplyVisible(v);
    }

    private void Awake()
    {
        instance = this;
        cam = Camera.main;
        motor = GetComponent<PlayerMotor>();

        body = GetComponent<SpriteRenderer>();
        if (body == null || body.sprite == null)
        {
            enabled = false;
            return;
        }

        // 建独立的视觉层。所有形变只作用在它身上。
        GameObject vis = new GameObject("Visual");
        vis.transform.SetParent(transform, false);
        visualRoot = vis.transform;

        Sprite sprite = ArtStore.Player;
        hasArt = sprite != null;
        if (!hasArt) sprite = body.sprite;

        art = MakePart("Body", sprite, hasArt ? Color.white : BodyColor, BodyOrder, visualRoot);

        // 无图时保留程序化描边和眼睛；有图时不需要，画上去会和原画打架
        outline = MakePart("Outline", sprite, InkColor, BodyOrder - 1, visualRoot);
        outline.transform.localScale = Vector3.one * 1.34f;
        outline.enabled = !hasArt;

        eyeL = MakePart("EyeL", sprite, InkColor, BodyOrder + 1, visualRoot);
        eyeR = MakePart("EyeR", sprite, InkColor, BodyOrder + 1, visualRoot);
        eyeL.transform.localScale = Vector3.one * 0.15f;
        eyeR.transform.localScale = Vector3.one * 0.15f;
        eyeL.enabled = !hasArt;
        eyeR.enabled = !hasArt;
        UpdateEyes();

        // 根节点原精灵关掉，否则会和 visualRoot 上的重影
        body.enabled = false;

        if (PlayerHealth.HasInstance && PlayerHealth.Instance.gameObject == gameObject)
            PlayerHealth.Instance.SetBaseColor(hasArt ? Color.white : BodyColor);
    }

    private SpriteRenderer MakePart(string name, Sprite sprite, Color color, int order, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private void Update()
    {
        UpdateFacing();
        UpdateAnimation();
    }

    private void UpdateFacing()
    {
        // 鼠标不在 Game 视图内时 Input.mousePosition 可能是 NaN；
        // 直接喂给 ScreenToWorldPoint 会每帧抛异常把 Console 刷满红字。
        if (cam != null)
        {
            Vector3 mp = Input.mousePosition;
            if (!float.IsNaN(mp.x) && !float.IsNaN(mp.y) && mp.z >= 0f)
            {
                float mx = cam.ScreenToWorldPoint(mp).x;
                if (!float.IsNaN(mx)) faceDir = mx >= transform.position.x ? 1f : -1f;
            }
        }

        // 图片是朝右画的，朝左时翻转视觉层；不改根节点 scale，碰撞体不受影响
        if (art != null) art.flipX = faceDir < 0f;
        if (hasArt) UpdateEyes();
    }

    private void UpdateEyes()
    {
        if (eyeL == null || eyeR == null) return;

        const float spread = 0.13f;
        const float lift = 0.10f;
        float lean = 0.05f * faceDir;

        eyeL.transform.localPosition = new Vector3(-spread + lean, lift, 0f);
        eyeR.transform.localPosition = new Vector3(spread + lean, lift, 0f);
    }

    //  squash / stretch：全行业通用的"动画十二原则"之一。
    //  起跳拉长、落地压扁、待机呼吸——不需要新帧图，角色立刻从"贴纸"变成"有重量"。
    private void UpdateAnimation()
    {
        if (visualRoot == null || art == null) return;

        // 暂停 / 进场期间冻结动画，和游戏其他系统保持一致
        if (PauseMenu.IsPaused || (RunManager.HasInstance && RunManager.IsSpawning))
        {
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = baseScale;
            return;
        }

        float vy = motor != null ? motor.VerticalSpeed : 0f;
        float vx = motor != null ? motor.HorizontalSpeed : 0f;
        bool grounded = motor != null && motor.Grounded;
        float dt = Time.deltaTime;

        // —— 事件：给弹簧冲量 ——
        // 落地：压扁量按落地速度，冲量让弹簧"砸"下去再弹回
        if (grounded && !wasGrounded && vy < -2f)
        {
            float strength = Mathf.Clamp01(-vy / 14f);
            squashSpring.Kick(-(strength * 10f + 4f));
            HitParticles.Spawn((Vector2)transform.position + Vector2.down * 0.28f, DustColor, 5);
            FxRings.Land((Vector2)transform.position + Vector2.down * 0.31f, strength);
        }
        // 起跳：拉伸冲量
        if (!grounded && wasGrounded && vy > 0f)
        {
            stretchSpring.Kick(Mathf.Clamp01(vy / 14f) * 7f + 3f);
            FxRings.Jump((Vector2)transform.position + Vector2.down * 0.30f);
        }
        // 转身：倾斜弹簧给个反向冲量，有一个"急停再转"的过渡
        int faceSign = faceDir >= 0f ? 1 : -1;
        if (lastFaceSign != 0 && faceSign != lastFaceSign)
            leanSpring.Kick(-faceSign * 6f);
        lastFaceSign = faceSign;

        wasGrounded = grounded;
        hurtLock = Mathf.Max(0f, hurtLock - dt);

        // —— 弹簧推进（刚度/阻尼调到"轻微超调"，动作有弹性不僵硬）——
        squashSpring.Step(0f, 190f, 21f, dt);
        stretchSpring.Step(0f, 180f, 20f, dt);
        recoilSpring.Step(0f, 240f, 23f, dt);

        // —— 连续状态驱动（不是事件，每帧按实际状态算）——
        // 跑步周期：相位随速度推进，身体有节奏地上下起伏 + 左右摇摆
        float speed01 = Mathf.Clamp01(Mathf.Abs(vx) / 7f);
        runPhase += dt * (4f + speed01 * 9f);
        float bob = grounded ? Mathf.Sin(runPhase) * 0.05f * speed01 : 0f;
        float sway = grounded ? Mathf.Sin(runPhase * 0.5f) * 0.025f * speed01 : 0f;

        // 空中姿态连续映射：上升头上脚下（拉长），下落反过来——不再只在起跳瞬间变一次
        float airStretch = grounded ? 0f : Mathf.Clamp(vy / 16f, -0.5f, 0.5f);

        // 待机呼吸：站定时才呼吸，跑起来让位给跑步起伏
        float breath = (grounded && speed01 < 0.05f)
            ? Mathf.Sin(Time.time * 2.6f) * 0.028f
            : 0f;

        // 奔跑倾斜：弹簧追踪目标角度，转身冲量会叠加出过渡
        float targetTilt = Mathf.Clamp(-vx / 8f, -1f, 1f) * 7f;
        leanSpring.Step(targetTilt, 130f, 19f, dt);

        // —— 合成最终形变 ——
        float sq = squashSpring.value;
        float st = stretchSpring.value;
        float sy = 1f + breath + st * 0.30f + sq * 0.16f + airStretch * 0.22f + bob;
        float sx = 1f - breath * 0.5f - st * 0.16f - sq * 0.14f - airStretch * 0.12f - bob * 0.5f;
        visualRoot.localScale = new Vector3(baseScale.x * sx, baseScale.y * sy, 1f);

        // 后坐 + 跑步起伏的位置偏移（都在视觉层，不碰根节点碰撞）
        float rk = recoilSpring.value * 0.012f;
        visualRoot.localPosition = new Vector3(
            recoilDir.x * rk + sway,
            recoilDir.y * rk + bob * 0.4f,
            0f);

        visualRoot.localRotation = Quaternion.Euler(0f, 0f, leanSpring.value);
    }

    private void ApplyVisible(bool v)
    {
        visible = v;
        if (art != null) art.enabled = v;
        if (hasArt) return;             // 有美术时描边/五官本来就是关的，闪烁不能把它们打开
        if (outline != null) outline.enabled = v;
        if (eyeL != null) eyeL.enabled = v;
        if (eyeR != null) eyeR.enabled = v;
    }

    // PlayerHealth 复活时会 SetBaseColor，把它同步到真正的美术精灵上
    public static void SetTint(Color c)
    {
        if (instance != null && instance.art != null)
            instance.art.color = c;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
