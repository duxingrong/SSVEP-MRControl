// TCPManager.cs

using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent; 
using System.IO;




// 用于承载 object_added 和 object_updated 消息的数据结构
[System.Serializable]
public class ObjectData
{
    public int id;
    public int[] pos;
}

// 用于承载 object_removed 消息的数据结构
[System.Serializable]
public class ObjectId
{
    public int id;
}


// 定义接收到的坐标数据结构
[System.Serializable]
public class PointData
{
    public int id;
    public int[] pos;
}


[System.Serializable]
public class PointDataList
{
    public PointData[] points;
}



public class TCPManager : MonoBehaviour
{
    // 为消息类型创建委托（delegate）
    public delegate void ObjectDataHandler(ObjectData data);
    public delegate void ObjectIdHandler(ObjectId data);
    public delegate void PointDataHandler(PointData[] points);
    public delegate void CommandHandler(string command); 

    // 为新消息类型创建静态事件（event），作为广播频道
    public static event ObjectDataHandler OnObjectAdded;
    public static event ObjectDataHandler OnObjectUpdated;
    public static event ObjectIdHandler OnObjectRemoved;
    public static event CommandHandler OnSubtitleReceived; 
    public static event PointDataHandler OnPointsReceived;
    

    // --- 单例模式 ---
    private static TCPManager _instance;
    public static TCPManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("TCPManager");
                _instance = obj.AddComponent<TCPManager>();
            }
            return _instance;
        }
    }

    [System.Serializable]
    private class MessageEnvelope
    {
        public string type;
        public string payload; // Payload始终先作为字符串接收
    }

    [Header("Network Settings")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 9998;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;

    private readonly ConcurrentQueue<string> receivedMessages = new ConcurrentQueue<string>();

    // 防止线程一直循环
    private bool shouldKeepTrying = true;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            _instance = this;
        }
    }

    void Start()
    {
        ConnectToServer();
    }


    /// <summary>
    /// 尝试连接到服务器
    /// </summary>
    private void ConnectToServer()
    {
        if (isConnected) return;

        receiveThread = new Thread(() =>
        {
            while (!isConnected && shouldKeepTrying) 
            {
                try
                {
                    Debug.Log("TCP: 尝试连接到服务器...");
                    client = new TcpClient();
                    client.Connect(serverIP, serverPort);

                    stream = client.GetStream();
                    isConnected = true;
                    Debug.Log("TCP: 成功连接到服务器！");

                    ReceiveLoop();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"TCP: 连接失败: {e.Message}. 5秒后重试...");
                    isConnected = false;
                    client?.Close();

                    // 在睡眠期间也要检查是否应该继续
                    for (int i = 0; i < 50 && shouldKeepTrying; i++) 
                    {
                        Thread.Sleep(100);
                    }
                }
            }
        });
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    /// <summary>
    /// 从服务端接受数据，解析后将JSON数据放入receivemessage队列中
    /// </summary>
    private void ReceiveLoop()
    {
        try
        {
            byte[] lengthPrefix = new byte[4];
            while (isConnected && shouldKeepTrying && (stream?.CanRead ?? false)) 
            {
                int bytesRead = stream.Read(lengthPrefix, 0, 4);
                if (bytesRead < 4) break;

                // 当接受一个(整数，浮点数)的时候，需要判断设备是不是小端序，如果是就反转，因为服务器是以大端序发送的
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthPrefix);
                }
                int messageLength = BitConverter.ToInt32(lengthPrefix, 0);

                byte[] messageBytes = new byte[messageLength];
                int totalBytesRead = 0;
                while (totalBytesRead < messageLength)
                {
                    bytesRead = stream.Read(messageBytes, totalBytesRead, messageLength - totalBytesRead);
                    if (bytesRead == 0) break;
                    totalBytesRead += bytesRead;
                }

                if (totalBytesRead == messageLength)
                {
                    string message = Encoding.UTF8.GetString(messageBytes);
                    receivedMessages.Enqueue(message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"TCP: 接收数据时发生错误: {e.Message}");
        }

        // 只有在应该继续尝试的情况下才重连
        if (shouldKeepTrying)
        {
            Debug.LogWarning("TCP: 连接已断开，正在尝试重连...");
            isConnected = false;
            client?.Close();
            ConnectToServer();
        }
    }


    /// <summary>
    /// 根据信封不同的type分别调用不用的回调函数来处理
    /// </summary>
    void Update()
    {
        while (receivedMessages.TryDequeue(out string message))
        {
            try
            {
                MessageEnvelope envelope = JsonUtility.FromJson<MessageEnvelope>(message);

                // switch来处理新消息
                switch (envelope.type)
                {
                    // 处理“物体添加”事件
                    case "object_added":
                        ObjectData addedData = JsonUtility.FromJson<ObjectData>(envelope.payload);
                        Debug.Log($"[Unity收到消息] 类型: 'object_added', ID: {addedData.id}");
                        OnObjectAdded?.Invoke(addedData);
                        break;

                    // 处理“物体更新”事件
                    case "object_updated":
                        ObjectData updatedData = JsonUtility.FromJson<ObjectData>(envelope.payload);
       
                        // Debug.Log($"[Unity收到消息] 类型: 'object_updated', ID: {updatedData.id}");
                        OnObjectUpdated?.Invoke(updatedData);
                        break;

                    // 处理“物体移除”事件
                    case "object_removed":
                        ObjectId removedData = JsonUtility.FromJson<ObjectId>(envelope.payload);
                        Debug.Log($"[Unity收到消息] 类型: 'object_removed', ID: {removedData.id}");
                        OnObjectRemoved?.Invoke(removedData);
                        break;

                    // 处理“字幕”事件
                    case "subtitle":
                        Debug.Log($"[Unity收到消息] 类型: 'subtitle', 内容: {envelope.payload}");
                        OnSubtitleReceived?.Invoke(envelope.payload);
                        break;

                    // 处理送来的九宫格
                    case "point_list":
                        Debug.Log("<color=orange>[DEBUG]</color> Received a 'point_list' message. Payload: " + envelope.payload);

                        PointDataList dataList = JsonUtility.FromJson<PointDataList>(envelope.payload);

                        if (dataList != null && dataList.points != null)
                        {
                            Debug.Log($"<color=green>[DEBUG]</color> Successfully parsed {dataList.points.Length} points. Invoking event now.");
                            OnPointsReceived?.Invoke(dataList.points);
                        }
                        else
                        {
                            Debug.LogError("<color=red>[DEBUG]</color> Failed to parse 'point_list' payload. 'dataList' or 'dataList.points' is null.");
                        }
                        break;

                    default:
                        Debug.LogWarning($"收到未知类型的消息: {envelope.type}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"处理消息时发生错误: {e.Message}. 原始消息: {message}");
            }
        }
    }

    private void Send(string msgType, string payload)
    {
        if (!isConnected || stream == null || !stream.CanWrite)
        {
            Debug.LogError("TCP: 未连接，无法发送消息。");
            return;
        }

        try
        {
            MessageEnvelope envelope = new MessageEnvelope { type = msgType, payload = payload };
            string message = JsonUtility.ToJson(envelope);

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] lengthPrefix = BitConverter.GetBytes(messageBytes.Length);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthPrefix);
            }

            stream.Write(lengthPrefix, 0, 4);
            stream.Write(messageBytes, 0, messageBytes.Length);
        }
        catch (Exception e)
        {
            Debug.LogError($"TCP: 发送消息时发生错误: {e.Message}");
            stream.Close();
            isConnected = false;
        }
    }

    //为发送不同类型数据提供清晰的公共接口
    [System.Serializable] private class SelectionPayload { public int selected_id; }
    public void SendSelection(int id)
    {
        SelectionPayload payload = new SelectionPayload { selected_id = id };
        string payloadJson = JsonUtility.ToJson(payload);
        Send("selection", payloadJson);
    }

    public void SendCommand(string command)
    {
        Send("command", command);
    }


    // 一个私有的清理方法，避免代码重复
    private void Cleanup()
    {
        Debug.Log("TCP: Cleaning up resources.");

        // 首先设置标志位停止重连尝试
        shouldKeepTrying = false;
        isConnected = false;

        // 等待线程结束（最多等待1秒）
        if (receiveThread != null && receiveThread.IsAlive)
        {
            if (!receiveThread.Join(1000)) // 等待1秒
            {
                receiveThread.Abort(); // 如果线程没有正常结束，强制终止
            }
        }

        stream?.Close();
        client?.Close();
    }


    private void OnDestroy()
    {
        Cleanup();
    }

    
    private void OnApplicationQuit()
    {
        Cleanup();
    }

}