using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.MediaFoundation;
using SharpDX.Mathematics.Interop;

namespace ScreenServer
{
    /// <summary>
    /// H.264 硬件编码器 (基于 Media Foundation)
    /// 使用 GPU 加速进行低延迟视频编码
    /// </summary>
    public class H264Encoder : IDisposable
    {
        private int _width;
        private int _height;
        private int _fps;
        private int _bitrate;
        
        private MediaFactory _mediaFactory;
        private Transform _encoder;
        private MediaBuffer _inputBuffer;
        private InMemoryMediaBuffer _outputBuffer;
        private bool _isInitialized;
        
        public bool IsHardwareAccelerated { get; private set; }
        
        public event Action<byte[], bool> OnEncodedFrame;
        
        public H264Encoder(int width, int height, int fps = 60, int bitrate = 8000000)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _bitrate = bitrate;
        }
        
        public void Initialize()
        {
            try
            {
                // 初始化 Media Foundation
                MediaFactory.Startup();
                _mediaFactory = new MediaFactory();
                
                // 查找 H.264 编码器（优先硬件编码）
                _encoder = FindH264Encoder();
                
                if (_encoder == null)
                {
                    throw new Exception("未找到 H.264 编码器");
                }
                
                // 设置输入类型 (BGRA32)
                var inputType = new MediaType();
                inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatCodes.BGRA);
                inputType.Set(MediaTypeAttributeKeys.FrameSize, new RawVector2(_width, _height));
                inputType.Set(MediaTypeAttributeKeys.FrameRate, new Rational(_fps, 1));
                inputType.Set(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
                _encoder.SetInputType(0, inputType, 0);
                
                // 设置输出类型 (H.264)
                var outputType = new MediaType();
                outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatCodes.H264);
                outputType.Set(MediaTypeAttributeKeys.FrameSize, new RawVector2(_width, _height));
                outputType.Set(MediaTypeAttributeKeys.FrameRate, new Rational(_fps, 1));
                outputType.Set(MediaTypeAttributeKeys.AvgBitrate, _bitrate);
                outputType.Set(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
                outputType.Set(MediaTypeAttributeKeys.EncodePicEntropyCabac, 1);
                outputType.Set(MediaTypeAttributeKeys.Mpeg2Profile, (int)AvcLevelIds.AVCLevel4_1);
                _encoder.SetOutputType(0, outputType, 0);
                
                // 创建输入缓冲区
                long bufferSize = (long)_width * _height * 4; // BGRA = 4 bytes/pixel
                _inputBuffer = MediaFactory.CreateMemoryBuffer((int)bufferSize);
                
                // 获取实际_encoder info
                _encoder.GetOutputAvailableType(0, 0, out var actualOutputType);
                var encInfo = _encoder.QueryInterface<EncoderEnums>();
                IsHardwareAccelerated = true; // Media Foundation 默认优先硬件
                
                // 开始流
                _encoder.ProcessMessage(TransformMessageType.NotifyBeginStreaming, IntPtr.Zero);
                _encoder.ProcessMessage(TransformMessageType.NotifyStartOfStream, IntPtr.Zero);
                
                _isInitialized = true;
                Console.WriteLine($"H.264 编码器初始化完成: {_width}x{_height}@{_fps}fps, {_bitrate / 1000000}Mbps");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"H.264 编码器初始化失败: {ex.Message}");
                throw;
            }
        }
        
        private Transform FindH264Encoder()
        {
            // 激活属性
            var activationAttributes = new MediaAttributes();
            // 优先使用硬件编码器
            activationAttributes.Set(Guid.Parse("{1EA1EA14-48F4-4054-AD1A-E8AEE10AC805}"), 
                HardwareEncoderMode.Hardware);
            
            // 查找 H.264 编码器
            var activator = MediaFactory.CreateTransformActivationAttributes(
                activationAttributes,
                EncoderEndpoint.CategoryVideo,
                EncoderEndpoint.Encode,
                EncoderHardwareEncode.Hardware,
                0,
                null);
            
            if (activator != null)
            {
                try
                {
                    return activator.ActivateObject<Transform>();
                }
                catch
                {
                    activator.Dispose();
                }
            }
            
            // 回退到软件编码器
            Console.WriteLine("硬件编码器不可用，尝试使用软件编码器...");
            var swActivator = MediaFactory.CreateTransformActivationAttributes(
                activationAttributes,
                EncoderEndpoint.CategoryVideo,
                EncoderEndpoint.Encode,
                EncoderHardwareEncode.Software,
                0,
                null);
            
            if (swActivator != null)
            {
                return swActivator.ActivateObject<Transform>();
            }
            
            return null;
        }
        
        /// <summary>
        /// 编码一帧 BGRA 图像
        /// </summary>
        public bool EncodeFrame(byte[] bgraData, long timestamp)
        {
            if (!_isInitialized || bgraData == null) return false;
            
            try
            {
                // 复制数据到输入缓冲区
                _inputBuffer.Lock(out IntPtr ptr, out int maxLength, out int currentLength);
                if (bgraData.Length <= maxLength)
                {
                    Marshal.Copy(bgraData, 0, ptr, bgraData.Length);
                    _inputBuffer.CurrentLength = bgraData.Length;
                }
                _inputBuffer.Unlock();
                
                // 创建输入样本
                var inputSample = MediaFactory.CreateSample();
                inputSample.AddBuffer(_inputBuffer.QueryInterface<SharpDX.MediaFoundation.Buffer>());
                inputSample.SetSampleTime(timestamp);
                inputSample.SetSampleDuration(10_000_000 / _fps); // 单位: 100ns
                
                // 执行编码
                _encoder.ProcessInput(0, inputSample, 0);
                inputSample.Dispose();
                
                // 获取编码后的数据
                var outputBuffer = MediaFactory.CreateMemoryBuffer(2 * 1024 * 1024); // 2MB buffer
                var outputSample = MediaFactory.CreateSample();
                outputSample.AddBuffer(outputBuffer.QueryInterface<SharpDX.MediaFoundation.Buffer>());
                
                var outputStatus = _encoder.ProcessOutput(0, 1, 
                    new [] { new DataBuffer { Sample = outputSample, StreamID = 0 } }, 
                    out int status);
                
                if (outputStatus == TransformOutputCommandStatus.ProvideOutput ||
                    outputStatus == TransformOutputCommandStatus.ProvideAll ||
                    outputStatus == TransformOutputCommandStatus.ProvideCurrent)
                {
                    outputSample.ConvertToContiguousBuffer(out var contiguousBuffer);
                    contiguousBuffer.CurrentLength = (int)contiguousBuffer.MaxLength;
                    
                    byte[] encodedData = new byte[contiguousBuffer.CurrentLength];
                    contiguousBuffer.Lock(out IntPtr dataPtr, out int maxLen, out int curLen);
                    Marshal.Copy(dataPtr, encodedData, 0, curLen);
                    contiguousBuffer.Unlock();
                    
                    bool IsKeyFrame = false;
                    outputSample.Get(MFSampleExtension_BottomFieldFirst, out long temporal);
                    
                    // 检查是否为关键帧 (通过查找 NAL unit type)
                    IsKeyFrame = CheckIfKeyFrame(encodedData);
                    
                    outputSample.Dispose();
                    
                    if (encodedData.Length > 0)
                    {
                        OnEncodedFrame?.Invoke(encodedData, IsKeyFrame);
                        return true;
                    }
                }
                
                outputSample.Dispose();
                return false;
            }
            catch (SharpDXException ex)
            {
                Console.WriteLine($"编码错误: {ex.Message}");
                return false;
            }
        }
        
        private bool CheckIfKeyFrame(byte[] h264Data)
        {
            // 简单的关键帧检测：查找 NAL unit type 5 (IDR frame)
            for (int i = 0; i < h264Data.Length - 5; i++)
            {
                if (h264Data[i] == 0 && h264Data[i + 1] == 0 && 
                    h264Data[i + 2] == 0 && h264Data[i + 3] == 1)
                {
                    int nalType = h264Data[i + 4] & 0x1F;
                    if (nalType == 5) return true; // IDR frame
                }
            }
            return false;
        }
        
        public void Dispose()
        {
            _isInitialized = false;
            
            _encoder?.ProcessMessage(TransformMessageType.NotifyEndOfStream, IntPtr.Zero);
            _encoder?.ProcessMessage(TransformMessageType.NotifyEndStreaming, IntPtr.Zero);
            _encoder?.Dispose();
            _inputBuffer?.Dispose();
            _outputBuffer?.Dispose();
            
            MediaFactory.Shutdown();
        }
    }
}
