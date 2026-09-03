using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace ScreenServer
{
    /// <summary>
    /// 流媒体服务器 - 处理客户端连接、视频分发和输入回传
    /// </summary>
    public class StreamingServer : IDisposable
    {
        // 网络组件
        private TcpListener _controlListener;
        private TcpListener _inputListener;
        private UdpClient _videoUdp;
        private UdpClient _discoveryUdp;
        
        // 客户端管理
        private Dictionary<string, ClientSession> _clients = new Dictionary<string, ClientSession>();
        private readonly object _clientsLock = new object();
        
        // 线程控制
        private CancellationTokenSource _cts;
        private bool _isRunning;
        
        // 视频数据分发
        private ConcurrentVideoBuffer _videoBuffer;
        
        // 设备发现
        private DeviceDiscovery _discovery;
        
        public event Action<ClientSession, string> OnClientConnected;
        public event Action<ClientSession> OnClientDisconnected;
        public event Action<string> OnLog;
        
        public class ClientSession
        {
            public string SessionId { get; set; }
            public string DeviceName { get; set; }
            public TcpClient ControlClient { get; set; }
            public TcpClient InputClient { get; set; }
            public IPEndPoint VideoEndPoint { get; set; }
            public DateTime ConnectedTime { get; set; }
            public bool IsStreaming { get; set; }
            public int Quality { get; set; } = 5; // 1-10
            
            public void SendControlData(byte[] data)
            {
                try
                {
                    if (ControlClient?.Connected == true)
                    {
                        ControlClient.GetStream().Write(data, 0, data.Length);
                    }
                }
                catch { }
            }
            
            public void SendInputData(byte[] data)
            {
                try
                {
                    if (InputClient?.Connected == true)
                    {
                        InputClient.GetStream().Write(data, 0, data.Length);
                    }
                }
                catch { }
            }
        }
        
        public StreamingServer()
        {
            _videoBuffer = new ConcurrentVideoBuffer();
            _discovery = new DeviceDiscovery();
        }
        
        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            // 启动控制通道
            _controlListener = new TcpListener(IPAddress.Any, Protocol.ControlPort);
            _controlListener.Start();
            Task.Run(() => AcceptControlClients(_cts.Token));
            
            // 启动输入通道
            _inputListener = new TcpListener(IPAddress.Any, Protocol.InputPort);
            _inputListener.Start();
            Task.Run(() => AcceptInputClients(_cts.Token));
            
            // 启动 UDP 视频发送
            _videoUdp = new UdpClient(Protocol.VideoPort);
            
            // 启动设备发现服务
            _discovery.Start(Protocol.DiscoveryPort);
            _discovery.OnDiscoveryRequest += HandleDiscoveryRequest;
            
            OnLog?.Invoke($"服务器已启动 - 控制端口:{Protocol.ControlPort}, 视频端口:{Protocol.VideoPort}, 输入端口:{Protocol.InputPort}");
        }
        
        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            
            // 断开所有客户端
            lock (_clientsLock)
            {
                foreach (var client in _clients.Values)
                {
                    client.ControlClient?.Close();
                    client.InputClient?.Close();
                }
                _clients.Clear();
            }
            
            _controlListener?.Stop();
            _inputListener?.Stop();
            _videoUdp?.Close();
            _discovery.Stop();
            
            OnLog?.Invoke("服务器已停止");
        }
        
        /// <summary>
        /// 发送视频帧到所有连接的客户端
        /// </summary>
        public void BroadcastVideoFrame(byte[] h264Data, bool isKeyFrame)
        {
            if (_clients.Count == 0) return;
            
            var packet = new VideoFramePacket
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                FrameNumber = 0,
                H264Data = h264Data,
                IsKeyFrame = isKeyFrame
            };
            
            byte[] data = packet.Serialize();
            
            List<ClientSession> clients;
            lock (_clientsLock)
            {
                clients = _clients.Values.ToList();
            }
            
            foreach (var client in clients)
            {
                if (client.IsStreaming && client.VideoEndPoint != null)
                {
                    try
                    {
                        _videoUdp.Send(data, data.Length, client.VideoEndPoint);
                    }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke($"发送视频到 {client.DeviceName} 失败: {ex.Message}");
                    }
                }
            }
        }
        
        private async Task AcceptControlClients(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _controlListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleControlClient(tcpClient, token), token);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        OnLog?.Invoke($"接受控制连接失败: {ex.Message}");
                }
            }
        }
        
        private async Task HandleControlClient(TcpClient tcpClient, CancellationToken token)
        {
            var session = new ClientSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ControlClient = tcpClient,
                ConnectedTime = DateTime.Now
            };
            
            try
            {
                var stream = tcpClient.GetStream();
                byte[] headerBuffer = new byte[Protocol.HeaderSize];
                
                // 读取设备信息
                int read = await stream.ReadAsync(headerBuffer, 0, Protocol.HeaderSize, token);
                if (read < Protocol.HeaderSize) return;
                
                // 验证 Magic
                ushort magic = BitConverter.ToUInt16(headerBuffer, 0);
                if (magic != Protocol.Magic) return;
                
                int payloadLength = BitConverter.ToInt32(headerBuffer, 3);
                byte[] payload = new byte[payloadLength];
                await stream.ReadAsync(payload, 0, payloadLength, token);
                
                // 解析设备信息
                session.DeviceName = System.Text.Encoding.UTF8.GetString(payload);
                
                // 添加到客户端列表
                lock (_clientsLock)
                {
                    _clients[session.SessionId] = session;
                }
                
                OnLog?.Invoke($"设备已连接: {session.DeviceName} ({session.SessionId})");
                OnClientConnected?.Invoke(session, session.DeviceName);
                
                // 发送确认
                var response = new ControlPacket
                {
                    Command = Protocol.CmdPairResponse,
                    Payload = System.Text.Encoding.UTF8.GetBytes($"OK|{session.SessionId}")
                };
                byte[] responseData = response.Serialize();
                await stream.WriteAsync(responseData, 0, responseData.Length, token);
                
                // 保持连接，等待命令
                byte[] cmdBuffer = new byte[Protocol.HeaderSize];
                while (_isRunning && tcpClient.Connected && !token.IsCancellationRequested)
                {
                    read = await stream.ReadAsync(cmdBuffer, 0, Protocol.HeaderSize, token);
                    if (read < Protocol.HeaderSize) break;
                    
                    magic = BitConverter.ToUInt16(cmdBuffer, 0);
                    if (magic != Protocol.Magic) continue;
                    
                    int cmdLength = BitConverter.ToInt32(cmdBuffer, 3);
                    byte[] cmdPayload = new byte[cmdLength];
                    await stream.ReadAsync(cmdPayload, 0, cmdLength, token);
                    
                    HandleControlCommand(session, cmdPayload);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"控制连接异常: {ex.Message}");
            }
            finally
            {
                // 清理
                lock (_clientsLock)
                {
                    _clients.Remove(session.SessionId);
                }
                OnClientDisconnected?.Invoke(session);
                OnLog?.Invoke($"设备已断开: {session.DeviceName}");
                tcpClient.Close();
            }
        }
        
        private void HandleControlCommand(ClientSession session, byte[] payload)
        {
            if (payload.Length < 1) return;
            
            byte cmd = payload[0];
            switch (cmd)
            {
                case Protocol.CmdStartStream:
                    session.IsStreaming = true;
                    OnLog?.Invoke($"{session.DeviceName} 请求开始流传输");
                    break;
                    
                case Protocol.CmdStopStream:
                    session.IsStreaming = false;
                    OnLog?.Invoke($"{session.DeviceName} 请求停止流传输");
                    break;
                    
                case Protocol.CmdQualityChange:
                    if (payload.Length > 1)
                    {
                        session.Quality = payload[1];
                        OnLog?.Invoke($"{session.DeviceName} 调整质量: {session.Quality}");
                    }
                    break;
                    
                case Protocol.CmdDisconnect:
                    session.ControlClient?.Close();
                    break;
            }
        }
        
        private async Task AcceptInputClients(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _inputListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleInputClient(tcpClient, token), token);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        OnLog?.Invoke($"接受输入连接失败: {ex.Message}");
                }
            }
        }
        
        private async Task HandleInputClient(TcpClient tcpClient, CancellationToken token)
        {
            try
            {
                var stream = tcpClient.GetStream();
                byte[] headerBuffer = new byte[Protocol.HeaderSize];
                
                while (_isRunning && tcpClient.Connected && !token.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(headerBuffer, 0, Protocol.HeaderSize, token);
                    if (read < Protocol.HeaderSize) break;
                    
                    ushort magic = BitConverter.ToUInt16(headerBuffer, 0);
                    if (magic != Protocol.Magic) continue;
                    
                    int payloadLength = BitConverter.ToInt32(headerBuffer, 3);
                    byte[] payload = new byte[payloadLength];
                    await stream.ReadAsync(payload, 0, payloadLength, token);
                    
                    // 解析输入事件
                    if (payload.Length >= 17)
                    {
                        var inputEvent = InputEventPacket.Deserialize(payload, 0);
                        HandleInputEvent(inputEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"输入连接异常: {ex.Message}");
            }
        }
        
        private void HandleInputEvent(InputEventPacket inputEvent)
        {
            // 将输入事件转发到输入注入器
            InputInjector.ProcessInputEvent(inputEvent);
        }
        
        private void HandleDiscoveryRequest(string deviceName, IPEndPoint endPoint)
        {
            OnLog?.Invoke($"发现设备: {deviceName} from {endPoint}");
            // 可以自动响应或等待用户确认
        }
        
        public void Dispose()
        {
            Stop();
            _videoUdp?.Dispose();
            _discovery?.Dispose();
            _cts?.Dispose();
        }
    }
    
    /// <summary>
    /// 视频缓冲区 - 用于平滑帧率波动
    /// </summary>
    public class ConcurrentVideoBuffer
    {
        private System.Collections.Concurrent.ConcurrentQueue<VideoFramePacket> _queue = 
            new System.Collections.Concurrent.ConcurrentQueue<VideoFramePacket>();
        
        private const int MaxBufferSize = 3; // 最多缓冲3帧
        
        public void PushFrame(VideoFramePacket frame)
        {
            _queue.Enqueue(frame);
            while (_queue.Count > MaxBufferSize)
            {
                _queue.TryDequeue(out _);
            }
        }
        
        public bool TryPopFrame(out VideoFramePacket frame)
        {
            return _queue.TryDequeue(out frame);
        }
        
        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
