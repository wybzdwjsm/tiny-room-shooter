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

    private bool hasArt;
    private float faceDir = 1f;
    private float squash;              // 落地压扁量，0~1，随时间回弹
    private float stretch;             // 起跳拉伸量
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

        // 落地瞬间：压扁 + 一簇扬尘
        if (grounded && !wasGrounded && vy < -2f)
        {
            squash = Mathf.Clamp01(-vy / 14f) * 0.8f + 0.2f;
            HitParticles.Spawn((Vector2)transform.position + Vector2.down * 0.28f, DustColor, 5);
        }
        // 起跳瞬间：拉长
        if (!grounded && wasGrounded && vy > 0f)
            stretch = Mathf.Clamp01(vy / 14f) * 0.7f + 0.2f;

        wasGrounded = grounded;

        squash = Mathf.MoveTowards(squash, 0f, Time.deltaTime * 4.5f);
        stretch = Mathf.MoveTowards(stretch, 0f, Time.deltaTime * 4.5f);

        // 待机呼吸（只在地面上、速度很小时）
        float breath = (grounded && Mathf.Abs(vx) < 0.2f)
            ? Mathf.Sin(Time.time * 2.6f) * 0.025f
            : 0f;

        float sy = 1f + breath + stretch * 0.30f - squash * 0.28f;
        float sx = 1f - breath * 0.5f - stretch * 0.16f + squash * 0.24f;
        visualRoot.localScale = new Vector3(baseScale.x * sx, baseScale.y * sy, 1f);

        // 奔跑倾斜：速度越大越前倾，最多 8 度
        float tilt = Mathf.Clamp(-vx / 8f, -1f, 1f) * 8f;
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, tilt);
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
