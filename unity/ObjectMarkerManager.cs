// ObjectMarkerManager.cs

using UnityEngine;
using System.Collections.Generic;

// 这个管理器的职责是：
// 1. 监听TCPManager的物体事件 (added, updated, removed)。
// 2. 根据事件，在UI上动态地创建、移动、销毁物体的可视化标记（Marker）。
// 3. 为每个创建的Marker动态添加并配置GazeDwellButton，使其能够被用户通过注视来选择。
// 4. 打印subtitle
public class ObjectMarkerManager : MonoBehaviour
{
    // --- 单例模式 ---
    public static ObjectMarkerManager Instance { get; private set; }

    [Header("Network Settings")]
    [Tooltip("PC端服务器的IP地址")]
    public string serverIP = "127.0.0.1";
    [Tooltip("PC端服务器的TCP端口")]
    public int serverPort = 9998;

    [Header("必要组件引用")]
    [Tooltip("用于可视化标记的预制体（Prefab），例如一个带Collider的圆形薄片")]
    public GameObject markerPrefab;

    [Tooltip("所有标记都将在此RectTransform下创建，通常是你的RawImage组件所在的GameObject")]
    public RectTransform markerParent;


    // 为移动模式的目标点单独设置预制体，方便区分样式
    [Header("Move Mode Settings")]
    [Tooltip("用于可视化“移动目标点”的预制体")]
    public GameObject destinationMarkerPrefab;

    [Header("配置参数")]
    [Tooltip("用户需要注视多久才能选中标记（秒）")]
    public float selectionDwellTime = 2.0f;

    // PC端的摄像头分辨率是640x480，用于坐标转换
    private const float SOURCE_RESOLUTION_X = 640f;
    private const float SOURCE_RESOLUTION_Y = 480f;

    // 使用字典来追踪当前所有活跃的标记，键是物体的track_id，值是对应的GameObject
    private Dictionary<int, GameObject> activeMarkers = new Dictionary<int, GameObject>();
    // 为移动目标点创建一个独立的字典进行管理
    private Dictionary<int, GameObject> activeDestinationMarkers = new Dictionary<int, GameObject>();
    
    private void Awake()
    {
        // 实现单例模式
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return; 
        }

        Instance = this;

        // 配置TCPManager的网络参数
        Debug.Log("ObjectMarkerManager 正在配置 TCPManager...");
        TCPManager tcpManager = TCPManager.Instance; 
        tcpManager.serverIP = this.serverIP;
        tcpManager.serverPort = this.serverPort;
        Debug.Log($"已将TCPManager的目标连接更新为 -> {tcpManager.serverIP}:{tcpManager.serverPort}");
    }

    // 在脚本启用时，开始“收听”TCPManager的广播
    private void OnEnable()
    {
        Debug.Log("ObjectMarkerManager 已启用，正在订阅TCP事件...");
        TCPManager.OnObjectAdded += HandleObjectAdded;
        TCPManager.OnObjectUpdated += HandleObjectUpdated;
        TCPManager.OnObjectRemoved += HandleObjectRemoved;
        TCPManager.OnPointsReceived += HandlePointsReceived;
        TCPManager.OnSubtitleReceived += HandleSubtitleReceived;
    }

    // 在脚本禁用时，取消“收听”，防止内存泄漏
    private void OnDisable()
    {
        Debug.Log("ObjectMarkerManager 已禁用，正在取消订阅TCP事件。");
        TCPManager.OnObjectAdded -= HandleObjectAdded;
        TCPManager.OnObjectUpdated -= HandleObjectUpdated;
        TCPManager.OnObjectRemoved -= HandleObjectRemoved;
        TCPManager.OnPointsReceived -= HandlePointsReceived;
        TCPManager.OnSubtitleReceived -= HandleSubtitleReceived;
    }


    // 处理从PC收到的所有字幕消息
    private void HandleSubtitleReceived(string message)
    {
        SingleLineConsoleManager.Instance.ShowMessage(message, Color.green);
    }



    // --- 事件处理核心函数 ---

    private void HandleObjectAdded(ObjectData data)
    {
        // 防御性编程：检查预制体和父级是否存在
        if (markerPrefab == null || markerParent == null)
        {
            Debug.LogError("MarkerPrefab 或 MarkerParent 未在Inspector中设置！");
            return;
        }

        // 如果因为某些原因（如消息延迟）已经存在这个id的标记，先移除旧的
        if (activeMarkers.ContainsKey(data.id))
        {
            HandleObjectRemoved(new ObjectId { id = data.id });
        }

        // 1. 实例化标记Prefab，并设置其父级
        GameObject newMarker = Instantiate(markerPrefab, markerParent);
        newMarker.name = $"Marker_{data.id}";

        // 2. 计算并设置其在UI画布上的位置
        newMarker.transform.localPosition = ConvertPixelToLocalPosition(data.pos);

        // 3. 动态添加并配置注视按钮组件
        GazeDwellButton_Simplified gazeButton = newMarker.AddComponent<GazeDwellButton_Simplified>();
        gazeButton.dwellTime = selectionDwellTime;
        // 当注视完成时，调用本脚本的 OnMarkerSelected 方法，并传入当前物体的id
        gazeButton.OnDwellComplete.AddListener(() => OnMarkerSelected(data.id));

        // 4. 将新创建的标记存入字典进行管理
        activeMarkers.Add(data.id, newMarker);
    }

    private void HandleObjectUpdated(ObjectData data)
    {
        // 尝试从字典中根据id查找标记
        if (activeMarkers.TryGetValue(data.id, out GameObject markerToUpdate))
        {
            // 如果找到，则更新其位置
            markerToUpdate.transform.localPosition = ConvertPixelToLocalPosition(data.pos);
        }
    }

    private void HandleObjectRemoved(ObjectId data)
    {
        if (activeMarkers.TryGetValue(data.id, out GameObject markerToRemove))
        {
            // 从字典中移除
            activeMarkers.Remove(data.id);
            // 销毁对应的GameObject
            Destroy(markerToRemove);
        }
    }


    // 处理从PC收到的九宫格目标点
    private void HandlePointsReceived(PointData[] points)
    {
        Debug.Log($"收到了 {points.Length} 个移动目标点，准备进行可视化...");

        if (destinationMarkerPrefab == null || markerParent == null)
        {
            Debug.LogError("DestinationMarkerPrefab 或 MarkerParent 未在Inspector中设置！");
            return;
        }

        ClearAllDestinationMarkers();

        foreach (var point in points)
        {
            GameObject destMarker = Instantiate(destinationMarkerPrefab, markerParent);
            destMarker.name = $"DestinationMarker_{point.id}";
            destMarker.transform.localPosition = ConvertPixelToLocalPosition(point.pos);

            GazeDwellButton_Simplified gazeButton = destMarker.AddComponent<GazeDwellButton_Simplified>();
            gazeButton.dwellTime = selectionDwellTime;
            gazeButton.OnDwellComplete.AddListener(() => OnDestinationSelected(point.id));

            activeDestinationMarkers.Add(point.id, destMarker);
        }
    }

    // --- 辅助方法 ---

    /// <summary>
    /// 将来自PC的像素坐标转换为Unity UI的本地坐标。
    /// </summary>
    private Vector2 ConvertPixelToLocalPosition(int[] pixelCoords)
    {
        // 获取父级（RawImage）的尺寸
        float parentWidth = markerParent.rect.width;
        float parentHeight = markerParent.rect.height;

        // 1. 将像素坐标归一化 (0.0 to 1.0)
        float normX = pixelCoords[0] / SOURCE_RESOLUTION_X;
        float normY = pixelCoords[1] / SOURCE_RESOLUTION_Y;

        // 2. 将归一化坐标映射到父级RectTransform的本地坐标
        // UI坐标系的原点(0,0)在中心，而像素坐标原点在左上角，因此需要转换：
        // X: (归一化值 - 0.5) * 宽度
        // Y: (0.5 - 归一化值) * 高度 (Y轴方向相反)
        float localX = (normX - 0.5f) * parentWidth;
        float localY = (0.5f - normY) * parentHeight;

        return new Vector2(localX, localY);
    }

    /// <summary>
    /// 当一个标记被成功注视后，此方法被调用。
    /// </summary>
    public void OnMarkerSelected(int id)
    {
        // 在发送消息前，立即给用户一个本地反馈
        SingleLineConsoleManager.Instance.ShowMessage($"已选择物体 ID:{id}，正在等待PC端确认...", Color.cyan);

        Debug.Log($"用户通过注视选择了物体，ID: {id}。正在通知PC端...");
        TCPManager.Instance.SendSelection(id);
        ClearAllMarkers();
    }

    /// <summary>
    /// 清除所有当前显示的标记。
    /// </summary>
    public void ClearAllMarkers()
    {
        foreach (var marker in activeMarkers.Values)
        {
            Destroy(marker);
        }
        activeMarkers.Clear();
    }

    // 当一个“移动目标点”被成功注视后调用
    public void OnDestinationSelected(int id)
    {
        // 在发送消息前，立即给用户一个本地反馈
        SingleLineConsoleManager.Instance.ShowMessage($"已选择移动目标点 ID:{id}，等待PC端执行...", Color.cyan);

        Debug.Log($"用户通过注视选择了移动目标点，ID: {id}。正在通知PC端...");
        TCPManager.Instance.SendSelection(id);
        ClearAllDestinationMarkers();
    }

    // 清除所有当前显示的“移动目标点”标记
    public void ClearAllDestinationMarkers()
    {
        foreach (var marker in activeDestinationMarkers.Values)
        {
            Destroy(marker);
        }
        activeDestinationMarkers.Clear();
    }
}