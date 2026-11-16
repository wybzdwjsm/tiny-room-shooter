using UnityEngine;

// 子弹不挂 Rigidbody2D：手动推进 + OverlapCircle 判定，避开不同引擎分支的 velocity API 差异。
// 子弹由 Weapon 的对象池复用，运行时不 Instantiate / Destroy，战斗高峰期不会产生 GC 尖峰。
public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 22f;
    [SerializeField] private float radius = 0.14f;   // 与缩小的角色/子弹视觉尺寸匹配
    [SerializeField] private float life = 2f;

    private Weapon owner;
    private Vector2 dir;
    private float timer;
    private int damage = 1;

    public void Init(Weapon weapon, Vector2 position, Vector2 direction)
    {
        owner = weapon;
        dir = direction.normalized;
        transform.position = position;
        timer = life;
        damage = RunManager.HasInstance ? RunManager.Current.bulletDamage : 1;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        // 进场 / 暂停期间冻结飞行（不碰 timeScale，否则编辑器会显示暂停）
        if (PauseMenu.IsPaused || RunManager.IsSpawning) return;

        transform.position += (Vector3)(dir * speed * Time.deltaTime);
        timer -= Time.deltaTime;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);

        // 第一遍只认角色：同一帧里如果同时压到平台和贴边的敌人，要让敌人吃伤害，
        // 否则玩家会觉得"明明打到了却没反应"。
        foreach (Collider2D hit in hits)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null && enemy.isActiveAndEnabled)
            {
                enemy.Hit(dir, damage);
                OnHit();
                return;
            }

            Boss boss = hit.GetComponent<Boss>();
            if (boss != null && boss.Alive)
            {
                boss.Hit(dir, damage);
                OnHit();
                return;
            }
        }

        // 第二遍才认地形：平台和墙会挡住子弹，留一小簇冷色火花说明"打在了硬东西上"
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponent<Solid>() != null)
            {
                HitParticles.Spawn(transform.position, new Color(0.78f, 0.84f, 1f, 1f), 3);
                owner.Recycle(this);
                return;
            }
        }

        if (timer <= 0f) owner.Recycle(this);
    }

    private void OnHit()
    {
        GameAudio.Play(GameAudio.Sfx.Hit);
        HitParticles.Spawn(transform.position, new Color(1f, 0.85f, 0.35f, 1f), 4);
        if (RunManager.HasInstance) RunManager.Instance.NotifyHit();
        owner.Recycle(this);
    }
}
