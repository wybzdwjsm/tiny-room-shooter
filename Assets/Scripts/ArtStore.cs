using UnityEngine;

// 运行时加载 Resources/Art 下的角色美术，转成 Sprite 供玩家 / 敌人 / Boss 使用。
//
// 为什么不直接把图片设成 Sprite 类型再拖进场景：
//   1) 现有代码已经用 transform.localScale 统一控制体型（CharacterScale=0.62、BossScale=1.9），
//      这里用 PPU=1024 让一张 1024px 的图正好等于 1 个世界单位，原有缩放数值全部不用改；
//      生成图里角色大约只占画布中间 70%，用 ART_FILL 补偿，让可见角色和原来的方块差不多大；
//   2) Resources.Load 在 WebGL 下可用，不依赖场景里手动拖引用，旧场景也能直接生效；
//   3) 加载失败时返回 null，各调用点保留原来的方块兜底，不会因为缺图白屏。
public static class ArtStore
{
    private const float PPU = 1024f;
    // 生成图角色约占画布 70%，放大 1.35 倍让可见主体和原方块尺寸接近（碰撞体不变）
    private const float ART_FILL = 1.35f;

    private static Sprite player, shooter, charger, turret, boss;
    // 4 种 Boss 的专属贴图（按 Boss.Kind 顺序：TideEye/Spore/Spear/Abyss）
    private static Sprite[] bossVariants = new Sprite[4];
    private static bool tried;

    public static Sprite Player { get { Ensure(); return player; } }
    public static Sprite Shooter { get { Ensure(); return shooter; } }
    public static Sprite Charger { get { Ensure(); return charger; } }
    public static Sprite Turret { get { Ensure(); return turret; } }
    public static Sprite Boss { get { Ensure(); return boss; } }

    // 按 Boss 类型取贴图。
    // 优先用 Resources 里的外部专属图；若加载失败（Unity 导入问题），
    // 退回 BossArt 程序化生成的 4 种贴图——保证 4 个 Boss 外观一定不同，绝不再出现纯色方块。
    public static Sprite BossFor(int kindIndex)
    {
        Ensure();
        if (kindIndex >= 0 && kindIndex < bossVariants.Length && bossVariants[kindIndex] != null)
            return bossVariants[kindIndex];
        return BossArt.Get(kindIndex);
    }

    // 是否专属 Boss 贴图（外部变体 或 程序化生成）——用于决定显示原色还是叠 tint
    public static bool IsVariantBossSprite(Sprite s)
    {
        if (s == null) return false;
        for (int i = 0; i < 4; i++)
            if (s == BossArt.Get(i)) return true;
        for (int i = 0; i < bossVariants.Length; i++)
            if (bossVariants[i] != null && s == bossVariants[i]) return true;
        return false;
    }

    // 判断某个 Sprite 是不是我们加载的 Boss 美术（原版或 4 种变体），用于 Boss 的有图/无图分支
    public static bool IsBossSprite(Sprite s)
    {
        Ensure();
        if (s == null) return false;
        if (s == boss) return true;
        for (int i = 0; i < bossVariants.Length; i++)
            if (s == bossVariants[i]) return true;
        return false;
    }

    // 判断某个 SpriteRenderer 用的是不是我们加载的美术（而不是兜底的纯色方块）
    public static bool IsEnemySprite(Sprite s)
    {
        Ensure();
        return s != null && (s == shooter || s == charger || s == turret || s == boss);
    }

    private static void Ensure()
    {
        if (tried) return;
        tried = true;

        player = Load("player");
        shooter = Load("shooter");
        charger = Load("charger");
        turret = Load("turret");
        boss = Load("boss");                       // 原版兜底

        // 外部专属图：尝试加载但静默失败（加载不了就用 BossArt 程序化贴图）。
        // 之前反复出现"未找到"警告是因为 Unity 导入 meta 的问题，现在有兜底就不再吵用户。
        bossVariants[0] = LoadQuiet("boss_tideeye");
        bossVariants[1] = LoadQuiet("boss_spore");
        bossVariants[2] = LoadQuiet("boss_spear");
        bossVariants[3] = LoadQuiet("boss_abyss");
    }

    // 静默版加载：失败只返回 null，不打印警告
    private static Sprite LoadQuiet(string name)
    {
        Texture2D tex = Resources.Load<Texture2D>("Art/" + name);
        if (tex == null) return null;
        float ppu = (tex.width > 0 ? tex.width / 1f : PPU) / ART_FILL;
        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), ppu, 0u, SpriteMeshType.FullRect);
        s.name = "art_" + name;
        return s;
    }

    private static Sprite Load(string name)
    {
        Texture2D tex = Resources.Load<Texture2D>("Art/" + name);
        if (tex == null)
        {
            Debug.LogWarning("[ArtStore] 未找到 Resources/Art/" + name + "，该角色退回纯色方块");
            return null;
        }

        // 一张 1024px 图 = 1 个世界单位，保持所有既有缩放数值的含义不变
        float ppu = (tex.width > 0 ? tex.width / 1f : PPU) / ART_FILL;
        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), ppu, 0u, SpriteMeshType.FullRect);
        s.name = "art_" + name;
        return s;
    }
}
