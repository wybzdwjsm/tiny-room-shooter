using System;

// 注意用 struct：C# 的 class 赋值是引用拷贝（C++ 直觉容易误判），
// 用值类型才能保证「基础数值」不会被词条叠加污染，重开一局也能干净重置。
[Serializable]
public struct RunStats
{
    public float fireRate;        // 发/秒
    public int bulletCount;       // 每次开火的弹丸数
    public float bulletDamage;      // 每发伤害（0.25 = 原来的 1/4，命中 4 发才扣 1 点血）
    public float moveSpeedMul;    // 移速倍率
    public int maxHpBonus;        // 最大生命加成
    public int enemyHpBonus;      // 敌人血量加成（负值 = 更容易）

    public static RunStats Base()
    {
        return new RunStats
        {
            fireRate = 6f,
            bulletCount = 1,
            bulletDamage = 0.25f,
            moveSpeedMul = 1f,
            maxHpBonus = 0,
            enemyHpBonus = 0
        };
    }
}
