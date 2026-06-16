# 安装包说明

本目录用于生成 `CATIA Wing Designer` 的 Windows 安装包。安装包面向已安装 CATIA V5 R20 的 Windows 机器。

## 前置条件

- Windows 10/11 64 位。
- .NET Framework 4.8 或更高版本。
- CATIA V5 R20 已安装，并且 `CATIA.Application` COM 已注册。
- 使用本程序生成 CATPart 前，需要先手动打开 CATIA V5 R20，并确认没有许可证、环境选择或确认弹窗。
- CATIA 与本程序需要使用相同权限级别运行，例如都以普通权限运行，或都以管理员权限运行。

如果目标机器无法检测到 CATIA COM 注册，先在目标机器上运行 CATIA 自带注册工具，例如：

```powershell
& 'D:\CATIAr20\win_b64\code\bin\V5RegServer.exe'
```

具体路径以目标机器 CATIA 安装目录为准。

## 生成安装包

在项目根目录执行：

```powershell
.\tools\package-installer.ps1
```

脚本会执行以下动作：

1. 使用 `Release | x64` 构建 WPF 程序。
2. 将程序运行依赖复制到 `artifacts/publish/app/`。
3. 使用 Windows 自带 `iexpress.exe` 生成单文件安装程序。
4. 输出安装包到 `artifacts/installer/CatiaWingDesigner-0.1.0-Setup.exe`。

指定版本号：

```powershell
.\tools\package-installer.ps1 -Version 0.2.0
```

如果当前机器尚未还原 NuGet 包，可以显式执行 Restore：

```powershell
.\tools\package-installer.ps1 -Restore
```

## 安装行为

安装包默认安装到当前用户目录：

```text
%LOCALAPPDATA%\Programs\CatiaWingDesigner
```

安装时会创建：

- 开始菜单快捷方式：`CATIA Wing Designer`
- 桌面快捷方式：`CATIA Wing Designer`
- 当前用户卸载项：`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CatiaWingDesigner`

不写入 CATIA 安装目录，不修改 CATIA 文件，不复制或注册 CATIA 类型库。

## 卸载

可以在 Windows “应用和功能”中卸载，也可以运行安装目录中的：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File uninstall-current-user.ps1
```
