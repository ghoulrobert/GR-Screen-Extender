package com.screenext.client

import android.content.Context
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer
import java.util.concurrent.ConcurrentLinkedQueue

/**
 * 硬件视频编码器 - 用于客户端回传的辅助编码器
 * 也可用于客户端端屏幕流式回传
 */
class HardwareEncoder(
    private val width: Int = 1920,
    private val height: Int = 1080,
    private val bitrate: Int = 4_000_000,
    private val fps: Int = 60
) {
    companion object {
        private const val TAG = "HardwareEncoder"
        private const val MIME_TYPE = "video/avc"
        private const val TIMEOUT_US = 10000L
    }
    
    private var codec: MediaCodec? = null
    private var isRunning = false
    
    fun initialize() {
        try {
            val format = MediaFormat.createVideoFormat(MIME_TYPE, width, height).apply {
                setInteger(
                    MediaFormat.KEY_COLOR_FORMAT,
                    MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible
                )
                setInteger(MediaFormat.KEY_BIT_RATE, bitrate)
                setInteger(MediaFormat.KEY_FRAME_RATE, fps)
                setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1) // 1秒关键帧间隔
                setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileHigh)
                setInteger(MediaFormat.KEY_LEVEL, MediaCodecInfo.CodecProfileLevel.AVCLevel41)
            }
            
            codec = MediaCodec.createEncoderByType(MIME_TYPE).apply {
                configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
                start()
            }
            
            isRunning = true
            Log.i(TAG, "编码器初始化完成: ${width}x${height} @ ${bitrate / 1000000}Mbps")
        } catch (e: Exception) {
            Log.e(TAG, "编码器初始化失败: ${e.message}")
            throw e
        }
    }
    
    fun inputSurface(): Surface? {
        return codec?.createInputSurface()
    }
    
    fun drainEncoder(endOfStream: Boolean = false): ByteArray? {
        if (!isRunning) return null
        
        val bufferInfo = MediaCodec.BufferInfo()
        
        while (true) {
            val outputBufferIndex = codec?.dequeueOutputBuffer(bufferInfo, TIMEOUT_US) ?: -1
            
            when {
                outputBufferIndex >= 0 -> {
                    val outputBuffer = codec?.getOutputBuffer(outputBufferIndex)
                    
                    if (outputBuffer != null) {
                        val data = ByteArray(bufferInfo.size)
                        outputBuffer.position(bufferInfo.offset)
                        outputBuffer.limit(bufferInfo.offset + bufferInfo.size)
                        outputBuffer.get(data)
                        
                        codec?.releaseOutputBuffer(outputBufferIndex, false)
                        
                        if (bufferInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) {
                            // SPS/PPS 头
                            return data
                        }
                        
                        return data
                    }
                }
                outputBufferIndex == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                    if (!endOfStream) break
                }
                outputBufferIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    Log.i(TAG, "输出格式改变: ${codec?.outputFormat}")
                }
            }
            
            if (breakLoop) break
        }
        
        return null
    }
    
    private var breakLoop = false
    
    fun stop() {
        isRunning = false
        breakLoop = true
        try { codec?.stop() } catch (_: Exception) {}
        codec?.release()
        codec = null
    }
}
