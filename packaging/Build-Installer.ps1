param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$AssetLicense,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Version = '1.2.2'
)

# User-scoped MSI that permits elevation for cross-volume rollback; keep user data outside the install tree.
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$AssetLicense = (Resolve-Path -LiteralPath $AssetLicense).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$scratch = Join-Path $OutputDirectory ('msi-work-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
Copy-Item -LiteralPath $Executable -Destination (Join-Path $scratch 'app.exe')
Copy-Item -LiteralPath $AssetLicense -Destination (Join-Path $scratch 'license.txt')
$ddf = @'
.OPTION EXPLICIT
.Set CabinetNameTemplate=payload.cab
.Set DiskDirectoryTemplate=.
.Set MaxDiskSize=0
.Set MaxCabinetSize=0
.Set CompressionType=MSZIP
.Set Cabinet=on
.Set Compress=on
app.exe AppExe
license.txt AssetLicense
'@
[IO.File]::WriteAllText((Join-Path $scratch 'payload.ddf'), $ddf, [Text.Encoding]::ASCII)
Push-Location $scratch
try {
    & "$env:SystemRoot\System32\makecab.exe" /F payload.ddf | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cabinet generation failed.' }
} finally { Pop-Location }

$installer = New-Object -ComObject WindowsInstaller.Installer
$output = Join-Path $OutputDirectory ('鲸小金-' + $Version + '-安装版.msi')
if (Test-Path -LiteralPath $output) { throw 'Output MSI already exists. Choose a new output folder.' }
$database = $installer.OpenDatabase($output, 3)
# MSI uses a legacy database code page, not UTF-8; Simplified Chinese is code page 936.
[IO.File]::WriteAllText((Join-Path $scratch 'codepage.idt'), "`r`n`r`n936`t_ForceCodepage`r`n", [Text.Encoding]::ASCII)
$database.Import($scratch, 'codepage.idt')

function Execute-Sql([string]$Sql) {
    $view = $database.OpenView($Sql)
    try { $view.Execute() } finally { $view.Close() }
}

# Parameters protect Unicode strings and filesystem paths from SQL interpretation.
function Add-Row([string]$Table, [string[]]$Columns, [object[]]$Values) {
    $names = ($Columns | ForEach-Object { '``' + $_ + '``' }) -join ','
    $names = $names.Replace('``', '`')
    $marks = (@($Columns | ForEach-Object { '?' })) -join ','
    $view = $database.OpenView(('INSERT INTO `' + $Table + '` (' + $names + ') VALUES (' + $marks + ')'))
    $record = $installer.CreateRecord($Values.Count)
    for ($i = 0; $i -lt $Values.Count; $i++) {
        if ($Values[$i] -is [int]) { $record.IntegerData($i + 1) = $Values[$i] }
        elseif ($null -ne $Values[$i]) { $record.StringData($i + 1) = [string]$Values[$i] }
    }
    try { $view.Execute($record) } finally { $view.Close() }
}

$schemas = @(
'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)',
'CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` CHAR(255) PRIMARY KEY `Action`)',
'CREATE TABLE `ControlCondition` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Action` CHAR(50) NOT NULL, `Condition` CHAR(255) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Action`, `Condition`)',
'CREATE TABLE `Signature` (`Signature` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `MinVersion` CHAR(20), `MaxVersion` CHAR(20), `MinSize` LONG, `MaxSize` LONG, `MinDate` LONG, `MaxDate` LONG, `Languages` CHAR(255) PRIMARY KEY `Signature`)',
'CREATE TABLE `AppSearch` (`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`)',
'CREATE TABLE `RegLocator` (`Signature_` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`)',
'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)',
'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)',
'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)',
'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)',
'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)',
'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)',
'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)',
'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)',
'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)',
'CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)',
'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)',
'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)',
'CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)',
'CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)',
'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50), `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)',
'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(50), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)',
'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255) NOT NULL, `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)',
'CREATE TABLE `CheckBox` (`Property` CHAR(72) NOT NULL, `Value` CHAR(64) PRIMARY KEY `Property`)'
)
foreach ($schema in $schemas) { Execute-Sql $schema }
Add-Row TextStyle @('TextStyle','FaceName','Size','StyleBits') @('NormalFont','Microsoft YaHei UI',10,0)
Add-Row TextStyle @('TextStyle','FaceName','Size','StyleBits') @('HeadingFont','Microsoft YaHei UI',13,1)
# Resolve the previously selected target before costing on repair/uninstall.
Add-Row RegLocator @('Signature_','Root','Key','Name','Type') @('InstalledDirectory',1,'Software\Jingxiaojin','InstallPath',18)
Add-Row AppSearch @('Property','Signature_') @('INSTALLDIR','InstalledDirectory')
$upgradeCode = '{D4EEAB35-222A-4EBD-A09C-7A55A230FF22}'
$productCode = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
$props = @{
    ProductName='鲸小金'; Manufacturer='鲸小金'; ProductVersion=$Version; ProductLanguage='2052';
    ProductCode=$productCode; UpgradeCode=$upgradeCode; INSTALLLEVEL='1';
    # Keep file costing, rollback and restore-point policy intact; omit unrelated component costs.
    DefaultUIFont='NormalFont'; MSIDISABLERMRESTART='1'; MSIFASTINSTALL='2';
    ARPCOMMENTS='小金陪你看国际金价 · 人民币每克'; ARPURLINFOABOUT='https://github.com/symx-shi/jingxiaojin';
    SecureCustomProperties='OLDPRODUCTS;NEWERPRODUCT;BASEDIR;INSTALLDIR'; DESKTOPSHORTCUT='1'; RUNAPP='1'
}
foreach ($key in $props.Keys) { Add-Row Property @('Property','Value') @($key,$props[$key]) }
foreach ($row in @(
    @('TARGETDIR',$null,'SourceDir'), @('LocalAppDataFolder','TARGETDIR','.'), @('BASEDIR','LocalAppDataFolder','Programs'),
    @('INSTALLDIR','BASEDIR','jingxiaojin'), @('ProgramMenuFolder','TARGETDIR','.'), @('AppMenu','ProgramMenuFolder','鲸小金'), @('DesktopFolder','TARGETDIR','.')
)) { Add-Row Directory @('Directory','Directory_Parent','DefaultDir') $row }
Add-Row Feature @('Feature','Title','Description','Display','Level','Directory_','Attributes') @('Main','鲸小金','桌宠与行情工具',1,1,'INSTALLDIR',0)
Add-Row Component @('Component','ComponentId','Directory_','Attributes','KeyPath') @('AppComponent','{133E34B6-7CD1-4F89-8C58-DC6087C46971}','INSTALLDIR',260,'AppMarker')
Add-Row Component @('Component','ComponentId','Directory_','Attributes','Condition','KeyPath') @('DesktopComponent','{899E5E82-08A1-498E-96FB-022CFF5D42D8}','DesktopFolder',260,'DESKTOPSHORTCUT = 1','DesktopMarker')
foreach ($component in @('AppComponent','DesktopComponent')) { Add-Row FeatureComponents @('Feature_','Component_') @('Main',$component) }
Add-Row Registry @('Registry','Root','Key','Name','Value','Component_') @('AppMarker',1,'Software\Jingxiaojin','InstallPath','[INSTALLDIR]','AppComponent')
Add-Row Registry @('Registry','Root','Key','Name','Value','Component_') @('DesktopMarker',1,'Software\Jingxiaojin','DesktopShortcut','#1','DesktopComponent')
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($Executable).FileVersion
Add-Row File @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') @('AppExe','AppComponent','app.exe|鲸小金.exe',[int](Get-Item $Executable).Length,$fileVersion,512,1)
Add-Row File @('File','Component_','FileName','FileSize','Attributes','Sequence') @('AssetLicense','AppComponent','license.txt|素材来源与许可.txt',[int](Get-Item $AssetLicense).Length,512,2)
Add-Row Media @('DiskId','LastSequence','Cabinet') @(1,2,'#payload.cab')
Add-Row Shortcut @('Shortcut','Directory_','Name','Component_','Target','Description','ShowCmd','WkDir') @('MenuShortcut','AppMenu','鲸小金','AppComponent','[#AppExe]','小金陪你看金价',1,'INSTALLDIR')
Add-Row Shortcut @('Shortcut','Directory_','Name','Component_','Target','Description','ShowCmd','WkDir') @('DesktopShortcut','DesktopFolder','鲸小金','DesktopComponent','[#AppExe]','小金陪你看金价',1,'INSTALLDIR')
Add-Row RemoveFile @('FileKey','Component_','DirProperty','InstallMode') @('RemoveMenuDir','AppComponent','AppMenu',2)
Add-Row Upgrade @('UpgradeCode','VersionMax','Attributes','ActionProperty') @($upgradeCode,$Version,1,'OLDPRODUCTS')
Add-Row Upgrade @('UpgradeCode','VersionMin','Attributes','ActionProperty') @($upgradeCode,$Version,258,'NEWERPRODUCT')
Add-Row LaunchCondition @('Condition','Description') @('NOT NEWERPRODUCT','已安装更新版本的鲸小金，请先卸载更新版本。')
Add-Row LaunchCondition @('Condition','Description') @('VersionNT64','鲸小金需要64位 Windows。')
Add-Row CheckBox @('Property','Value') @('DESKTOPSHORTCUT','1')
Add-Row CheckBox @('Property','Value') @('RUNAPP','1')
# Installed-file EXE action, asynchronous/no-wait. Invoked only by the interactive finish button.
Add-Row CustomAction @('Action','Type','Source') @('LaunchWhaleGold',210,'AppExe')
# Only versions built with the graceful shutdown command can execute this uninstall action.
# Close BEFORE FileCost: otherwise MSI caches an in-use file and still performs a slow
# process scan in InstallValidate even though the app has exited by that point.
# AppSearch already recovered INSTALLDIR; property-based launch needs no file costing.
Add-Row CustomAction @('Action','Type','Source','Target') @('ResolveCloseExecutable',51,'CLOSEEXE','[INSTALLDIR]鲸小金.exe')
Add-Row CustomAction @('Action','Type','Source','Target') @('CloseWhaleGold',50,'CLOSEEXE','--shutdown-for-maintenance')
Add-Row InstallExecuteSequence @('Action','Condition','Sequence') @('ResolveCloseExecutable','Installed AND REMOVE ~= "ALL" AND INSTALLDIR',60)
Add-Row InstallExecuteSequence @('Action','Condition','Sequence') @('CloseWhaleGold','Installed AND REMOVE ~= "ALL" AND INSTALLDIR',70)

# Minimal native wizard. MSI handles progress, files-in-use, rollback, repair and uninstall.
Add-Row Dialog @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('Welcome',50,50,370,255,3,'鲸小金安装','ParentPath','Install','Cancel')
foreach ($row in @(
 @('Welcome','Heading','Text',18,15,334,25,3,$null,'{\HeadingFont}安装鲸小金',$null),
 @('Welcome','Body','Text',18,46,334,38,3,$null,'选择安装父目录，安装时自动新建 jingxiaojin 子目录。素材仅限非商业使用，署名与许可随程序安装。',$null),
 @('Welcome','ParentLabel','Text',18,91,334,16,3,$null,'安装父目录：',$null),
 @('Welcome','ParentPath','PathEdit',18,111,252,20,3,'BASEDIR',$null,'Browse'),
 @('Welcome','Browse','PushButton',280,111,72,20,3,$null,'浏览…','Desktop'),
 @('Welcome','PathHint','Text',18,140,334,30,3,$null,'例如选择 D:\soft，实际安装到 D:\soft\jingxiaojin。请选择有写入权限的目录。',$null),
 @('Welcome','Desktop','CheckBox',18,183,275,18,3,'DESKTOPSHORTCUT','创建桌面快捷方式','Install'),
 @('Welcome','Install','PushButton',198,220,72,20,3,$null,'安装','Cancel'),
 @('Welcome','Cancel','PushButton',280,220,72,20,3,$null,'取消','ParentPath')
)) { Add-Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next') $row }
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Welcome','Install','SetTargetPath','BASEDIR','1',1)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Welcome','Install','EndDialog','Return','1',2)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Welcome','Browse','SetTargetPath','BASEDIR','1',1)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Welcome','Browse','SpawnDialog','BrowseParent','1',2)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Welcome','Cancel','EndDialog','Exit','1',1)
Add-Row Dialog @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('BrowseParent',50,50,370,280,3,'选择安装父目录','Drives','OK','Cancel')
foreach ($row in @(
 # DirectoryCombo needs explicit volume flags to expose other drives, not only the current path.
 # Visible + Enabled + RemovableVolume + FixedVolume + RemoteVolume.
 @('BrowseParent','Drives','DirectoryCombo',18,15,334,80,458755,'BASEDIR',$null,'Folders'),
 @('BrowseParent','Folders','DirectoryList',18,45,334,140,3,'BASEDIR',$null,'Path'),
 @('BrowseParent','Path','PathEdit',18,197,334,20,3,'BASEDIR',$null,'OK'),
 @('BrowseParent','OK','PushButton',198,244,72,20,3,$null,'选择','Cancel'),
 @('BrowseParent','Cancel','PushButton',280,244,72,20,3,$null,'返回','Drives')
)) { Add-Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next') $row }
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('BrowseParent','OK','SetTargetPath','BASEDIR','1',1)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('BrowseParent','OK','EndDialog','Return','1',2)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('BrowseParent','Cancel','EndDialog','Return','1',1)
Add-Row Dialog @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('Finished',50,50,330,165,3,'鲸小金安装','Close','Close','Close')
Add-Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Text') @('Finished','Message','Text',18,20,294,65,3,'操作已完成。安装后可从开始菜单或桌面运行鲸小金；卸载不会移除本机的价格提醒和设置。')
Add-Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next') @('Finished','Launch','CheckBox',18,95,294,18,3,'RUNAPP','立即启动鲸小金','Close')
Add-Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Text','Control_Next') @('Finished','Close','PushButton',240,130,72,20,3,'完成','Launch')
Add-Row ControlCondition @('Dialog_','Control_','Action','Condition') @('Finished','Launch','Hide','Installed OR REMOVE ~= "ALL"')
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Finished','Close','DoAction','LaunchWhaleGold','RUNAPP = 1 AND NOT Installed AND NOT (REMOVE ~= "ALL")',1)
Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('Finished','Close','EndDialog','Return','1',2)

$execute = @{
 FindRelatedProducts=25; AppSearch=50; LaunchConditions=100; CostInitialize=800; FileCost=900; CostFinalize=1000;
 InstallValidate=1400; InstallInitialize=1500; RemoveExistingProducts=1510; ProcessComponents=1600;
 UnpublishFeatures=1800; RemoveShortcuts=3200; RemoveRegistryValues=3300; RemoveFiles=3500;
 InstallFiles=4000; CreateShortcuts=4500; WriteRegistryValues=5000; RegisterUser=6000;
 RegisterProduct=6100; PublishFeatures=6300; PublishProduct=6400; InstallFinalize=6600
}
foreach ($action in $execute.Keys) { Add-Row InstallExecuteSequence @('Action','Sequence') @($action,[int]$execute[$action]) }
foreach ($row in @(@('FindRelatedProducts',$null,25),@('AppSearch',$null,50),@('LaunchConditions',$null,100),@('CostInitialize',$null,800),@('FileCost',$null,900),@('CostFinalize',$null,1000),@('Welcome','NOT Installed',1200),@('ExecuteAction',$null,1300),@('Finished',$null,-1))) {
    Add-Row InstallUISequence @('Action','Condition','Sequence') $row
}
$view = $database.OpenView('INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)')
$record = $installer.CreateRecord(2)
$record.StringData(1) = 'payload.cab'
$record.SetStream(2, (Join-Path $scratch 'payload.cab'))
$view.Execute($record); $view.Close()
$summary = $database.SummaryInformation(20)
$summary.Property(1) = 936
$summary.Property(2) = '鲸小金安装包'
$summary.Property(3) = '鲸小金桌宠'
$summary.Property(4) = '鲸小金'
$summary.Property(7) = 'x64;2052'
$summary.Property(9) = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
$summary.Property(14) = 200
# Compressed package, without the LUA/no-elevation bit: Windows can elevate rollback on other volumes.
$summary.Property(15) = 2
$summary.Persist()
$database.Commit()
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
Write-Output $output
