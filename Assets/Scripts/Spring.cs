using UnityEngine;

// 轻量弹簧：给动画量（压扁 / 后坐 / 倾斜）提供惯性和回弹。
//
// 为什么不用 Mathf.MoveTowards：
//   MoveTowards 是匀速逼近，动作"到点就停"，看起来像机械；
//   弹簧有加速度和阻尼，落地压扁会轻轻弹过头再收回，才像有质量的东西。
//
// 用法：
//   spring.Kick(-8f);                       // 受击/开火时给它一个初速度
//   spring.Step(target, stiffness, damping, dt);   // 每帧推进
//
// damping 略小于 2*sqrt(stiffness) 时会轻微超调（有弹性）；等于时刚好回弹不振荡。
public struct Spring
{
    public float value;
    public float velocity;

    public void Kick(float impulse)
    {
        velocity += impulse;
    }

    public void Step(float target, float stiffness, float damping, float dt)
    {
        // 限帧：切标签页 / 卡顿时 dt 可能很大，弹簧会数值爆炸
        if (dt > 0.04f) dt = 0.04f;
        float accel = (target - value) * stiffness - velocity * damping;
        velocity += accel * dt;
        value += velocity * dt;
    }
}
