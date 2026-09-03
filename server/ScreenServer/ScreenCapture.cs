using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using System.Drawing;
using System.Drawing.Imaging;

namespace ScreenServer
{
    /// <summary>
    /// DXGI Desktop Duplication API 屏幕捕获
    /// 提供高性能的桌面画面捕获能力
    /// </summary>
    public class ScreenCapture : IDisposable
    {
        private Factory1 _factory;
        private Adapter1 _adapter;
        private Device _device;
        private Output _output;
        private Output1 _output1;
        private OutputDuplication _duplication;
        private Texture2DDescription _textureDesc;
        
        private bool _isRunning;
        private Thread _captureThread;
        private CancellationTokenSource _cts;
        
        // 帧率控制
        private readonly int _targetFps;
        private readonly double _frameInterval;
        
        // 输出事件
        public event Action<ScreenFrame> OnFrameCaptured;
        public event Action<string> OnError;
        
        // 显示器信息
        public int ScreenWidth { get; private set; }
        public int ScreenHeight { get; private set; }
        public string DisplayName { get; private set; }
        
        public class ScreenFrame
        {
            public byte[] PixelData { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public long Timestamp { get; set; }
            public int RowPitch { get; set; }
        }
        
        public ScreenCapture(int displayIndex = 0, int targetFps = 60)
        {
            _targetFps = targetFps;
            _frameInterval = 1000.0 / targetFps;
            InitializeDXGI(displayIndex);
        }
        
        private void InitializeDXGI(int displayIndex)
        {
            try
            {
                // 创建 DXGI 工厂
                _factory = new Factory1();
                
                // 获取第一个适配器和输出（显示器）
                _adapter = new Adapter1(_factory.GetAdapter1(0));
                
                _output = _adapter.GetOutput(displayIndex);
                _output1 = _output.QueryInterface<Output1>();
                
                // 获取显示器信息
                var desc = _output1.Description;
                ScreenWidth = desc.DesktopBounds.Right - desc.DesktopBounds.Left;
                ScreenHeight = desc.DesktopBounds.Bottom - desc.DesktopBounds.Top;
                DisplayName = desc.DeviceName;
                
                Console.WriteLine($"显示器: {DisplayName} ({ScreenWidth}x{ScreenHeight})");
                
                // 创建 D3D11 设备
                _device = new Device(_adapter, DeviceCreationFlags.BgraSupport);
                
                // 创建 Desktop Duplication
                _duplication = _output1.DuplicateOutput(_device);
                
                // 设置纹理描述（用于 CPU 读取）
                _textureDesc = new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = ScreenWidth,
                    Height = ScreenHeight,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = Usage.Staging
                };
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"初始化 DXGI 失败: {ex.Message}");
                throw;
            }
        }
        
        public void StartCapture()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.Start();
            
            Console.WriteLine($"屏幕捕获已启动，目标帧率: {_targetFps} FPS");
        }
        
        public void StopCapture()
        {
            _isRunning = false;
            _cts?.Cancel();
            _captureThread?.Join(2000);
            Console.WriteLine("屏幕捕获已停止");
        }
        
        private void CaptureLoop()
        {
            var token = _cts.Token;
            long lastCaptureTime = 0;
            
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    double elapsed = currentTime - lastCaptureTime;
                    
                    if (elapsed < _frameInterval)
                    {
                        int sleepTime = (int)(_frameInterval - elapsed);
                        if (sleepTime > 0)
                            Thread.Sleep(sleepTime);
                    }
                    
                    ScreenFrame frame = CaptureFrame();
                    if (frame != null)
                    {
                        lastCaptureTime = currentTime;
                        frame.Timestamp = currentTime;
                        OnFrameCaptured?.Invoke(frame);
                    }
                }
                catch (SharpDXException ex)
                {
                    if (ex.ResultCode.ResultCode == SharpDX.DXGI.ResultCode.AccessDenied.ResultCode)
                    {
                        OnError?.Invoke("屏幕访问被拒绝，可能正在运行受保护的内容");
                    }
                    else if (ex.ResultCode.ResultCode == SharpDX.DXGI.ResultCode.AccessLost.ResultCode)
                    {
                        OnError?.Invoke("桌面复制会话丢失，尝试重新初始化");
                        ReinitializeDuplication();
                    }
                    else if (ex.ResultCode.ResultCode == SharpDX.DXGI.ResultCode.WaitTimeout.ResultCode)
                    {
                        // 超时，继续尝试
                        Thread.Sleep(1);
                    }
                    else
                    {
                        OnError?.Invoke($"DXGI 错误: {ex.Message}");
                        Thread.Sleep(16);
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"捕获异常: {ex.Message}");
                    Thread.Sleep(16);
                }
            }
        }
        
        private ScreenFrame CaptureFrame()
        {
            OutputDuplicateFrameInformation frameInfo;
            SharpDX.DXGI.Resource resource;
            
            // 获取下一帧（超时100ms）
            var result = _duplication.AcquireNextFrame(100, out frameInfo, out resource);
            
            if (result.RawFailure || resource == null)
                return null;
            
            try
            {
                // 获取纹理
                using (var texture = resource.QueryInterface<Texture2D>())
                {
                    // 创建暂存纹理用于 CPU 读取
                    var stagingTexture = new Texture2D(_device, _textureDesc);
                    
                    // 复制到暂存纹理
                    _device.ImmediateContext.CopyResource(texture, stagingTexture);
                    
                    // 映射纹理读取像素数据
                    var mapped = _device.ImmediateContext.MapSubresource(
                        stagingTexture,
                        0,
                        MapMode.Read,
                        SharpDX.Direct3D11.MapFlags.None);
                    
                    byte[] pixelData = new byte[ScreenHeight * mapped.RowPitch];
                    
                    // 逐行复制（处理步长对齐）
                    unsafe
                    {
                        byte* srcPtr = (byte*)mapped.DataPointer;
                        fixed (byte* dstPtr = pixelData)
                        {
                            for (int y = 0; y < ScreenHeight; y++)
                            {
                                Buffer.MemoryCopy(
                                    srcPtr + y * mapped.RowPitch,
                                    dstPtr + y * mapped.RowPitch,
                                    mapped.RowPitch,
                                    mapped.RowPitch);
                            }
                        }
                    }
                    
                    _device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                    stagingTexture.Dispose();
                    
                    return new ScreenFrame
                    {
                        PixelData = pixelData,
                        Width = ScreenWidth,
                        Height = ScreenHeight,
                        RowPitch = mapped.RowPitch,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                }
            }
            finally
            {
                resource.Dispose();
                _duplication.ReleaseFrame();
            }
        }
        
        private void ReinitializeDuplication()
        {
            try
            {
                _duplication?.Dispose();
                _duplication = _output1.DuplicateOutput(_device);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"重新初始化失败: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            StopCapture();
            
            _duplication?.Dispose();
            _output1?.Dispose();
            _output?.Dispose();
            _device?.Dispose();
            _adapter?.Dispose();
            _factory?.Dispose();
            _cts?.Dispose();
        }
    }
}
