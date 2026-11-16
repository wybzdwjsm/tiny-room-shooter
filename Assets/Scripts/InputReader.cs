using UnityEngine;

// 保证输入采集早于 PlayerMotor.Update，避免晚一帧
[DefaultExecutionOrder(-100)]
public class InputReader : MonoBehaviour
{
    public float MoveX { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool JumpReleased { get; private set; }

    private void Update()
    {
        // 旧 Input Manager：项目需把 Active Input Handling 设为 Both
        // GetAxisRaw 返回 -1/0/1，无平滑过渡，响应更跟手
        MoveX = Input.GetAxisRaw("Horizontal");
        JumpPressed = Input.GetKeyDown(KeyCode.Space);
        JumpReleased = Input.GetKeyUp(KeyCode.Space);
    }
}
