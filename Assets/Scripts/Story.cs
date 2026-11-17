// 全部叙事文本集中在这里：改剧情只要动这一个文件，不用翻 UI 代码。
//
// 一句话设定：世界被「潮」淹了，你是最后一个守灯人。
// 灯是唯一的光，「蚀」从黑暗里涌进来；每涨一次潮，它们就更凶一点。
// 每五次涨潮，「潮眼」会醒一次；打退四次潮眼才算守住这一夜。
// 灯灭之后，你还在挥拳 —— 那就是无尽模式。

public static class Story
{
    public const string GameTitle = "守灯人";
    public const string GameSubtitle = "THE LAMPLIGHTER · 一间房，一夜灯";

    // 开局前的序章，一行一句，读完按任意键点灯
    public static readonly string[] Brief =
    {
        "灯油只剩一夜。",
        "你是最后一个守灯人，城里的人早就走了。",
        "四次潮眼苏醒之前撑住 —— 或者，一直撑下去。"
    };

    public const string PlayStandard = "守夜 · 四夜潮眼（空格）";
    public const string PlayEndless = "灯灭之后 · 无尽（E）";

    public const string HintStandard = "四次涨潮、四次潮眼。撑到最后的灯，才算守住。";
    public const string HintEndless = "没有尽头。每五次涨潮，潮眼苏醒一次。";

    // 波次横幅：标准模式用"第几次涨潮"，无尽模式一直数下去
    public static string WaveBanner(int index)
    {
        switch (index)
        {
            case 0: return "第一次涨潮";
            case 1: return "第二次涨潮";
            case 2: return "第三次涨潮";
            case 3: return "第四次涨潮";
            case 4: return "第五次涨潮";
            default: return "第 " + (index + 1) + " 次涨潮";
        }
    }

    public const string BossWave = "潮眼苏醒";
    public const string BossDefeated = "潮眼退去 · 继续守夜";

    // 章节横幅：chapter 从 0 起，显示"第 N 夜"。标准模式每 5 波换一章。
    public static string ChapterBanner(int chapter)
    {
        switch (chapter)
        {
            case 0: return "第一夜 · 灯还亮着";
            case 1: return "第二夜 · 潮声近了";
            case 2: return "第三夜 · 影子变多";
            default: return "第四夜 · 最后一盏";
        }
    }

    public const string Victory = "灯，又亮了一夜。";
    public const string Death = "灯灭了。";

    // 无尽模式结算：{0} = 撑到的波数
    public const string EndlessDeath = "灯灭了 · 你撑到了第 {0} 次涨潮";

    // 选卡面板的标题：词条在这个世界观里是"往灯里添的东西"
    public const string ChoiceTitle = "往灯里添一点什么";
    public const string ChoiceHint = "按 1 / 2 / 3 也能选 · 选完回 1 点血量";
}
