using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 当按钮触发时，调用对应的函数发送给PC端
/// </summary>
public class sendButtonCommand : MonoBehaviour
{
    public void Eat()
    {
        TCPManager.Instance.SendCommand("eat");
        SingleLineConsoleManager.Instance.ShowMessage("将为您执行eat命令！",Color.green);
    }

    public void Grub()
    {
        TCPManager.Instance.SendCommand("grub");
        SingleLineConsoleManager.Instance.ShowMessage("将为您执行grub命令！", Color.green);
    }

    public void Door()
    {
        TCPManager.Instance.SendCommand("door");
        SingleLineConsoleManager.Instance.ShowMessage("将为您执行door命令！", Color.green);
    }

    public void Plate()
    {
        TCPManager.Instance.SendCommand("plate");
        SingleLineConsoleManager.Instance.ShowMessage("将为您执行plate命令！", Color.green);
    }

    public void Move()
    {
        TCPManager.Instance.SendCommand("move");
        SingleLineConsoleManager.Instance.ShowMessage("将为您执行move命令！", Color.green);
    }

}
