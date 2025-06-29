using UnityEngine;
using UnityEngine.Events;
using Microsoft.MixedReality.Toolkit; // 来获取眼动数据

public class GazeDwellButton_Simplified : MonoBehaviour
{
    [Header("UI 组件")]
    [Tooltip("用于显示填充进度的Image组件，可以为null")]
    public UnityEngine.UI.Image fillImage;

    [Header("注视参数")]
    [Tooltip("需要注视多久才能触发按钮（秒）")]
    public float dwellTime = 2.0f;

    [Header("触发事件")]
    [Tooltip("当注视完成后要触发的事件")]
    public UnityEvent OnDwellComplete = new UnityEvent();
    private float _currentDwellTime = 0f;
    private bool _isGazing = false;

    // 状态锁，记录本次注视是否已触发过事件
    private bool _actionTriggered = false;

    void Update()
    {
        // 1. 获取眼动数据
        var eyeGazeProvider = CoreServices.InputSystem?.EyeGazeProvider;
        if (eyeGazeProvider == null || !eyeGazeProvider.IsEyeTrackingEnabled)
        {
            if (_isGazing) ResetAll(); // 如果眼动失效，确保停止
            return;
        }

        // 2. 发射射线检测
        bool isHittingMe = false;
        Ray ray = new Ray(eyeGazeProvider.GazeOrigin, eyeGazeProvider.GazeDirection);
        if (Physics.Raycast(ray, out RaycastHit hit, 20f))
        {
            if (hit.collider.gameObject == this.gameObject)
            {
                isHittingMe = true;
            }
        }

        // 3. 核心状态机逻辑
        if (isHittingMe)
        {
            // --- 视线在我身上 ---
            if (!_isGazing)
            {
                // 如果是刚从“非注视”变为“注视”状态
                _isGazing = true;
                // 在新的注视开始时，重置“动作已触发”的锁
                _actionTriggered = false;
                _currentDwellTime = 0f; // 确保每次新的注视都从0开始计时
                Debug.Log($"视线进入: {gameObject.name}");
            }

            // 只有在“正在注视”且“动作尚未触发”时，才累加计时器
            if (_isGazing && !_actionTriggered)
            {
                _currentDwellTime += Time.deltaTime;
                if (fillImage != null)
                {
                    fillImage.fillAmount = _currentDwellTime / dwellTime;
                }

                if (_currentDwellTime >= dwellTime)
                {
                    // --- 触发事件的逻辑 ---
                    OnDwellComplete?.Invoke();

                    // 触发后，立即锁住，防止重复触发
                    _actionTriggered = true;

                    // 我们不再重置计时器，让进度条保持满的状态，作为一种视觉反馈
                    Debug.Log("事件已触发，本次注视不再重复。");
                }
            }
        }
        else // isHittingMe == false (视线不在我身上)
        {
            if (_isGazing) // 检查上一帧是不是还在注视？
            {
                // 只有上一帧还在注视(true)，而这一帧不在了，才说明是“刚刚离开”
                ResetAll();
            }
        }
    }

    /// <summary>
    /// 将所有重置逻辑统一到一个方法中
    /// </summary>
    private void ResetAll()
    {
        if (_isGazing) Debug.Log($"视线离开: {gameObject.name}");
        _isGazing = false;
        _actionTriggered = false; // 确保锁也被重置
        _currentDwellTime = 0f;
        if (fillImage != null)
        {
            fillImage.fillAmount = 0f;
        }
    }
}