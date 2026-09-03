# 使用 GitHub Actions 构建 APK

本项目已配置 GitHub Actions 自动构建。将代码推送到 GitHub 后，每次 push 或 PR 都会自动触发 APK 构建。

## 快速开始

### 1. 创建 GitHub 仓库

1. 登录 [GitHub](https://github.com)
2. 点击 **New repository** 创建一个新仓库（例如：`GR-Screen-Extender`）
3. 选择 **Public**（公开仓库，GitHub Actions 免费无限时）
4. **不要**勾选添加 .gitignore（因为你本地已有项目代码）

### 2. 推送代码到 GitHub

在项目根目录执行以下命令：

```bash
git init
git add .
git commit -m "Initial commit: GR扩展屏幕项目"
git branch -M main
git remote add origin https://github.com/你的用户名/GR-Screen-Extender.git
git push -u origin main
```

### 3. 获取 APK 文件

推送完成后，GitHub 会自动触发构建。获取 APK 有两种方式：

#### 方式一：从 Artifacts 下载（任意推送都会生成）

1. 打开 GitHub 仓库页面
2. 点击 **Actions** 标签
3. 选择最新的工作流运行记录
4. 滚动到页面底部 **Artifacts** 区域
5. 点击 **GR扩展屏幕-debug.apk** 下载
6. 解压后得到 `app-debug.apk`

#### 方式二：从 Releases 下载（仅 main/master 分支推送生成）

1. 打开 GitHub 仓库页面
2. 点击右侧 **Releases** 区域
3. 点击最新版本（如 `v1`）
4. 下载 `app-debug.apk` 附件

### 4. 手动触发构建

如果代码未改变但想重新构建：

1. 进入 **Actions** → **Build Android APK**
2. 点击 **Run workflow** 按钮
3. 确认后点击 **Run workflow**

## 安装 APK 到手机

1. 将 APK 文件发送到手机（微信、QQ、邮件等方式）
2. 在手机上点击 APK 文件安装
3. 如遇"不允许安装未知应用"提示，设置 → 安全 → 开启"允许来自此来源的应用"

## 常见问题

### 构建失败怎么办？

1. 进入 Actions 查看错误日志
2. 常见问题：
   - **编译错误**：检查 Kotlin 语法
   - **依赖下载失败**：重新运行 workflow
   - **Gradle 版本不匹配**：更新 `gradle-wrapper.properties` 中的版本

### 如何修改版本号？

编辑 `client/app/build.gradle`：
```gradle
android {
    defaultConfig {
        versionCode 2      // 版本号（数字，递增）
        versionName "1.1"  // 版本名称
    }
}
```

### 如何生成 Release（发布版）APK？

修改 `.github/workflows/build-apk.yml` 中的构建命令：
```yaml
# Debug APK（当前配置）
- run: ./gradlew assembleDebug

# Release APK（需要签名配置）
- run: ./gradlew assembleRelease
```
