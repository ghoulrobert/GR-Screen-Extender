using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenServer
{
    class Program
    {
        static ScreenCapture _capture;
        static H264Encoder _encoder;
        static StreamingServer _server;
        static bool _isRunning;
        
        static void Main(string[] args)
        {
            Console.Title = "GR 扩展屏幕 - 服务器";
            Console.WriteLine("=====================================");
            Console.WriteLine("    GR 扩展屏幕服务 v1.0");
            Console.WriteLine("=====================================");
            Console.WriteLine();
            
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Shutdown();
            };
            
            try
            {
                Initialize();
                Run().Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"致命错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                Shutdown();
            }
        }
        
        static void Initialize()
        {
            Console.WriteLine("正在初始化...");
            
            // 初始化输入注入器
            InputInjector.Initialize();
            
            // 创建流媒体服务器
            _server = new StreamingServer();
            _server.OnLog += msg => Console.WriteLine($"[服务器] {msg}");
            _server.OnClientConnected += (session, name) => 
                Console.WriteLine($"[连接] 客户端 {name} 已连接 ({session.SessionId})");
            _server.OnClientDisconnected += session => 
                Console.WriteLine($"[断开] 客户端 {session.DeviceName} 已断开");
            _server.Start();
            
            // 创建屏幕捕获
            _capture = new ScreenCapture(0, 60);
            _capture.OnFrameCaptured += OnFrameCaptured;
            _capture.OnError += err => Console.WriteLine($"[捕获] 错误: {err}");
            
            // 创建 H.264 编码器
            _encoder = new H264Encoder(
                _capture.ScreenWidth, 
                _capture.ScreenHeight, 
                60, 
                Protocol.DefaultBitrate);
            _encoder.OnEncodedFrame += (data, isKeyFrame) =>
            {
                _server.BroadcastVideoFrame(data, isKeyFrame);
            };
            _encoder.Initialize();
            
            _isRunning = true;
            Console.WriteLine("初始化完成，等待客户端连接...");
            Console.WriteLine("按 Ctrl+C 退出");
            Console.WriteLine();
        }
        
        static void OnFrameCaptured(ScreenCapture.ScreenFrame frame)
        {
            try
            {
                _encoder.EncodeFrame(frame.PixelData, frame.Timestamp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[编码] 错误: {ex.Message}");
            }
        }
        
        static async Task Run()
        {
            // 启动屏幕捕获
            _capture.StartCapture();
            
            while (_isRunning)
            {
                await Task.Delay(1000);
                
                // 显示状态
                Console.Title = $"GR 扩展屏幕 | {_capture.ScreenWidth}x{_capture.ScreenHeight} | 运行中";
            }
        }
        
        static void Shutdown()
        {
            Console.WriteLine("\n正在关闭...");
            _isRunning = false;
            
            _capture?.StopCapture();
            _capture?.Dispose();
            
            _encoder?.Dispose();
            _server?.Stop();
            _server?.Dispose();
            
            Console.WriteLine("已关闭");
        }
    }
}
