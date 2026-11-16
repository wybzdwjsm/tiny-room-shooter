using System.Collections;
using UnityEngine;

// 打击反馈分三级：普通命中只轻抖一下（无停顿），击杀 / 受伤才有极短的命中停顿。
// 上一版的问题：每发子弹命中都触发 0.12 单位随机跳变 + 全局 timeScale = 0，
// 6 发/秒时等于每秒十几次地震加十几次卡顿，所以整体重做：
//   1) 幅度按「命中 < 击杀 < 受伤」分级，普通命中从 0.12 降到 0.020；
//   2) 普通命中不再触发命中停顿（那是连射卡顿的真凶）；
//   3) 0.05 秒内的连续命中合并取最大值，绝不线性叠加；
//   4) 抖动幅度按剩余时间二次衰减，尾巴更顺；
//   5) 整体乘以 GameSettings.ShakeScale，玩家可在暂停菜单里调到「关」。
public class HitFeedback : MonoBehaviour
{
    public enum Feel { Hit, Kill, Hurt }

    public static HitFeedback Instance { get; private set; }

    // 每级参数：震幅（世界单位） / 时长（秒）
    // 注意：v0.13 起这里彻底不碰 Time.timeScale。
    // 之前的「命中停顿 = timeScale 0.05」只要有一次没还原成功，全局时间就会永久卡在 5% 速度，
    // 画面几乎静止 —— 和暂停完全无法区分，而且查不到（因为不是 0）。现在只用震屏表达重量。
    private const float HitAmp = 0.020f; private const float HitDur = 0.055f;
    private const float KillAmp = 0.055f; private const float KillDur = 0.13f;
    private const float HurtAmp = 0.085f; private const float HurtDur = 0.19f;
    private const float MergeWindow = 0.05f;   // 连射合并窗口

    private Vector3 restPos;
    private float shakeAmp;
    private float shakeDur;
    private float shakeUntil;

    private void Awake()
    {
        Instance = this;
        restPos = transform.position;
    }

    public static void Play(Feel feel = Feel.Hit)
    {
        if (Instance != null) Instance.Request(feel);
    }

    private void Request(Feel feel)
    {
        float amp, dur;
        switch (feel)
        {
            case Feel.Kill: amp = KillAmp; dur = KillDur; break;
            case Feel.Hurt: amp = HurtAmp; dur = HurtDur; break;
            default: amp = HitAmp; dur = HitDur; break;
        }

        float scaled = amp * GameSettings.ShakeScale;
        if (scaled <= 0.0001f) return;   // 设置为「关」时彻底不抖

        float now = Time.unscaledTime;

        if (now < shakeUntil + MergeWindow && shakeAmp > 0.0001f)
        {
            // 合并：把当前抖动衰减到此刻的剩余幅度，再与本次取较大值，不累加
            float remain = Mathf.Clamp01((shakeUntil - now) / Mathf.Max(0.0001f, shakeDur));
            shakeAmp = Mathf.Max(shakeAmp * remain * remain, scaled);
            shakeDur = dur;
            shakeUntil = now + dur;
        }
        else
        {
            restPos = transform.position;   // 只在起抖时记录静止位置，避免累积漂移
            shakeAmp = scaled;
            shakeDur = dur;
            shakeUntil = now + dur;
        }
    }

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (shakeAmp <= 0.0001f)
        {
            restPos = transform.position;   // 未抖动时跟随相机真实位置（兼容以后加相机跟随）
            return;
        }

        if (now >= shakeUntil)
        {
            shakeAmp = 0f;
            transform.position = restPos;
            return;
        }

        float k = (shakeUntil - now) / Mathf.Max(0.0001f, shakeDur);   // 1 -> 0
        float a = shakeAmp * k * k;                                     // 二次衰减
        transform.position = restPos + (Vector3)(Random.insideUnitCircle * a);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
