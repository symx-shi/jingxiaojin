# 鲸小金

小金陪你看国际金价。C# / WPF 桌宠，Windows 10/11 x64，当前版本 **v1.2.2**。

## 功能

- 默认置顶的小号动态桌宠，头顶仅显示人民币/克价格。
- 单击摸头，双击行情详情；涨跌、休市推测和连接异常使用独立动画。
- 悬停速览包含涨跌、日内高低和迷你曲线；完整详情支持鼠标查看指定时间附近的真实报价。
- 一次性价格提醒、提醒记录、安静模式、大小设置、屏幕边缘吸附和可选登录自启。
- 详情独立最小化或关闭，平时桌宠不占任务栏。

## 两种发行方式

- `鲸小金-v1.2.2-便携版.exe`：双击即可运行，动画与图标已内嵌。
- `鲸小金-v1.2.2-安装版.exe`：安装向导，支持选择父目录并自动追加 `jingxiaojin`，例如 `D:\soft\jingxiaojin`；提供开始菜单、可选桌面快捷方式、安装后启动和卸载入口。

安装器使用 NSIS，不再使用旧 MSI 的长时间文件校验流程。旧 MSI 用户先退出并卸载旧版，再安装新版；检测到旧 MSI 登记时会提示。

需要 .NET Framework 4.8 和联网。当前程序与安装包未做代码签名。Release 建议同时提供 ZIP、素材许可与 SHA256 校验文件。

## 源码与构建

`src/GoldWidget.cs`：行情、换算、详情与图表；`src/PetWindow.cs`：角色互动、状态和设置。

使用 Visual Studio 2022 的 .NET 桌面开发工作负载及 .NET Framework 4.8 开发工具包打开 `WhaleGold.csproj`，或：

```powershell
dotnet build WhaleGold.csproj -c Release
```

输出为 `bin/Release/net48/鲸小金.exe`。本次发行 EXE 使用 Windows .NET Framework 自带 C# 编译器编译，SDK 项目文件尚未实际构建验证。

安装包使用官方 [NSIS](https://nsis.sourceforge.io/Download) 3.11（可使用便携 ZIP）：

```powershell
.\packaging\Build-Setup.ps1 -Executable '.\bin\Release\net48\鲸小金.exe' -AssetLicense '.\ASSET-LICENSE.txt' -MakeNsis 'C:\tools\nsis\makensis.exe' -OutputDirectory '.\dist'
```

`packaging/WhaleGold.nsi` 为当前安装脚本；`Build-Installer.ps1` 仅保留作旧 MSI 维护参考。

## 数据与报价口径

数据来自新浪财经公开接口，约15秒请求报价、60秒请求分钟历史，不保证交易所级实时性及接口长期可用。

人民币/克 = 美元/金衡盎司 × 在岸 USD/CNY 买入报价 ÷ 31.1034768。

涨跌、昨收、曲线和日内高低均按当前汇率折算，不含历史汇率变化和品牌溢价，不是金饰售价。黄金与汇率时效分别判断；沿用旧汇率时暂停价格提醒。周末睡觉只是基于时间和报价停更推测可能休市，并非官方交易日历。

设置、缓存、日志和提醒记录保存在 `%LOCALAPPDATA%\GoldWidgetRMB`，无需账户或密钥。卸载保留用户数据，只移除已知程序文件；登录自启项仅在指向被卸载程序时移除。

## 验证

已验证主程序交互、在线报价、行情曲线、提醒记录和设置；轻量安装器已在本机 D 盘验证安装、运行中卸载、文件及卸载登记清理和用户设置保留。旧 MSI 卸载约27秒，新安装器几轮约0.4～10.5秒，耗时随环境变化。未在另一台电脑或混合 DPI 多显示器环境验证；未编写自动化测试代码。

## 素材来源与许可

角色与动画来自 **[PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)**，参考项目 [qinliql/DeepSeekPet](https://github.com/qinliql/DeepSeekPet)。非 DeepSeek 官方产品。

原型“溟月”来自 [上善无形](https://space.bilibili.com/4456176)，女仆鲸鱼娘设计来自 [ZipZipPipe](https://space.bilibili.com/4168597)。按上游声明采用 **CC BY-NC-SA 4.0：署名、非商业、相同方式共享**。本项目对 GIF 抽帧、裁切、去绿背景，转换为透明 PNG 并内嵌 EXE。

分发和修改时保留作者、上游链接及许可，详见 [ASSET-LICENSE.txt](ASSET-LICENSE.txt)。上游代码 MIT 许可不覆盖角色素材；本仓库未另行指定应用代码许可证。