using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenServer
{
    /// <summary>
    /// 输入注入器 - 将客户端的触摸/键盘/鼠标事件注入到 Windows 系统
    /// </summary>
    public static class InputInjector
    {
        // Windows API 导入
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
        
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        
        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        
        // 常量
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        
        private const int DESKTOPHORZRES = 118;
        private const int DESKTOPVERTRES = 117;
        
        // 屏幕尺寸缓存
        private static int _screenWidth;
        private static int _screenHeight;
        private static bool _initialized;
        
        // 触摸状态
        private static bool _isTouching;
        private static float _lastTouchX;
        private static float _lastTouchY;
        
        public static void Initialize()
        {
            if (_initialized) return;
            
            // 获取真实屏幕分辨率
            IntPtr hdc = GetDC(IntPtr.Zero);
            _screenWidth = GetDeviceCaps(hdc, DESKTOPHORZRES);
            _screenHeight = GetDeviceCaps(hdc, DESKTOPVERTRES);
            ReleaseDC(IntPtr.Zero, hdc);
            
            _initialized = true;
            Console.WriteLine($"输入注入器初始化完成，屏幕尺寸: {_screenWidth}x{_screenHeight}");
        }
        
        /// <summary>
        /// 处理来自客户端的输入事件
        /// </summary>
        public static void ProcessInputEvent(InputEventPacket inputEvent)
        {
            if (!_initialized) Initialize();
            
            switch (inputEvent.InputType)
            {
                case InputEventPacket.InputTypeTouchDown:
                    HandleTouchDown(inputEvent.X, inputEvent.Y);
                    break;
                    
                case InputEventPacket.InputTypeTouchMove:
                    HandleTouchMove(inputEvent.X, inputEvent.Y);
                    break;
                    
                case InputEventPacket.InputTypeTouchUp:
                    HandleTouchUp(inputEvent.X, inputEvent.Y);
                    break;
                    
                case InputEventPacket.InputTypeKeyDown:
                    HandleKeyDown(inputEvent.KeyCode);
                    break;
                    
                case InputEventPacket.InputTypeKeyUp:
                    HandleKeyUp(inputEvent.KeyCode);
                    break;
                    
                case InputEventPacket.InputTypeMouseMove:
                    HandleMouseMove(inputEvent.X, inputEvent.Y);
                    break;
                    
                case InputEventPacket.InputTypeMouseDown:
                    HandleMouseDown(inputEvent.X, inputEvent.Y);
                    break;
                    
                case InputEventPacket.InputTypeMouseUp:
                    HandleMouseUp(inputEvent.X, inputEvent.Y);
                    break;
                    
                case InputEventPacket.InputTypeMouseWheel:
                    HandleMouseWheel(inputEvent.Delta);
                    break;
            }
        }
        
        private static void HandleTouchDown(float normalizedX, float normalizedY)
        {
            _isTouching = true;
            _lastTouchX = normalizedX;
            _lastTouchY = normalizedY;
            
            int screenX = (int)(normalizedX * _screenWidth);
            int screenY = (int)(normalizedY * _screenHeight);
            
            SetCursorPos(screenX, screenY);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        }
        
        private static void HandleTouchMove(float normalizedX, float normalizedY)
        {
            if (!_isTouching) return;
            
            int screenX = (int)(normalizedX * _screenWidth);
            int screenY = (int)(normalizedY * _screenHeight);
            
            SetCursorPos(screenX, screenY);
            _lastTouchX = normalizedX;
            _lastTouchY = normalizedY;
        }
        
        private static void HandleTouchUp(float normalizedX, float normalizedY)
        {
            _isTouching = false;
            
            int screenX = (int)(normalizedX * _screenWidth);
            int screenY = (int)(normalizedY * _screenHeight);
            
            SetCursorPos(screenX, screenY);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
        
        private static void HandleKeyDown(int keyCode)
        {
            byte vkCode = (byte)MapToVirtualKey(keyCode);
            keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        }
        
        private static void HandleKeyUp(int keyCode)
        {
            byte vkCode = (byte)MapToVirtualKey(keyCode);
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        
        private static void HandleMouseMove(float normalizedX, float normalizedY)
        {
            int screenX = (int)(normalizedX * _screenWidth);
            int screenY = (int)(normalizedY * _screenHeight);
            SetCursorPos(screenX, screenY);
        }
        
        private static void HandleMouseDown(float normalizedX, float normalizedY)
        {
            int screenX = (int)(normalizedX * _screenWidth);
            int screenY = (int)(normalizedY * _screenHeight);
            SetCursorPos(screenX, screenY);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        }
        
        private static void HandleMouseUp(float normalizedX, float normalizedY)
        {
            int screenX = (int)(normalizedX * _screenWidth);
            int screenY = (int)(normalizedY * _screenHeight);
            SetCursorPos(screenX, screenY);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
        
        private static void HandleMouseWheel(int delta)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
        }
        
        /// <summary>
        /// 将 Android 按键码映射到 Windows 虚拟键码
        /// </summary>
        private static int MapToVirtualKey(int androidKeyCode)
        {
            // Android KeyEvent.keyCode -> Windows Virtual Key Code
            switch (androidKeyCode)
            {
                case 4: return 0x08; // BACK -> VK_BACK
                case 29: return 0x41; // A
                case 30: return 0x42; // B
                case 31: return 0x43; // C
                case 32: return 0x44; // D
                case 33: return 0x45; // E
                case 34: return 0x46; // F
                case 35: return 0x47; // G
                case 36: return 0x48; // H
                case 37: return 0x49; // I
                case 38: return 0x4A; // J
                case 39: return 0x4B; // K
                case 40: return 0x4C; // L
                case 41: return 0x4D; // M
                case 42: return 0x4E; // N
                case 43: return 0x4F; // O
                case 44: return 0x50; // P
                case 45: return 0x51; // Q
                case 46: return 0x52; // R
                case 47: return 0x53; // S
                case 48: return 0x54; // T
                case 49: return 0x55; // U
                case 50: return 0x56; // V
                case 51: return 0x57; // W
                case 52: return 0x58; // X
                case 53: return 0x59; // Y
                case 54: return 0x5A; // Z
                case 7: return 0x30; // 0
                case 8: return 0x31; // 1
                case 9: return 0x32; // 2
                case 10: return 0x33; // 3
                case 11: return 0x34; // 4
                case 12: return 0x35; // 5
                case 13: return 0x36; // 6
                case 14: return 0x37; // 7
                case 15: return 0x38; // 8
                case 16: return 0x39; // 9
                case 62: return 0x20; // SPACE
                case 66: return 0x0D; // ENTER
                case 67: return 0x08; // DEL
                case 57: return 0xA0; // ALT_LEFT
                case 58: return 0xA4; // ALT_RIGHT
                case 59: return 0xA2; // SHIFT_LEFT
                case 60: return 0xA1; // SHIFT_RIGHT
                case 61: return 0x09; // TAB
                case 113: return 0xA3; // CTRL_LEFT
                case 114: return 0xA5; // CTRL_RIGHT
                case 24: return 0x26; // DPAD_UP (音量+)
                case 25: return 0x28; // DPAD_DOWN (音量-)
                default: return 0;
            }
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
