package com.screenext.client

import android.os.Handler
import android.os.Looper
import android.util.Log
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicBoolean

/**
 * 网络客户端 - 连接 Windows 主机端，接收视频流并发送输入事件
 */
class NetworkClient private constructor(
    private val serverHost: String,
    private val serverPort: Int
) {
    companion object {
        private const val TAG = "NetworkClient"
        const val CONTROL_PORT = 33891
        const val VIDEO_PORT = 33892
        const val INPUT_PORT = 33893
        
        const val MAGIC: Short = 0x4752 // "GR"
        const val TYPE_VIDEO_FRAME: Byte = 0x01
        const val TYPE_INPUT_EVENT: Byte = 0x03
        const val TYPE_CONTROL_CMD: Byte = 0x04
        const val HEADER_SIZE = 7
        
        // 构建器模式创建客户端
        class Builder {
            private var host: String = ""
            private var port: Int = CONTROL_PORT
            private var deviceName: String = android.os.Build.MODEL
            
            fun setServer(host: String) = apply { this.host = host }
            fun setPort(port: Int) = apply { this.port = port }
            fun setDeviceName(name: String) = apply { this.deviceName = name }
            
            fun build(): NetworkClient {
                require(host.isNotEmpty()) { "服务器地址不能为空" }
                return NetworkClient(host, port)
            }
        }
    }
    
    private var controlSocket: Socket? = null
    private var inputSocket: Socket? = null
    private var videoSocket: DatagramSocket? = null
    
    private var controlOutputStream: OutputStream? = null
    private var controlInputStream: InputStream? = null
    private var inputOutputStream: OutputStream? = null
    
    private val isConnected = AtomicBoolean(false)
    private val isRunning = AtomicBoolean(false)
    
    private val handler = Handler(Looper.getMainLooper())
    
    // 接收缓冲区
    private val receiveBuffer = ByteArray(4 * 1024 * 1024) // 4MB
    private val videoPacketBuffer = ByteArray(65535) // UDP 包最大 65535
    
    // 回调
    var onConnected: (() -> Unit)? = null
    var onDisconnected: (() -> Unit)? = null
    var onVideoFrame: ((ByteArray, Long, Boolean) -> Unit)? = null
    var onError: ((String) -> Unit)? = null
    var onLog: ((String) -> Unit)? = null
    
    /**
     * 连接到服务器
     */
    fun connect() {
        Thread {
            try {
                // 1. 建立控制连接
                log("正在连接服务器 $serverHost:$serverPort...")
                controlSocket = Socket(serverHost, serverPort)
                controlOutputStream = controlSocket!!.getOutputStream()
                controlInputStream = controlSocket!!.getInputStream()
                
                // 2. 发送设备信息
                val deviceInfo = android.os.Build.MODEL.toByteArray(Charsets.UTF_8)
                val header = createHeader(TYPE_CONTROL_CMD, deviceInfo.size)
                controlOutputStream?.write(header)
                controlOutputStream?.write(deviceInfo)
                controlOutputStream?.flush()
                
                // 3. 建立输入连接
                inputSocket = Socket(serverHost, INPUT_PORT)
                inputOutputStream = inputSocket!!.getOutputStream()
                
                // 4. 建立 UDP 视频接收
                videoSocket = DatagramSocket(VIDEO_PORT)
                videoSocket?.broadcast = true
                
                isConnected.set(true)
                isRunning.set(true)
                
                handler.post { onConnected?.invoke() }
                log("已连接到服务器 $serverHost")
                
                // 5. 启动视频接收线程
                startVideoReceiver()
                
                // 6. 启动控制命令监听
                startControlListener()
                
            } catch (e: Exception) {
                Log.e(TAG, "连接失败: ${e.message}")
                handler.post { 
                    onError?.invoke("连接失败: ${e.message}")
                    onDisconnected?.invoke()
                }
            }
        }.apply {
            isDaemon = true
            start()
        }
    }
    
    /**
     * 创建数据包头部
     */
    private fun createHeader(type: Byte, payloadLength: Int): ByteArray {
        val header = ByteArray(HEADER_SIZE)
        val buffer = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN)
        buffer.putShort(MAGIC)
        buffer.put(type)
        buffer.putInt(payloadLength)
        return header
    }
    
    /**
     * 请求开始流传输
     */
    fun startStreaming() {
        if (!isConnected.get()) return
        
        val packet = createHeader(TYPE_CONTROL_CMD, 1)
        val cmd = byteArrayOf(0x03) // CmdStartStream
        
        try {
            controlOutputStream?.write(packet)
            controlOutputStream?.write(cmd)
            controlOutputStream?.flush()
            log("请求开始流传输")
        } catch (e: Exception) {
            log("发送开始流命令失败: ${e.message}")
        }
    }
    
    /**
     * 请求停止流传输
     */
    fun stopStreaming() {
        if (!isConnected.get()) return
        
        val packet = createHeader(TYPE_CONTROL_CMD, 1)
        val cmd = byteArrayOf(0x04) // CmdStopStream
        
        try {
            controlOutputStream?.write(packet)
            controlOutputStream?.write(cmd)
            controlOutputStream?.flush()
            log("请求停止流传输")
        } catch (e: Exception) {
            log("发送停止流命令失败: ${e.message}")
        }
    }
    
    /**
     * 发送输入事件到主机
     */
    fun sendInputEvent(eventType: Byte, x: Float, y: Float, keyCode: Int = 0, delta: Int = 0) {
        if (!isConnected.get()) return
        
        try {
            val payload = ByteArray(17)
            val buffer = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
            buffer.put(eventType)
            buffer.putFloat(x)
            buffer.putFloat(y)
            buffer.putInt(keyCode)
            buffer.putInt(delta)
            
            val header = createHeader(TYPE_INPUT_EVENT, payload.size)
            inputOutputStream?.write(header)
            inputOutputStream?.write(payload)
            inputOutputStream?.flush()
        } catch (e: Exception) {
            Log.e(TAG, "发送输入事件失败: ${e.message}")
        }
    }
    
    /**
     * 视频接收线程
     */
    private fun startVideoReceiver() {
        Thread {
            log("视频接收线程已启动")
            
            while (isRunning.get() && isConnected.get()) {
                try {
                    val packet = DatagramPacket(videoPacketBuffer, videoPacketBuffer.size)
                    videoSocket?.receive(packet)
                    
                    // 解析数据包
                    if (packet.length >= HEADER_SIZE) {
                        val data = packet.data.copyOfRange(0, packet.length)
                        parseVideoPacket(data)
                    }
                } catch (e: IOException) {
                    if (isRunning.get()) {
                        Log.e(TAG, "视频接收异常: ${e.message}")
                    }
                    break
                } catch (e: Exception) {
                    Log.e(TAG, "视频处理异常: ${e.message}")
                }
            }
            
            log("视频接收线程已结束")
        }.apply {
            isDaemon = true
            priority = Thread.MAX_PRIORITY
            start()
        }
    }
    
    /**
     * 解析视频数据包
     * 格式: Magic(2) + Type(1) + Length(4) + Timestamp(8) + FrameNumber(4) + IsKeyFrame(1) + Data
     */
    private fun parseVideoPacket(data: ByteArray) {
        try {
            val buffer = ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN)
            
            // 验证 Magic
            if (buffer.short != MAGIC) return
            val type = buffer.get()
            val length = buffer.int
            
            if (type != TYPE_VIDEO_FRAME) return
            
            // 检查长度
            if (data.length < 24) return // 至少要有头部信息
            
            val timestamp = buffer.long
            val frameNumber = buffer.int
            val isKeyFrame = buffer.get() != 0.toByte()
            
            // 获取 H.264 数据
            val dataLength = data.size - 24
            val h264Data = ByteArray(dataLength)
            System.arraycopy(data, 24, h264Data, 0, dataLength)
            
            // 回调到主线程
            handler.post {
                onVideoFrame?.invoke(h264Data, timestamp, isKeyFrame)
            }
        } catch (e: Exception) {
            Log.e(TAG, "解析视频包异常: ${e.message}")
        }
    }
    
    /**
     * 控制命令监听
     */
    private fun startControlListener() {
        Thread {
            try {
                val headerBuffer = ByteArray(HEADER_SIZE)
                
                while (isRunning.get() && isConnected.get()) {
                    val bytesRead = controlInputStream?.read(headerBuffer) ?: -1
                    if (bytesRead < HEADER_SIZE) break
                    
                    val buffer = ByteBuffer.wrap(headerBuffer).order(ByteOrder.LITTLE_ENDIAN)
                    val magic = buffer.short
                    val type = buffer.get()
                    val length = buffer.int
                    
                    if (magic != MAGIC) continue
                    
                    // 读取 payload
                    if (length > 0 && length < 1024) {
                        val payload = ByteArray(length)
                        controlInputStream?.read(payload)
                        handleControlResponse(payload)
                    }
                }
            } catch (e: Exception) {
                if (isRunning.get()) {
                    Log.e(TAG, "控制监听异常: ${e.message}")
                    handler.post { 
                        onError?.invoke("连接断开: ${e.message}")
                        onDisconnected?.invoke()
                    }
                }
            }
        }.apply {
            isDaemon = true
            start()
        }
    }
    
    private fun handleControlResponse(payload: ByteArray) {
        // 处理服务器响应
        val response = String(payload, Charsets.UTF_8)
        log("服务器响应: $response")
    }
    
    /**
     * 断开连接
     */
    fun disconnect() {
        isRunning.set(false)
        isConnected.set(false)
        
        try { controlSocket?.close() } catch (_: Exception) {}
        try { inputSocket?.close() } catch (_: Exception) {}
        try { videoSocket?.close() } catch (_: Exception) {}
        
        handler.post { onDisconnected?.invoke() }
        log("已断开连接")
    }
    
    fun isConnected(): Boolean = isConnected.get()
    
    private fun log(message: String) {
        Log.d(TAG, message)
        handler.post { onLog?.invoke(message) }
    }
}
