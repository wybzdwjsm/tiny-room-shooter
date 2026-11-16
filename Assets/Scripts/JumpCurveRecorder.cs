using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// W4：把每次跳跃的高度-时间曲线导出 CSV，用来做参数 A/B 对比。
// 面试时说的是「我量了曲线、对比了前后数据」，而不是「我觉得这样更爽」。
// 操作：按 Space 跳（自动记录 1.2 秒曲线），跳够几次后按 K 导出，Console 会打印路径。
public class JumpCurveRecorder : MonoBehaviour
{
    [SerializeField] private float sampleInterval = 0.02f;
    [SerializeField] private float window = 1.2f;
    [SerializeField] private KeyCode exportKey = KeyCode.K;

    private readonly List<string> rows = new List<string>();
    private int jumpIndex;
    private float elapsed;
    private float sampleTimer;
    private float baseY;
    private bool recording;

    private void Awake()
    {
        rows.Add("jump_index,time_s,height_u");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpIndex++;
            elapsed = 0f;
            sampleTimer = 0f;
            baseY = transform.position.y;
            recording = true;
        }

        if (recording)
        {
            elapsed += Time.deltaTime;
            sampleTimer += Time.deltaTime;
            if (sampleTimer >= sampleInterval)
            {
                sampleTimer -= sampleInterval;
                rows.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2:F3}",
                    jumpIndex, elapsed, transform.position.y - baseY));
                if (rows.Count > 6000) rows.RemoveRange(1, 1000);   // 防止长时间运行无限增长
            }
            if (elapsed >= window) recording = false;
        }

        if (Input.GetKeyDown(exportKey)) Export();
    }

    private void Export()
    {
#if UNITY_EDITOR
        string dir = Path.GetDirectoryName(Application.dataPath);   // 项目根目录，方便找
#else
        string dir = Application.persistentDataPath;
#endif
        string path = Path.Combine(dir, "jump_curve_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        File.WriteAllLines(path, rows.ToArray());
        Debug.Log("[JumpCurve] 已导出 " + rows.Count + " 行 -> " + path);
    }
}
