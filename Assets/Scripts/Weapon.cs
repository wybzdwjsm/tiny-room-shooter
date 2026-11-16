using UnityEngine;

// 持有子弹对象池：Awake 时一次性创建 poolSize 个子弹并全部隐藏，开火时取一个复用。
// 这是 W2 最值得写进复盘的技术点（避免每帧 Instantiate/Destroy 造成的 GC 尖峰）。
public class Weapon : MonoBehaviour
{
    // 池容量 48：高射速 build（射速 30 × 5 弹丸）在子弹撞墙前最多同时存在 ~70 发，
    // 原来 16 个会在满 build 时静默丢子弹。48 个对象只在开局创建一次，成本可以忽略。
    [SerializeField] private int poolSize = 48;
    [SerializeField] private float fireRate = 6f;
    [SerializeField] private float muzzleOffset = 0.45f;   // 角色缩小后枪口也往里收

    private Bullet[] pool;
    private float cooldown;
    private Camera cam;

    private void Awake()
    {
        cam = Camera.main;

        // 池容器不能挂在玩家下面，否则子弹会跟着玩家移动
        GameObject poolRoot = new GameObject("BulletPool");
        pool = new Bullet[poolSize];

        Sprite bulletSprite = MakeBulletSprite();
        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = new GameObject("Bullet_" + i);
            go.transform.SetParent(poolRoot.transform, false);
            go.transform.localScale = new Vector3(0.19f, 0.19f, 1f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = bulletSprite;
            sr.color = new Color(1f, 0.88f, 0.3f, 1f);
            sr.sortingOrder = 10;
            pool[i] = go.AddComponent<Bullet>();
            go.SetActive(false);
        }
    }

    private void Update()
    {
        // 标题 / 结算等模态界面期间不响应开火（三选一仍可开火，和"可以走动"保持一致）
        if (RunManager.HasInstance && !RunManager.IsControllable) return;
        // 暂停时 deltaTime = 0 会让冷却停在原地，必须单独拦掉输入
        if (PauseMenu.IsPaused) return;
        // 敌人进场期间整个程序冻结，禁止开火（否则子弹会停在枪口）
        if (RunManager.IsSpawning) return;
        cooldown -= Time.deltaTime;
        if ((Input.GetKey(KeyCode.J) || Input.GetMouseButton(0)) && cooldown <= 0f)
        {
            // W3：射速与子弹数改从 RunStats 读，词条只改数据、不改战斗代码
            float rate = RunManager.HasInstance ? RunManager.Current.fireRate : fireRate;
            cooldown = 1f / Mathf.Max(0.5f, rate);

            int count = RunManager.HasInstance ? RunManager.Current.bulletCount : 1;
            Vector2 aim = AimDirection();
            GameAudio.Play(GameAudio.Sfx.Shoot);   // 一次开火只播一声，散射不叠加
            if (RunManager.HasInstance) RunManager.Instance.NotifyShot(count);
            for (int i = 0; i < count; i++)
                Fire(Rotate(aim, (i - (count - 1) * 0.5f) * 0.12f));
        }
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private Vector2 AimDirection()
    {
        if (cam == null) return Vector2.right;

        // 鼠标移出 Game 视图时 Input.mousePosition 可能变成 NaN，
        // 直接传给 ScreenToWorldPoint 会每帧抛 "Screen position out of view frustum"。
        // 这种时候保持上一次的有效朝向（这里是右），不要刷异常也不要乱开火。
        Vector3 mp = Input.mousePosition;
        if (float.IsNaN(mp.x) || float.IsNaN(mp.y) || mp.z < 0f) return Vector2.right;

        Vector3 world = cam.ScreenToWorldPoint(mp);
        if (float.IsNaN(world.x) || float.IsNaN(world.y)) return Vector2.right;

        Vector2 dir = (Vector2)(world - transform.position);
        return dir.sqrMagnitude < 0.0001f ? Vector2.right : dir.normalized;
    }

    private void Fire(Vector2 dir)
    {
        foreach (Bullet bullet in pool)
        {
            if (!bullet.gameObject.activeSelf)
            {
                bullet.Init(this, (Vector2)transform.position + dir * muzzleOffset, dir);
                return;
            }
        }
        // 池耗尽时静默跳过，不动态扩容：保证运行时零分配
    }

    public void Recycle(Bullet bullet)
    {
        bullet.gameObject.SetActive(false);
    }

    private static Sprite MakeBulletSprite()
    {
        const int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        // PPU = 8，8x8 像素 = 1 单位；配合 scale 0.25 得到 0.25 单位的子弹
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
