package com.screenext.client

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.View
import android.view.WindowManager
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

/**
 * 主界面 - GR 扩展屏幕 Android 客户端
 * 提供连接服务器、查看扩展屏功能
 */
class MainActivity : AppCompatActivity() {
    
    companion object {
        private const val TAG = "GRMainActivity"
        private const val PERMISSION_REQUEST_CODE = 100
    }
    
    private lateinit var serverIpInput: EditText
    private lateinit var connectButton: Button
    private lateinit var disconnectButton: Button
    private lateinit var statusText: TextView
    private lateinit var videoSurfaceView: VideoSurfaceView
    
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        
        // 保持屏幕常亮
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        
        setContentView(R.layout.activity_main)
        
        // 初始化视图
        serverIpInput = findViewById(R.id.serverIpInput)
        connectButton = findViewById(R.id.connectButton)
        disconnectButton = findViewById(R.id.disconnectButton)
        statusText = findViewById(R.id.statusText)
        videoSurfaceView = findViewById(R.id.videoSurfaceView)
        
        // 检查权限
        checkPermissions()
        
        // 设置事件监听
        setupEventListeners()
        
        updateStatus("就绪")
    }
    
    private fun checkPermissions() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            val permissions = mutableListOf<String>()
            
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.INTERNET) 
                != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.INTERNET)
            }
            
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_NETWORK_STATE) 
                != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.ACCESS_NETWORK_STATE)
            }
            
            if (permissions.isNotEmpty()) {
                ActivityCompat.requestPermissions(this, permissions.toTypedArray(), PERMISSION_REQUEST_CODE)
            }
        }
    }
    
    private fun setupEventListeners() {
        connectButton.setOnClickListener {
            val serverIp = serverIpInput.text.toString().trim()
            if (serverIp.isEmpty()) {
                Toast.makeText(this, "请输入服务器IP地址", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            
            connectToServer(serverIp)
        }
        
        disconnectButton.setOnClickListener {
            disconnectFromServer()
        }
        
        videoSurfaceView.onError = { error ->
            runOnUiThread {
                updateStatus("错误: $error")
                Toast.makeText(this, error, Toast.LENGTH_SHORT).show()
            }
        }
        
        videoSurfaceView.onStreamingStarted = {
            runOnUiThread {
                updateStatus("流传输中")
                connectButton.isEnabled = false
                disconnectButton.isEnabled = true
            }
        }
        
        videoSurfaceView.onStreamingStopped = {
            runOnUiThread {
                updateStatus("已断开")
                connectButton.isEnabled = true
                disconnectButton.isEnabled = false
            }
        }
    }
    
    private fun connectToServer(host: String) {
        updateStatus("正在连接...")
        Log.i(TAG, "连接到服务器: $host")
        
        videoSurfaceView.connectAndStream(host)
    }
    
    private fun disconnectFromServer() {
        videoSurfaceView.disconnect()
        updateStatus("已断开")
    }
    
    private fun updateStatus(status: String) {
        runOnUiThread {
            statusText.text = "状态: $status"
            Log.i(TAG, "状态: $status")
        }
    }
    
    override fun onDestroy() {
        super.onDestroy()
        videoSurfaceView.disconnect()
    }
    
    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == PERMISSION_REQUEST_CODE) {
            if (grantResults.all { it == PackageManager.PERMISSION_GRANTED }) {
                Log.i(TAG, "权限已授予")
            } else {
                Toast.makeText(this, "部分功能可能需要相关权限", Toast.LENGTH_SHORT).show()
            }
        }
    }
}
