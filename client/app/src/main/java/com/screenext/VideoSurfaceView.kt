package com.screenext.client

import android.graphics.Rect
import android.util.Log
import android.view.MotionEvent
import android.view.Surface
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.view.KeyEvent
import java.util.concurrent.atomic.AtomicBoolean

/**
 * 视频渲染视图 - 负责显示解码后的视频并捕获触摸/键盘输入
 */
class VideoSurfaceView(context: android.content.Context) : SurfaceView(context), SurfaceHolder.Callback {
    
    companion object {
        private const val TAG = "VideoSurfaceView"
    }
    
    private var decoder: H264Decoder? = null
    private var networkClient: NetworkClient? = null
    
    private val isStreaming = AtomicBoolean(false)
    private var surfaceReady = false
    
    // 屏幕尺寸
    private var screenWidth = 0
    private var screenHeight = 0
    private var videoWidth = 1920
    private var videoHeight = 1080
    
    // 渲染回调
    var onStreamingStarted: (() -> Unit)? = null
    var onStreamingStopped: (() -> Unit)? = null
    var onError: ((String) -> Unit)? = null
    
    init {
        holder.addCallback(this)
        setOnTouchListener(TouchListener())
        isFocusableInTouchMode = true
        requestFocus()
        setOnKeyListener(KeyListener())
    }
    
    override fun surfaceCreated(holder: SurfaceHolder) {
        surfaceReady = true
        Log.i(TAG, "Surface 创建完成")
    }
    
    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        screenWidth = width
        screenHeight = height
        Log.i(TAG, "Surface 尺寸改变: ${width}x${height}")
    }
    
    override fun surfaceDestroyed(holder: SurfaceHolder) {
        surfaceReady = false
        decoder?.release()
        Log.i(TAG, "Surface 销毁")
    }
    
    /**
     * 连接服务器并开始接收视频流
     */
    fun connectAndStream(host: String, port: Int = NetworkClient.CONTROL_PORT) {
        if (isStreaming.get()) return
        
        networkClient = NetworkClient.Builder()
            .setServer(host)
            .setPort(port)
            .build()
        
        networkClient?.onConnected = {
            Log.i(TAG, "已连接，初始化解码器...")
            initDecoder()
            networkClient?.startStreaming()
        }
        
        networkClient?.onVideoFrame = { data, timestamp, isKeyFrame ->
            decoder?.queueFrame(data, timestamp, isKeyFrame)
        }
        
        networkClient?.onError = { error ->
            Log.e(TAG, "网络错误: $error")
            onError?.invoke(error)
        }
        
        networkClient?.onDisconnected = {
            isStreaming.set(false)
            onStreamingStopped?.invoke()
        }
        
        networkClient?.connect()
        isStreaming.set(true)
        onStreamingStarted?.invoke()
    }
    
    /**
     * 初始化解码器
     */
    private fun initDecoder() {
        try {
            decoder = H264Decoder(holder.surface)
            decoder?.initialize(videoWidth, videoHeight)
            decoder?.onFirstFrameDecoded = {
                Log.i(TAG, "首帧解码完成")
            }
            Log.i(TAG, "解码器已初始化")
        } catch (e: Exception) {
            Log.e(TAG, "初始化解码器失败: ${e.message}")
            onError?.invoke("解码器初始化失败: ${e.message}")
        }
    }
    
    /**
     * 断开连接
     */
    fun disconnect() {
        if (!isStreaming.get()) return
        
        networkClient?.stopStreaming()
        networkClient?.disconnect()
        decoder?.release()
        
        isStreaming.set(false)
        onStreamingStopped?.invoke()
    }
    
    /**
     * 触摸事件监听器 - 将触摸坐标归一化后发送到主机
     */
    private inner class TouchListener : OnTouchListener {
        override fun onTouch(v: View?, event: MotionEvent?): Boolean {
            if (event == null || !isStreaming.get()) return false
            
            // 归一化坐标到 0-1
            val normalizedX = event.x / width
            val normalizedY = event.y / height
            
            val eventType = when (event.action) {
                MotionEvent.ACTION_DOWN -> InputEventType.TOUCH_DOWN
                MotionEvent.ACTION_MOVE -> InputEventType.TOUCH_MOVE
                MotionEvent.ACTION_UP -> InputEventType.TOUCH_UP
                MotionEvent.ACTION_CANCEL -> InputEventType.TOUCH_UP
                else -> return false
            }
            
            sendInputEvent(eventType, normalizedX, normalizedY)
            return true
        }
    }
    
    /**
     * 按键事件监听器
     */
    private inner class KeyListener : OnKeyListener {
        override fun onKey(v: View?, keyCode: Int, event: KeyEvent?): Boolean {
            if (event == null || !isStreaming.get()) return false
            
            val normalizedX = 0.5f
            val normalizedY = 0.5f
            
            val eventType = when (event.action) {
                KeyEvent.ACTION_DOWN -> InputEventType.KEY_DOWN
                KeyEvent.ACTION_UP -> InputEventType.KEY_UP
                else -> return false
            }
            
            sendInputEvent(eventType, normalizedX, normalizedY, keyCode)
            return true
        }
    }
    
    /**
     * 发送输入事件到服务器
     */
    private fun sendInputEvent(
        eventType: InputEventType,
        x: Float,
        y: Float,
        keyCode: Int = 0,
        delta: Int = 0
    ) {
        val type = when (eventType) {
            InputEventType.TOUCH_DOWN -> NetworkClient.TYPE_INPUT_EVENT
            InputEventType.TOUCH_MOVE -> NetworkClient.TYPE_INPUT_EVENT
            InputEventType.TOUCH_UP -> NetworkClient.TYPE_INPUT_EVENT
            InputEventType.KEY_DOWN -> NetworkClient.TYPE_INPUT_EVENT
            InputEventType.KEY_UP -> NetworkClient.TYPE_INPUT_EVENT
            InputEventType.MOUSE_WHEEL -> NetworkClient.TYPE_INPUT_EVENT
        }
        
        // 根据事件类型设置具体子类型
        val subType = when (eventType) {
            InputEventType.TOUCH_DOWN -> 0x01.toByte() // TouchDown
            InputEventType.TOUCH_MOVE -> 0x02.toByte() // TouchMove
            InputEventType.TOUCH_UP -> 0x03.toByte() // TouchUp
            InputEventType.KEY_DOWN -> 0x04.toByte() // KeyDown
            InputEventType.KEY_UP -> 0x05.toByte() // KeyUp
            InputEventType.MOUSE_WHEEL -> 0x09.toByte() // MouseWheel
        }
        
        networkClient?.sendInputEvent(subType, x, y, keyCode, delta)
    }
    
    enum class InputEventType {
        TOUCH_DOWN, TOUCH_MOVE, TOUCH_UP,
        KEY_DOWN, KEY_UP, MOUSE_WHEEL
    }
}

/**
 * 输入事件封装
 */
data class InputEvent(
    val type: VideoSurfaceView.InputEventType,
    val x: Float,
    val y: Float,
    val keyCode: Int = 0,
    val delta: Int = 0
)
