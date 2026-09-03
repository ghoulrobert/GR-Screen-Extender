using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenServer
{
    /// <summary>
    /// 设备发现服务 - 通过 UDP 广播实现局域网设备自动发现
    /// </summary>
    public class DeviceDiscovery : IDisposable
    {
        private UdpClient _udpClient;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        
        public event Action<string, IPEndPoint> OnDiscoveryRequest;
        public event Action<string> OnLog;
        
        public void Start(int port)
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            _udpClient = new UdpClient(port);
            _udpClient.EnableBroadcast = true;
            
            Task.Run(() => ListenForDiscovery(_cts.Token));
            Task.Run(() => BroadcastPresence(_cts.Token));
            
            OnLog?.Invoke($"设备发现服务已启动，端口: {port}");
        }
        
        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            _udpClient?.Close();
        }
        
        private async Task ListenForDiscovery(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    
                    if (message.StartsWith("GR_DISCOVERY:"))
                    {
                        string deviceName = message.Substring("GR_DISCOVERY:".Length);
                        OnDiscoveryRequest?.Invoke(deviceName, result.RemoteEndPoint);
                        
                        // 响应发现请求
                        string response = $"GR_SERVER:{Environment.MachineName}";
                        byte[] responseData = Encoding.UTF8.GetBytes(response);
                        await _udpClient.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        OnLog?.Invoke($"发现监听异常: {ex.Message}");
                }
            }
        }
        
        private async Task BroadcastPresence(CancellationToken token)
        {
            string message = $"GR_SERVER:{Environment.MachineName}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, _udpClient.Client.LocalEndPoint is IPEndPoint ep ? ep.Port : 33894);
            
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    await _udpClient.SendAsync(data, data.Length, broadcastEP);
                    await Task.Delay(5000, token); // 每5秒广播一次
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        OnLog?.Invoke($"广播异常: {ex.Message}");
                }
            }
        }
        
        public void Dispose()
        {
            Stop();
            _udpClient?.Dispose();
            _cts?.Dispose();
        }
    }
}
