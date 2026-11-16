using UnityEngine;

// 玩家设置：震屏强度、音量、历史最佳，全部存 PlayerPrefs，重启游戏后保留。
// 震屏是最容易被抱怨的手感项，所以做成玩家可调的四档，默认「弱」。
public static class GameSettings
{
    private const string KeyShake = "gs.shakeIndex";
    private const string KeyVolume = "gs.volume";
    private const string KeyBestKill = "gs.bestKills";
    private const string KeyBestTime = "gs.bestClearTime";
    private const string KeyBestWave = "gs.bestWave";

    public static readonly string[] ShakeNames = { "关", "弱", "标准", "强" };
    private static readonly float[] ShakeValues = { 0f, 0.35f, 0.7f, 1f };

    public static int ShakeIndex { get; private set; }
    public static float ShakeScale { get { return ShakeValues[Mathf.Clamp(ShakeIndex, 0, ShakeValues.Length - 1)]; } }
    public static float Volume { get; private set; }
    public static int BestKills { get; private set; }
    public static float BestClearTime { get; private set; }
    public static int BestWave { get; private set; }
    public static bool HasRecord { get { return BestKills > 0 || BestClearTime > 0f || BestWave > 0; } }

    // 上一局是否刷新了记录，结算页用来显示「新纪录」
    public static bool LastRunSetRecord { get; private set; }

    static GameSettings()
    {
        // 默认「弱」：先用最保守的震动开局，觉得不够再到暂停菜单里往上调
        ShakeIndex = Mathf.Clamp(PlayerPrefs.GetInt(KeyShake, 1), 0, ShakeValues.Length - 1);
        Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 0.7f));
        BestKills = PlayerPrefs.GetInt(KeyBestKill, 0);
        BestClearTime = PlayerPrefs.GetFloat(KeyBestTime, 0f);
        BestWave = PlayerPrefs.GetInt(KeyBestWave, 0);
        AudioListener.volume = Volume;
    }

    public static void CycleShake()
    {
        ShakeIndex = (ShakeIndex + 1) % ShakeValues.Length;
        PlayerPrefs.SetInt(KeyShake, ShakeIndex);
        PlayerPrefs.Save();
    }

    public static void ChangeVolume(float delta)
    {
        SetVolume(Volume + delta);
    }

    public static void SetVolume(float v)
    {
        Volume = Mathf.Clamp01(Mathf.Round(v * 20f) / 20f);   // 按 5% 一档
        PlayerPrefs.SetFloat(KeyVolume, Volume);
        PlayerPrefs.Save();
        AudioListener.volume = Volume;
        GameAudio.ApplyVolume();
    }

    // 结算时提交战绩：最高击杀数 + 最快通关时间 + 最远波数（无尽模式的主指标）
    public static void SubmitRun(int kills, float clearTime, int wave = 0)
    {
        bool dirty = false;

        if (kills > BestKills)
        {
            BestKills = kills;
            PlayerPrefs.SetInt(KeyBestKill, kills);
            dirty = true;
        }
        if (clearTime > 0f && (BestClearTime <= 0f || clearTime < BestClearTime))
        {
            BestClearTime = clearTime;
            PlayerPrefs.SetFloat(KeyBestTime, clearTime);
            dirty = true;
        }
        if (wave > BestWave)
        {
            BestWave = wave;
            PlayerPrefs.SetInt(KeyBestWave, wave);
            dirty = true;
        }
        if (dirty) PlayerPrefs.Save();
        LastRunSetRecord = dirty;
    }

    public static string RecordText()
    {
        if (!HasRecord) return "尚无记录 —— 第一局就是你的基线";

        string s = "最佳：";
        if (BestWave > 0) s += "第 " + BestWave + " 波 · ";
        s += "击杀 " + BestKills;
        if (BestClearTime > 0f) s += " · 最快通关 " + BestClearTime.ToString("0.0") + " 秒";
        return s;
    }
}
