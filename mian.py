# main.py

import pyrealsense2 as rs
import numpy as np
import cv2
import ar_system.Img_sender as Img_sender
import time 
from ar_system.tcp_manager import TCPServer
import json
from ultralytics import YOLO
import math
from enum import Enum , auto
import threading 

# ===================================================================
# 1. 状态机核心定义
# ===================================================================

class SystemState(Enum):
    """定义系统的所有可能状态"""
    IDLE_DETECTING = auto()    # 1. 空闲 / 侦测中
    AWAITING_COMMAND = auto()  # 2. 等待指令
    MOVE_MODE = auto()         # 3. “移动”模式
    COMMAND_EXECUTING = auto() # 4. 指令执行中 


def set_system_state(new_state):
    """安全地更改系统状态并打印日志。"""
    global current_state, selected_object_id, tracked_objects_state 
    
    if isinstance(new_state, SystemState) and new_state != current_state:
        print(f"--- 状态切换: 从 {current_state.name} -> 到 {new_state.name} ---")
        current_state = new_state

        # 当状态返回到“空闲”时，清除所有上一轮的上下文信息
        if new_state == SystemState.IDLE_DETECTING:
            selected_object_id = None
            
            if tracked_objects_state: 
                tracked_objects_state.clear()
                print("状态重置为空闲，已清除选中的物体ID和上一轮的物体追踪状态。")
            else:
                 print("状态重置为空闲，已清除选中的物体ID。")



# --- 全局变量 ---
current_state = SystemState.IDLE_DETECTING # 初始化系统状态
selected_object_id = None # 存储被选中的物体ID
server = None 
move_target_points = None  # 用于在MOVE_MODE下临时存储九宫格坐标



# ===================================================================
# 2. 耗时任务与回调函数
# ===================================================================

def execute_robot_arm_task(command, target_info=None):
    """
    【模拟】一个耗时的机械臂操作。
    这个函数应该在一个独立的线程中运行，以避免阻塞主程序。
    """
    global server
    print(f"[机械臂线程] 开始执行 '{command}' 操作...")
    if target_info:
        print(f"[机械臂线程] 目标信息: {target_info}")
    
    # 模拟实际操作耗时
    time.sleep(5) 
    
    print(f"[机械臂线程] '{command}' 操作完成！")
    
    # 操作完成后，向Unity发送完成消息，并切换回空闲状态
    if server and server.client_connection:
        server.send("subtitle", f"'{command}' 操作已完成。")
    
    set_system_state(SystemState.IDLE_DETECTING)


def generate_nine_points(width, height):
    """生成九宫格中每个区域中心点的坐标"""
    points = []
    for i in range(3):  # 代表行 (y)
        for j in range(3):  # 代表列 (x)
            x = int(width * (2 * j + 1) / 6)
            y = int(height * (2 * i + 1) / 6)
            points.append([x, y])
    return points


def handle_unity_selection_and_verify(payload):
    """处理Unity发送的'selection'消息。根据当前状态来决定如何响应。"""
    global selected_object_id, server, move_target_points
    
    try:
        selection_data = json.loads(payload)
        
        # 场景一：在"空闲/侦测"状态下，收到了物体选择
        if current_state == SystemState.IDLE_DETECTING:
            if 'selected_id' in selection_data:
                selected_object_id = selection_data.get('selected_id')
                print(f"PC端：Unity选择的物体ID是: {selected_object_id}")
                
                # 切换到"等待指令"状态
                set_system_state(SystemState.AWAITING_COMMAND)
                
                # 发送提示字幕
                if server:
                    server.send("subtitle", f"已选中物体 {selected_object_id}，请选择操作。")

        # 场景二：在"移动"模式下，收到了目标点选择
        elif current_state == SystemState.MOVE_MODE:
            if 'selected_id' in selection_data:
                target_id = selection_data.get('selected_id')
                
                # 根据收到的ID，从之前存储的列表中查找对应的坐标
                if move_target_points and 0 <= target_id < len(move_target_points):
                    target_point = move_target_points[target_id]
                    print(f"PC端：Unity选择的目标点ID是: {target_id}, 对应坐标是: {target_point}")

                    # 切换到"指令执行中"状态
                    set_system_state(SystemState.COMMAND_EXECUTING)
                    
                    # 发送提示字幕并启动机械臂移动线程
                    if server:
                        server.send("subtitle", f"正在移动到 {target_point}，请稍候...")
                    
                    # 新线程中执行机械臂移动
                    task_thread = threading.Thread(target=execute_robot_arm_task, args=("move", target_point))
                    task_thread.start()
                else:
                    print(f"错误：收到了无效的目标点ID {target_id} 或 move_target_points 未设置。")

    except Exception as e:
        print(f"PC端：处理selection回调时出错: {e}")


def handle_unity_command(payload):
    """处理Unity发送的'command'消息。只在"等待指令"状态下响应。"""
    global server, move_target_points
    
    # 必须是在等待指令的状态下，才处理这些命令
    if current_state != SystemState.AWAITING_COMMAND:
        ignore_message = f"收到指令 '{payload}'，但当前状态为 {current_state.name}，已忽略。"
        print(ignore_message)
        
        if server:
            server.send("subtitle", ignore_message)
        return

    print(f"收到指令 '{payload}'，当前状态正确，开始处理。")
    
    if payload == "move":
        # 切换到“移动模式”
        set_system_state(SystemState.MOVE_MODE)
        
        # 生成九宫格坐标点并发送给Unity
        nine_points_coords = generate_nine_points(WIDTH, HEIGHT) 
        # 将生成的坐标点存储到全局变量中，以便后续查找
        move_target_points = nine_points_coords
        nine_points_data = [{"id": i, "pos": pos} for i, pos in enumerate(nine_points_coords)]
        if server:
            server.send("point_list", {"points": nine_points_data})
            server.send("subtitle", "请注视您想移动到的目标位置。")

    elif payload in ["eat", "grub", "door", "plate"]:
        # 对于其他直接执行的指令
        set_system_state(SystemState.COMMAND_EXECUTING)
        
        if server:
            server.send("subtitle", f"正在执行 '{payload}' 操作，请稍候...")
        
        # 在新线程中执行机械臂操作
        task_thread = threading.Thread(target=execute_robot_arm_task, args=(payload,))
        task_thread.start()
    else:
        print(f"收到未知指令: {payload}")


# ===================================================================
# 3. 主函数
# ===================================================================

if __name__=="__main__":
    
    # --- 网络配置 ---
    # 将IP和端口定义为变量，方便修改
    UNITY_IP = "127.0.0.1"
    UNITY_UDP_PORT = 9999
    UNITY_TCP_PORT = 9998

    # --- 相机配置 ---
    pipeline = rs.pipeline()
    config = rs.config()
    WIDTH = 640
    HEIGHT = 480
    FPS = 30
    config.enable_stream(rs.stream.depth, WIDTH, HEIGHT, rs.format.z16, FPS)
    config.enable_stream(rs.stream.color, WIDTH, HEIGHT, rs.format.bgr8, FPS)
    colorizer = rs.colorizer()

    print("正在启动相机流...")
    profile = pipeline.start(config)
    print(f"相机已启动，正在向 {UNITY_IP}:{UNITY_UDP_PORT} 发送图像，按'ESC'键退出。")


     # --- 通信配置 ---
    server = TCPServer(UNITY_IP,UNITY_TCP_PORT)
    # 注册回调
    server.register_callback("selection", handle_unity_selection_and_verify)
    server.register_callback("command", handle_unity_command)
    server.start()
    print("--- Python TCP服务器已就绪，等待 Unity 客户端连接... ---")


    # --- YOLO模型配置 ---
    try:
        model = YOLO('assets/yolo11n.pt')
    except Exception as e:
        print(f"加载YOLO模型失败，请检查网络连接或文件路径。错误: {e}")
        exit()

    # --- 状态追踪变量 ---
    tracked_objects_state = {}
    MOVE_THRESHOLD = 5 

    # --- FPS 计算变量 ---
    frame_count, start_time, display_fps = 0, time.time(), 0

    # --- 核心运行逻辑 ---
    try:
        while True:
            # --- 通用逻辑：帧捕获与图像发送 (所有状态下都执行) ---
            frames = pipeline.wait_for_frames()
            color_frame = frames.get_color_frame()
            if not color_frame:
                continue
            
            color_image = np.asanyarray(color_frame.get_data())
            Img_sender.send_image(color_image, UNITY_IP, UNITY_UDP_PORT)

            # --- 状态判断：根据当前状态执行特定逻辑 ---

            # 状态 1: 空闲 / 侦测中
            if current_state == SystemState.IDLE_DETECTING:
                results = model.track(source=color_image, persist=True, verbose=False)
                current_objects = {}
                
                if results[0].boxes.id is not None:
                    tracked_boxes = results[0].boxes
                    track_ids = tracked_boxes.id.int().cpu().tolist()
                    class_ids = tracked_boxes.cls.int().cpu().tolist()
                    confidences = tracked_boxes.conf.cpu().tolist()
                    boxes_coords = tracked_boxes.xyxy.cpu().tolist()
                    for track_id, box_coords, cls_id in zip(tracked_boxes.id.int().cpu().tolist(), tracked_boxes.xyxy.cpu().tolist(), tracked_boxes.cls.int().cpu().tolist()):
                        x1, y1, x2, y2 = map(int, box_coords)
                        center_x, center_y = (x1 + x2) // 2, (y1 + y2) // 2
                        current_objects[track_id] = {"pos": [center_x, center_y]}
                        label = f"ID:{track_id} {model.names[cls_id]}"
                        cv2.rectangle(color_image, (x1, y1), (x2, y2), (0, 255, 0), 2)
                        cv2.circle(color_image, (center_x, center_y), 3, (0, 0, 255), -1)
                        cv2.putText(color_image, label, (x1, y1 - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2)

                # --- 事件驱动消息发送 ---
                if server.client_connection:
                    # 1. 查找新出现的物体
                    new_ids = set(current_objects.keys()) - set(tracked_objects_state.keys())
                    for new_id in new_ids:
                        server.send("object_added", {"id": new_id, "pos": current_objects[new_id]["pos"]})

                    # 2. 查找消失的物体
                    lost_ids = set(tracked_objects_state.keys()) - set(current_objects.keys())
                    for lost_id in lost_ids:
                        server.send("object_removed", {"id": lost_id})

                    # 3. 查找位置更新的物体
                    for track_id, obj_data in current_objects.items():
                        if track_id in tracked_objects_state:
                            last_pos = tracked_objects_state[track_id]["pos"]
                            if math.dist(last_pos, obj_data["pos"]) > MOVE_THRESHOLD:
                                server.send("object_updated", {"id": track_id, "pos": obj_data["pos"]})
                
                tracked_objects_state = current_objects
            
            # 状态 2, 3, 4: 等待指令, 移动模式, 指令执行中
            # 在这些状态下，主循环不进行物体检测，画面上的物体标记会“冻结”在进入状态前的最后一帧。
            # 所有逻辑都由回调函数和后台线程驱动。
            else:
                # 可以在画面上显示当前状态
                cv2.putText(color_image, f"STATE: {current_state.name}", (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 0), 2)

            # --- 通用逻辑：FPS计算与画面显示  ---
            frame_count += 1
            if (time.time() - start_time) >= 1.0:
                display_fps = frame_count / (time.time() - start_time)
                frame_count, start_time = 0, time.time()
            cv2.putText(color_image, f"FPS: {display_fps:.2f}", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
            cv2.imshow('RealSense - State Machine Control', color_image)
            
            if cv2.waitKey(1) & 0xFF == 27:
                break
    finally:
        print("正在停止...")
        pipeline.stop()
        server.stop()
        cv2.destroyAllWindows()
        print("已退出。")