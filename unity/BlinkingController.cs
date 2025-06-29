// BlinkingController.cs

using UnityEngine;
using System.Collections;

/// <summary>
/// 控制挂载的GameObject进行持续闪烁。
/// 闪烁通过开关MeshRenderer实现，性能较好。
/// </summary>
public class BlinkingController : MonoBehaviour
{
    [Header("闪烁参数")]
    [Tooltip("每秒闪烁的次数 (单位: 赫兹Hz)")]
    public float frequency = 10f;

    [Tooltip("占空比，即在一个周期内，亮灯时间所占的比例 (0到1之间)")]
    [Range(0.01f, 1.0f)]
    public float dutyCycle = 0.5f;

    // 私有变量，用于缓存组件和协程引用
    private MeshRenderer meshRenderer;
    private Coroutine blinkingCoroutine;

    // 在对象被加载时调用，用于初始化
    void Awake()
    {
        // 获取并缓存MeshRenderer组件，比每次在循环中获取更高效
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogError("BlinkingController脚本需要一个MeshRenderer组件才能工作！", this.gameObject);
            this.enabled = false; 
        }
    }

    // 当此脚本或其所在的GameObject被激活时调用
    void OnEnable()
    {
        // 确保一开始是可见的
        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }

        // 启动闪烁协程，并保存它的引用，方便之后停止
        blinkingCoroutine = StartCoroutine(BlinkRoutine());
    }

    // 当此脚本或其所在的GameObject被禁用或销毁时调用
    void OnDisable()
    {
        // 如果协程正在运行，就停止它
        if (blinkingCoroutine != null)
        {
            StopCoroutine(blinkingCoroutine);
        }

        // 为确保对象在下次启用时是可见的，最好在禁用时将其恢复为可见状态
        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }
    }

    /// <summary>
    /// 实现闪烁逻辑的协程
    /// </summary>
    private IEnumerator BlinkRoutine()
    {
        // 无限循环，除非被OnDisable中的StopCoroutine停止
        while (true)
        {
            // 安全检查，防止频率为0或负数导致除以0的错误
            if (frequency <= 0)
            {
                // 如果频率无效，确保物体可见并退出协程
                meshRenderer.enabled = true;
                yield break; // 退出协程
            }

            // 根据频率和占空比计算亮灯和灭灯的持续时间
            float cycleDuration = 1.0f / frequency;
            float onDuration = cycleDuration * dutyCycle;
            float offDuration = cycleDuration * (1.0f - dutyCycle);

            // --- 一个闪烁周期 ---

            // 1. 亮灯（确保渲染器开启）
            meshRenderer.enabled = true;
            // 2. 等待“亮灯时长”
            yield return new WaitForSeconds(onDuration);

            // 3. 灭灯（关闭渲染器）
            meshRenderer.enabled = false;
            // 4. 等待“灭灯时长”
            yield return new WaitForSeconds(offDuration);
        }
    }
}