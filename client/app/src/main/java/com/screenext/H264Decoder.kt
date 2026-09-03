package com.screenext.client

import android.media.MediaCodec
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer
import java.util.concurrent.ConcurrentLinkedQueue

/**
 * H.264 硬件解码器 - 使用 Android MediaCodec
 * 负责接收主机端的 H.264 视频流并解码渲染到 Surface
 */
class H264Decoder(private val surface: Surface) {
    
    companion object {
        private const val TAG = "H264Decoder"
        private const val MIME_TYPE = "video/avc" // H.264
        private const val TIMEOUT_US = 10000L
    }
    
    private var codec: MediaCodec? = null
    private var isRunning = false
    private var width = 1920
    private var height = 1080
    
    // 待解码队列
    private val decodeQueue = ConcurrentLinkedQueue<FrameData>()
    
    // 音视频同步
    private var firstFrameTime = 0L
    private var baseTimestamp = 0L
    
    data class FrameData(
        val data: ByteArray,
        val timestamp: Long,
        val isKeyFrame: Boolean
    ) {
        override fun equals(other: Any?): Boolean {
            if (this === other) return true
            if (other !is FrameData) return false
            return timestamp == other.timestamp && isKeyFrame == other.isKeyFrame
        }
        override fun hashCode(): Int = timestamp.hashCode()
    }
    
    /**
     * 初始化解码器
     */
    fun initialize(frameWidth: Int = 1920, frameHeight: Int = 1080) {
        this.width = frameWidth
        this.height = frameHeight
        
        try {
            // 创建 H.264 解码器
            val format = MediaFormat.createVideoFormat(MIME_TYPE, width, height).apply {
                setInteger(MediaFormat.KEY_OPERATING_RATE, 60)
                setInteger(MediaFormat.KEY_PRIORITY, 0) // 实时优先级
            }
            
            codec = MediaCodec.createDecoderByType(MIME_TYPE).apply {
                configure(format, surface, null, 0)
                start()
            }
            
            isRunning = true
            
            // 启动解码线程
            Thread { decodeLoop() }.apply {
                isDaemon = true
                priority = Thread.MAX_PRIORITY
                start()
            }
            
            Log.i(TAG, "解码器初始化完成: ${width}x${height}")
        } catch (e: Exception) {
            Log.e(TAG, "解码器初始化失败: ${e.message}")
            throw e
        }
    }
    
    /**
     * 提交视频帧到解码队列
     */
    fun queueFrame(h264Data: ByteArray, timestamp: Long, isKeyFrame: Boolean) {
        if (!isRunning) return
        
        // 队列控制：避免堆积过多帧
        if (decodeQueue.size > 3) {
            decodeQueue.poll() // 丢弃旧帧
        }
        
        decodeQueue.offer(FrameData(h264Data, timestamp, isKeyFrame))
    }
    
    /**
     * 解码主循环
     */
    private fun decodeLoop() {
        val bufferInfo = MediaCodec.BufferInfo()
        
        while (isRunning) {
            try {
                // 获取输入缓冲区索引
                val inputBufferIndex = codec?.dequeueInputBuffer(TIMEOUT_US) ?: -1
                
                if (inputBufferIndex >= 0) {
                    val frameData = decodeQueue.poll()
                    
                    if (frameData != null) {
                        val inputBuffer = codec?.getInputBuffer(inputBufferIndex)
                        inputBuffer?.clear()
                        inputBuffer?.put(frameData.data)
                        
                        // 设置 PTS
                        val ptsUs = if (baseTimestamp == 0L) {
                            baseTimestamp = frameData.timestamp
                            0L
                        } else {
                            (frameData.timestamp - baseTimestamp) * 1000
                        }
                        
                        // 提交到解码器
                        codec?.queueInputBuffer(
                            inputBufferIndex,
                            0,
                            frameData.data.size,
                            ptsUs,
                            if (frameData.isKeyFrame) MediaCodec.BUFFER_FLAG_KEY_FRAME else 0
                        )
                    } else {
                        // 没有数据，发送空缓冲区保持时钟
                        codec?.queueInputBuffer(
                            inputBufferIndex,
                            0, 0, 0,
                            MediaCodec.BUFFER_FLAG_END_OF_STREAM
                        )
                    }
                }
                
                // 获取解码输出
                val outputBufferIndex = codec?.dequeueOutputBuffer(bufferInfo, TIMEOUT_US) ?: -1
                
                when {
                    outputBufferIndex >= 0 -> {
                        // 渲染到 Surface
                        codec?.releaseOutputBuffer(outputBufferIndex, true)
                        
                        if (firstFrameTime == 0L) {
                            firstFrameTime = System.currentTimeMillis()
                            onFirstFrameDecoded?.invoke()
                        }
                    }
                    outputBufferIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                        val newFormat = codec?.outputFormat
                        Log.i(TAG, "输出格式改变: $newFormat")
                    }
                    outputBufferIndex == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                        // 重试
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "解码异常: ${e.message}")
                try { Thread.sleep(16) } catch (_: InterruptedException) {}
            }
        }
    }
    
    var onFirstFrameDecoded: (() -> Unit)? = null
    
    /**
     * 动态调整分辨率
     */
    fun updateResolution(newWidth: Int, newHeight: Int) {
        if (newWidth == width && newHeight == height) return
        
        try {
            codec?.stop()
            codec?.release()
            
            width = newWidth
            height = newHeight
            
            val format = MediaFormat.createVideoFormat(MIME_TYPE, width, height)
            codec = MediaCodec.createDecoderByType(MIME_TYPE).apply {
                configure(format, surface, null, 0)
                start()
            }
            
            Log.i(TAG, "分辨率已更新: ${width}x${height}")
        } catch (e: Exception) {
            Log.e(TAG, "更新分辨率失败: ${e.message}")
        }
    }
    
    /**
     * 获取当前 FPS
     */
    fun getFps(): Float {
        // 实际实现需要统计帧间隔
        return 0f
    }
    
    /**
     * 释放解码器
     */
    fun release() {
        isRunning = false
        decodeQueue.clear()
        
        try {
            codec?.stop()
            codec?.release()
        } catch (e: Exception) {
            Log.e(TAG, "释放解码器异常: ${e.message}")
        }
        codec = null
        
        Log.i(TAG, "解码器已释放")
    }
}
