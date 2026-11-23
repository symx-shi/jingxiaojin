# 鲸小金

Windows 桌面金价小工具，桌宠名叫“小金”。使用 C#、WPF 和 .NET Framework 编写，默认置顶显示角色与人民币/克报价，双击打开行情详情。

## 功能

- 国际现货黄金与在岸人民币汇率，约每 15 秒刷新。
- 悬停查看涨跌、日内最高最低和迷你曲线。
- 详情支持日内/近一小时走势图，鼠标查看最近真实报价点。
- 独立待机、涨跌、摸头、睡觉与连接异常动画。
- 一次性价格提醒、提醒记录、安静模式、大小设置、边缘吸附和可选登录自启。
- 图标和动画内嵌 EXE，可复制到另一台 Windows 电脑使用。

## 构建

适用 Windows 10/11 x64。用 Visual Studio 2022 打开 `WhaleGold.csproj`，安装“.NET 桌面开发”工作负载及 .NET Framework 4.8 开发工具包，然后选择 Release 构建。

也可以在具备上述工具的开发者终端执行：

```powershell
dotnet build WhaleGold.csproj -c Release
```

输出位于 `bin/Release/net48/鲸小金.exe`。首次运行目标电脑需要 .NET Framework 4.8。此仓库新增的项目文件尚未实际构建验证；之前的交付 EXE 使用本机 .NET Framework C# 编译器生成。

## 报价说明

数据来自新浪财经公开接口，不保证交易所级实时性或接口长期可用。

人民币/克 = 美元/金衡盎司 × 在岸 USD/CNY 买入报价 ÷ 31.1034768。

涨跌、昨收、曲线和日内高低均使用当前汇率折算，不含历史汇率变化、手续费和品牌溢价，不是金饰零售价。“较昨收”指相对上一个交易日黄金收盘价的变化。

黄金与汇率时效分别判断；汇率过期会提示沿用旧汇率并暂停价格提醒。周末睡觉只是根据时间和停更状态推测可能休市，并非交易所官方状态。

## 本地数据

设置、位置、缓存、提醒记录和日志保存在 `%LOCALAPPDATA%\GoldWidgetRMB`，不在仓库内。不需要账户或 API 密钥。

自启由用户主动开启，使用当前用户的 `Software\Microsoft\Windows\CurrentVersion\Run` 下 `GoldWidgetRMB` 项。移动 EXE 后需要重新设置；删除程序前建议先关闭自启。

## 素材来源与许可

角色和动画来自 **[PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)**；参考项目为 [qinliql/DeepSeekPet](https://github.com/qinliql/DeepSeekPet)。本项目非 DeepSeek 官方产品。

角色原型“溟月”出自 [上善无形](https://space.bilibili.com/4456176)，女仆鲸鱼娘设计出自 [ZipZipPipe](https://space.bilibili.com/4168597)。角色素材按上游声明采用 **CC BY-NC-SA 4.0（署名、非商业、相同方式共享）**。本项目对 GIF 抽帧、裁切、移除绿色背景，转换为透明 PNG。发布、分发或再次修改时请保留署名与上游地址。

详见 [ASSET-LICENSE.txt](ASSET-LICENSE.txt)。不能将上游代码的 MIT 许可理解为角色素材可自由商用。本仓库暂未另行指定应用代码许可证。
