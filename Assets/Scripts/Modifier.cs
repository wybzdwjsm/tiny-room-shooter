using System;

public enum StatTarget { FireRate, BulletCount, BulletDamage, MoveSpeed, MaxHp, EnemyHp }
public enum ModifierOp { Add, Multiply }

// 词条只描述「改哪个字段、怎么改、最多叠几层」，不含任何战斗逻辑。
// 加新词条 = 加一条数据，战斗代码一行都不用动。
// dual = 有第二效果，用来表达"取舍"：拿到强化的同时付出代价。
[Serializable]
public class Modifier
{
    public string id;
    public string displayName;
    public StatTarget target;
    public ModifierOp op;
    public float value;

    public bool dual;
    public StatTarget target2;
    public ModifierOp op2;
    public float value2;

    public int maxStacks = 3;
}

public static class ModifierTable
{
    // 20 条：11 条纯强化 + 9 条带代价的取舍。displayName 里直接写清代价，
    // 玩家不用猜"这卡到底亏不亏"。
    public static readonly Modifier[] All =
    {
        // ---- 纯强化 ----
        new Modifier { id = "fr_mul",   displayName = "射速 ×1.2",                target = StatTarget.FireRate,     op = ModifierOp.Multiply, value = 1.2f, maxStacks = 3 },
        new Modifier { id = "fr_add",   displayName = "射速 +2 发/秒",             target = StatTarget.FireRate,     op = ModifierOp.Add,      value = 2f,   maxStacks = 3 },
        new Modifier { id = "fr_big",   displayName = "射速 ×1.6",                target = StatTarget.FireRate,     op = ModifierOp.Multiply, value = 1.6f, maxStacks = 1 },
        new Modifier { id = "bc_1",     displayName = "子弹 +1（散射）",           target = StatTarget.BulletCount,  op = ModifierOp.Add,      value = 1f,   maxStacks = 2 },
        new Modifier { id = "bc_big",   displayName = "子弹 +2（大散射）",         target = StatTarget.BulletCount,  op = ModifierOp.Add,      value = 2f,   maxStacks = 1 },
        new Modifier { id = "dmg_1",    displayName = "伤害 +1",                   target = StatTarget.BulletDamage, op = ModifierOp.Add,      value = 1f,   maxStacks = 2 },
        new Modifier { id = "dmg_2",    displayName = "伤害 +2",                   target = StatTarget.BulletDamage, op = ModifierOp.Add,      value = 2f,   maxStacks = 1 },
        new Modifier { id = "ehp_d1",   displayName = "敌人血量 -1",               target = StatTarget.EnemyHp,      op = ModifierOp.Add,      value = -1f,  maxStacks = 2 },
        new Modifier { id = "ehp_d2",   displayName = "敌人血量 -2",               target = StatTarget.EnemyHp,      op = ModifierOp.Add,      value = -2f,  maxStacks = 1 },
        new Modifier { id = "spd_up",   displayName = "移速 +15%",                 target = StatTarget.MoveSpeed,    op = ModifierOp.Multiply, value = 1.15f, maxStacks = 2 },
        new Modifier { id = "hp_1",     displayName = "生命上限 +1",               target = StatTarget.MaxHp,        op = ModifierOp.Add,      value = 1f,   maxStacks = 2 },

        // ---- 有代价的取舍 ----
        new Modifier { id = "dmg_fr",   displayName = "伤害 +1（射速 -15%）",       target = StatTarget.BulletDamage, op = ModifierOp.Add,
                       dual = true, target2 = StatTarget.FireRate,     op2 = ModifierOp.Multiply, value2 = 0.85f, maxStacks = 1 },
        new Modifier { id = "fr_dmg",   displayName = "射速 ×1.35（伤害 -1）",      target = StatTarget.FireRate,     op = ModifierOp.Multiply, value = 1.35f,
                       dual = true, target2 = StatTarget.BulletDamage, op2 = ModifierOp.Add,      value2 = -1f,   maxStacks = 1 },
        new Modifier { id = "bc_fr",    displayName = "子弹 +1（射速 -15%）",       target = StatTarget.BulletCount,  op = ModifierOp.Add,      value = 1f,
                       dual = true, target2 = StatTarget.FireRate,     op2 = ModifierOp.Multiply, value2 = 0.85f, maxStacks = 1 },
        new Modifier { id = "spd_hp",   displayName = "移速 +25%（生命上限 -1）",   target = StatTarget.MoveSpeed,    op = ModifierOp.Multiply, value = 1.25f,
                       dual = true, target2 = StatTarget.MaxHp,        op2 = ModifierOp.Add,      value2 = -1f,   maxStacks = 1 },
        new Modifier { id = "dmg_slow", displayName = "伤害 +2（射速 ×0.7）",       target = StatTarget.BulletDamage, op = ModifierOp.Add,      value = 2f,
                       dual = true, target2 = StatTarget.FireRate,     op2 = ModifierOp.Multiply, value2 = 0.7f,  maxStacks = 1 },
        new Modifier { id = "hp_dmg",   displayName = "生命上限 +2（伤害 -1）",     target = StatTarget.MaxHp,        op = ModifierOp.Add,      value = 2f,
                       dual = true, target2 = StatTarget.BulletDamage, op2 = ModifierOp.Add,      value2 = -1f,   maxStacks = 1 },
        new Modifier { id = "ehp_hp",   displayName = "敌人血量 -1（生命上限 -1）", target = StatTarget.EnemyHp,      op = ModifierOp.Add,      value = -1f,
                       dual = true, target2 = StatTarget.MaxHp,        op2 = ModifierOp.Add,      value2 = -1f,   maxStacks = 1 },
        new Modifier { id = "spd_dmg",  displayName = "移速 +20%（伤害 -1）",       target = StatTarget.MoveSpeed,    op = ModifierOp.Multiply, value = 1.2f,
                       dual = true, target2 = StatTarget.BulletDamage, op2 = ModifierOp.Add,      value2 = -1f,   maxStacks = 1 },
        new Modifier { id = "dmg_ehp",  displayName = "伤害 +1（敌人血量 +1）",     target = StatTarget.BulletDamage, op = ModifierOp.Add,      value = 1f,
                       dual = true, target2 = StatTarget.EnemyHp,      op2 = ModifierOp.Add,      value2 = 1f,    maxStacks = 1 },
    };
}
