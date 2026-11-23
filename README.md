# 豚鼠键盘 · WheekKeys

一只坐在迷你键盘后面的三花荷兰猪。你打字，它就伸爪敲键，连打时两只爪子可以同时动作。点鼠标时，它会探身去够左边那只小鼠标。

**[下载 Windows 版 WheekKeys.exe](https://github.com/gwtt/guinea-pig-keyboard/releases/latest/download/WheekKeys.exe)** · [所有版本](https://github.com/gwtt/guinea-pig-keyboard/releases)

![程序实际画面，依次演示左右爪、双爪、空格、回车和鼠标点击](preview.gif)

## 启动

在装有 .NET Framework 4.8 的 Windows 10 或 Windows 11 上，下载后双击 `WheekKeys.exe` 即可启动。图片和图标都已嵌入程序。

桌宠默认出现在主屏幕右下角。打开记事本，交替按 **A** 和 **J** 可以试左右爪，按空格可以看它向前伸爪。动画跟随物理键位，键帽和气泡使用美式键盘标识。几枚省略的标点共用相邻键帽，例如按等号会点亮减号所在的键，气泡仍显示等号。

## 桌面操作

| 操作 | 效果 |
| --- | --- |
| 按住荷兰猪拖动 | 移动桌宠，松手后记住位置 |
| 右键点荷兰猪或通知区域里的荷兰猪图标 | 调整大小、开关气泡、回到右下角或退出 |
| 选择「开启鼠标穿透」 | 直接点击桌宠下面的窗口。要再次拖动，从通知区域的菜单关闭穿透 |
| 选择「隐藏按键气泡」 | 保留敲击动作和键面反馈，隐藏弹起的键名 |

程序通过 Windows Raw Input 接收当前桌面会话的键鼠事件。按键只用于即时动画。保存在 `%LOCALAPPDATA%\WheekKeys\settings.txt` 的内容是窗口位置、大小、气泡和穿透开关。

## 从源码构建

在项目目录的 PowerShell 中运行以下命令。脚本调用 Windows 自带的 .NET Framework 编译器，将程序写入 `dist\WheekKeys.exe`。重新构建前先退出正在运行的桌宠。

```powershell
.\build.ps1
```

推送到 `main` 或向 `main` 提交拉取请求会触发 Windows 构建。发布新版本时，在要发布的提交上创建版本标签（例如 `v0.2.0`）并推送该标签。GitHub Actions 会编译该版本，并将 `WheekKeys.exe` 附在 GitHub Release 中。

需要补发已有标签时，在 **Actions → Windows build → Run workflow** 选择 `main`，填写 `release_tag`。留空会只构建所选提交。

## 美术与参考

角色和道具使用内置 ImageGen 分层生成，前爪、键帽反馈和气泡在 WPF 中实时组合。生成提示的主要描述如下。

| 图层 | 生成描述 |
| --- | --- |
| [荷兰猪](assets/guinea-pig.png) | 正面蹲坐的短毛三花荷兰猪，左侧焦糖色、右侧可可色，奶油白鼻梁和胸口；折耳、短鼻，四肢收起。暖棕细线、水粉和铅笔纸纹，透明背景 |
| [前爪](assets/paw.png) | 单只向下的奶油色荷兰猪前爪，从爪背看见短绒毛和四只浅粉脚趾，肩部边缘柔软。独立透明图层 |
| [键盘](assets/keyboard.png) | 正前方略俯视的五排迷你机械键盘，鼠尾草绿外壳、奶油键帽、橘色 Esc 和回车。水粉叠色和细纸纹，键面留白以便程序绘制字符 |
| [鼠标](assets/mouse.png) | 俯视的奶油色复古鼠标，橘色滚轮、掌托上两片小绿叶，水彩纸颗粒与暖赭色铅笔轮廓 |

后台响应键鼠、透明置顶的交互参考了 [KeyLimePie7/BongoCat](https://github.com/KeyLimePie7/BongoCat)，按键左右分区参考了 [DesktopKitty](https://github.com/MayankArambhi/DesktopKitty)。本项目使用独立的 WPF 实现和荷兰猪素材。
