using System;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace ScreenServer
{
    /// <summary>
    /// 通信协议定义
    /// </summary>
    public static class Protocol
    {
        // Magic bytes "GR"
        public const ushort Magic = 0x4752;
        
        // 端口定义
        public const int ControlPort = 33891;   // 控制通道
        public const int VideoPort = 33892;     // 视频数据
        public const int InputPort = 33893;     // 输入回传
        public const int DiscoveryPort = 33894; // 设备发现
        
        // 数据包类型
        public const byte TypeVideoFrame = 0x01;
        public const byte TypeAudioFrame = 0x02;
        public const byte TypeInputEvent = 0x03;
        public const byte TypeControlCmd = 0x04;
        public const byte TypeDiscovery = 0x05;
        public const byte TypeConfig = 0x06;
        
        // 控制命令
        public const byte CmdPairRequest = 0x01;
        public const byte CmdPairResponse = 0x02;
        public const byte CmdStartStream = 0x03;
        public const byte CmdStopStream = 0x04;
        public const byte CmdQualityChange = 0x05;
        public const byte CmdDisconnect = 0x06;
        
        // 包头大小: Magic(2) + Type(1) + Length(4) = 7 bytes
        public const int HeaderSize = 7;
        
        // 最大包大小 4MB
        public const int MaxPacketSize = 4 * 1024 * 1024;
        
        // 默认视频质量
        public const int DefaultBitrate = 8_000_000; // 8 Mbps
        public const int DefaultFps = 60;
        public const int DefaultWidth = 1920;
        public const int DefaultHeight = 1080;
    }

    /// <summary>
    /// 视频帧数据包
    /// </summary>
    public class VideoFramePacket
    {
        public long Timestamp { get; set; }
        public int FrameNumber { get; set; }
        public byte[] H264Data { get; set; }
        public bool IsKeyFrame { get; set; }
        
        public byte[] Serialize()
        {
            // Header(7) + Timestamp(8) + FrameNumber(4) + IsKeyFrame(1) + DataLength(4) + Data
            int totalSize = Protocol.HeaderSize + 8 + 4 + 1 + 4 + H264Data.Length;
            byte[] packet = new byte[totalSize];
            
            // Magic
            BitConverter.GetBytes(Protocol.Magic).CopyTo(packet, 0);
            // Type
            packet[2] = Protocol.TypeVideoFrame;
            // Length (payload only, not including header)
            BitConverter.GetBytes(totalSize - Protocol.HeaderSize).CopyTo(packet, 3);
            // Timestamp
            BitConverter.GetBytes(Timestamp).CopyTo(packet, 7);
            // FrameNumber
            BitConverter.GetBytes(FrameNumber).CopyTo(packet, 15);
            // IsKeyFrame
            packet[19] = (byte)(IsKeyFrame ? 1 : 0);
            // DataLength
            BitConverter.GetBytes(H264Data.Length).CopyTo(packet, 20);
            // Data
            H264Data.CopyTo(packet, 24);
            
            return packet;
        }
    }

    /// <summary>
    /// 输入事件数据包
    /// </summary>
    public class InputEventPacket
    {
        public const byte InputTypeTouchDown = 0x01;
        public const byte InputTypeTouchMove = 0x02;
        public const byte InputTypeTouchUp = 0x03;
        public const byte InputTypeKeyDown = 0x04;
        public const byte InputTypeKeyUp = 0x05;
        public const byte InputTypeMouseMove = 0x06;
        public const byte InputTypeMouseDown = 0x07;
        public const byte InputTypeMouseUp = 0x08;
        public const byte InputTypeMouseWheel = 0x09;
        
        public byte InputType { get; set; }
        public float X { get; set; }        // 归一化坐标 0-1
        public float Y { get; set; }        // 归一化坐标 0-1
        public int KeyCode { get; set; }
        public int Delta { get; set; }      // 滚轮增量
        
        public byte[] Serialize()
        {
            // Header(7) + InputType(1) + X(4) + Y(4) + KeyCode(4) + Delta(4)
            int payloadSize = 1 + 4 + 4 + 4 + 4;
            byte[] packet = new byte[Protocol.HeaderSize + payloadSize];
            
            BitConverter.GetBytes(Protocol.Magic).CopyTo(packet, 0);
            packet[2] = Protocol.TypeInputEvent;
            BitConverter.GetBytes(payloadSize).CopyTo(packet, 3);
            packet[7] = InputType;
            BitConverter.GetBytes(X).CopyTo(packet, 8);
            BitConverter.GetBytes(Y).CopyTo(packet, 12);
            BitConverter.GetBytes(KeyCode).CopyTo(packet, 16);
            BitConverter.GetBytes(Delta).CopyTo(packet, 20);
            
            return packet;
        }
        
        public static InputEventPacket Deserialize(byte[] data, int offset)
        {
            return new InputEventPacket
            {
                InputType = data[offset],
                X = BitConverter.ToSingle(data, offset + 1),
                Y = BitConverter.ToSingle(data, offset + 5),
                KeyCode = BitConverter.ToInt32(data, offset + 9),
                Delta = BitConverter.ToInt32(data, offset + 13)
            };
        }
    }

    /// <summary>
    /// 控制命令数据包
    /// </summary>
    public class ControlPacket
    {
        public byte Command { get; set; }
        public byte[] Payload { get; set; }
        
        public byte[] Serialize()
        {
            int payloadSize = 1 + (Payload?.Length ?? 0);
            byte[] packet = new byte[Protocol.HeaderSize + payloadSize];
            
            BitConverter.GetBytes(Protocol.Magic).CopyTo(packet, 0);
            packet[2] = Protocol.TypeControlCmd;
            BitConverter.GetBytes(payloadSize).CopyTo(packet, 3);
            packet[7] = Command;
            if (Payload != null)
                Payload.CopyTo(packet, 8);
            
            return packet;
        }
    }
}
