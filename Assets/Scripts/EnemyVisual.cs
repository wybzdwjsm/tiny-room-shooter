using UnityEngine;

// 敌人视觉层：和 PlayerVisual 同样的思路——所有形变挂在独立的 Visual 子节点上，
// 根节点只保留碰撞体和移动逻辑，避免"看着被打到了、判定却没过"。
//
// 关键实现：不是新建 SpriteRenderer，而是把根节点上原有的那个 sr 组件连同它的 GameObject
// 一起"下沉"到 Visual 层级——但 GameObject 不能只移动组件，所以这里移动的是
// 承载精灵的子节点：把根节点 sr 的精灵信息复制到一个子节点，并让 Enemy 通过
// LiveSprite 属性动态取当前 sr，避免缓存指向被关掉的对象。
//
// 三种敌人各有自己的"活起来"方式：
//   Shooter / Turret：悬停呼吸（像扇翅膀 / 能量起伏），开火前微微膨胀
//   Charger：朝移动方向前倾，跑得越快压得越低（像冲刺的姿态）
public class EnemyVisual : MonoBehaviour
{
    private Enemy enemy;
    private Transform visualRoot;
    private SpriteRenderer visualSprite;
    private float phase;               // 每只错开，避免所有敌人同时呼吸
    private float lastX;
    private float afterImageTimer;

    public static void Attach(GameObject go)
    {
        if (go == null || go.GetComponent<EnemyVisual>() != null) return;
        go.AddComponent<EnemyVisual>();
    }

    // Enemy 用它来拿到"当前真正显示在屏幕上的精灵"，闪白 / 染色都作用于这个
    public SpriteRenderer LiveSprite { get { return visualSprite; } }

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        SpriteRenderer rootSr = GetComponent<SpriteRenderer>();
        if (rootSr == null) { enabled = false; return; }

        GameObject vis = new GameObject("Visual");
        vis.transform.SetParent(transform, false);
        visualRoot = vis.transform;

        visualSprite = vis.AddComponent<SpriteRenderer>();
        visualSprite.sprite = rootSr.sprite;
        visualSprite.color = rootSr.color;
        visualSprite.sortingOrder = rootSr.sortingOrder;

        // 根节点精灵关掉，避免和视觉层重影；碰撞体留在根节点不受影响
        rootSr.enabled = false;

        // 图片画死了朝向：冲锋图朝右、射手 / 炮台图朝左，按类型翻到面向玩家
        if (ArtStore.IsEnemySprite(visualSprite.sprite) && enemy != null)
            visualSprite.flipX = enemy.kind == Enemy.Kind.Charger;

        phase = transform.position.x * 1.7f + transform.position.y * 0.9f;
        lastX = transform.position.x;

        // 让 Enemy 从视觉层取精灵（EnemyVisual 的 Awake 一定晚于同物体上先添加的 Enemy，
        // 但 Init 在更晚的 RunManager.SpawnRoom 里调用，所以那里能拿到正确引用）
    }

    private void Update()
    {
        if (visualRoot == null || visualSprite == null) return;
        if (PauseMenu.IsPaused || (RunManager.HasInstance && RunManager.IsSpawning))
        {
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = Vector3.one;
            return;
        }

        float breath = Mathf.Sin(Time.time * 3.1f + phase) * 0.04f;

        // 开火前膨胀：颜色预警之外再加形变
        float charge = enemy != null ? enemy.TelegraphProgress : 0f;
        float puff = charge * 0.12f;

        float lean = 0f;
        if (enemy != null && enemy.kind == Enemy.Kind.Charger)
        {
            float dx = transform.position.x - lastX;
            lastX = transform.position.x;
            float speed = Time.deltaTime > 0f ? dx / Time.deltaTime : 0f;
            lean = Mathf.Clamp(-speed / 6f, -1f, 1f) * 10f;
            breath *= 0.35f;           // 冲锋时收敛呼吸，避免姿态发飘

            // 高速冲刺时留残影：每 0.07 秒一个，读起来是一条运动轨迹
            afterImageTimer -= Time.deltaTime;
            if (Mathf.Abs(speed) > 5f && afterImageTimer <= 0f)
            {
                afterImageTimer = 0.07f;
                FxRings.AfterImage(transform.position, visualSprite.sprite,
                    visualSprite.color, visualSprite.flipX);
            }
        }

        visualRoot.localScale = new Vector3(
            1f - breath * 0.6f + puff * 0.5f,
            1f + breath + puff,
            1f);
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, lean);
    }
}
