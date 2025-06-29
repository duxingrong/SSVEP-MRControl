# tcp_manager.py

import socket
import threading
import time
import json

#################################################
# TCP服务，双向传输数据
# 双向传输的数据格式一定都是信封，即{type:, payload:}
#################################################



class TCPServer:
    def __init__(self, host='0.0.0.0', port=9998):
        self.host = host
        self.port = port
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM) #TCP
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.client_connection = None
        self.client_address = None
        self.is_running = True
        self.receive_thread = threading.Thread(target=self._wait_for_client)
        self.receive_thread.daemon = True
        # 创建一个字典来存储回调函数
        self.callbacks = {}

    # 注册回调函数
    def register_callback(self, msg_type, callback_func):
        """
        为特定消息类型注册一个回调函数。
        Args:
            msg_type (str): 消息的类型
            callback_func (function): 当收到该类型消息时要调用的函数。
        """
        self.callbacks[msg_type] = callback_func
        print(f"已为消息类型 '{msg_type}' 注册回调函数: {callback_func.__name__}")


    def start(self):
        """启动服务器并开始在后台监听"""
        self.server_socket.bind((self.host, self.port))
        self.server_socket.listen(1) 
        print(f"TCP服务器已启动，正在监听 {self.host}:{self.port}...")
        self.receive_thread.start()

    def _handle_client(self):
        """处理已连接客户端的数据接收"""
        print(f"客户端 {self.client_address} 已连接。")
        while self.is_running and self.client_connection:
            try:
                # 首先接收4个字节的长度前缀
                length_prefix = self.client_connection.recv(4)
                if not length_prefix:
                    break
                
                message_length = int.from_bytes(length_prefix, 'big')
                
                # 然后根据长度接收完整的消息
                data = b''
                while len(data) < message_length:
                    packet = self.client_connection.recv(message_length - len(data))
                    if not packet:
                        break
                    data += packet
                
                # 跳过数据不完整
                if not data or len(data)!=message_length:
                    break

                message_str = data.decode('utf-8')
                # 先解析外层的"信封"
                envelope = json.loads(message_str)
                msg_type = envelope.get("type")
                payload = envelope.get('payload')
                
                
                # 调用注册的回调函数
                if msg_type in self.callbacks:
                    # 如果有注册的回调函数，就调用它，并把payload传进去
                    self.callbacks[msg_type](payload)
                else:
                    print(f"警告: 收到未注册回调的消息类型 '{msg_type}'")

            except (ConnectionResetError, ConnectionAbortedError):
                print("与客户端的连接已断开。")
                break
            except Exception as e:
                print(f"接收数据时发生错误: {e}")
                break
        
        print("客户端处理循环结束，等待新连接...")
        self.client_connection.close()
        self.client_connection = None


    def _wait_for_client(self):
        """在循环中等待客户端连接"""
        while self.is_running:
            if not self.client_connection:
                try:
                    # accept() 会阻塞，直到有客户端连接
                    self.client_connection, self.client_address = self.server_socket.accept()
                    # 启动一个新线程来处理客户端
                    self._handle_client()
                except Exception as e:
                    if self.is_running:
                         print(f"等待连接时发生错误: {e}")
                    break
            time.sleep(1)

    def send(self, msg_type,payload):
        """向已连接的Unity客户端发送数据 (字符串或字典/列表)"""
        if not self.client_connection:
            print("没有客户端连接，无法发送数据。")
            return False

        try:
            # 无论payload是什么，都先将其转换为字符串（如果是字典/列表，则转换为JSON字符串）
            if isinstance(payload, (dict, list)):
                payload_str = json.dumps(payload)
            else:
                payload_str = str(payload)

            # 构建“信封”
            envelope = {
                "type": msg_type,
                "payload": payload_str
            }
            message = json.dumps(envelope)
            
            encoded_message = message.encode('utf-8')
            length_prefix = len(encoded_message).to_bytes(4, 'big')

            self.client_connection.sendall(length_prefix + encoded_message)
            return True
        except Exception as e:
            print(f"发送数据时发生错误: {e}")
            self.client_connection = None # 假设连接已断开
            return False
            
    def stop(self):
        """关闭TCP服务"""
        self.is_running = False
        if self.client_connection:
            self.client_connection.close()
        # 通过连接一个自己来解除accept的阻塞
        try:
            socket.socket(socket.AF_INET, socket.SOCK_STREAM).connect((self.host, self.port))
        except:
            pass
        self.server_socket.close()
        print("TCP服务器已关闭。")


