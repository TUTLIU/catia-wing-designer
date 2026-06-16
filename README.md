# CATIA Wing Designer

面向 CATIA V5 R20 的 C# WPF 机翼参数化设计与自动建模程序。项目用于 CATIA 二次开发课程实践，支持类似 OpenVSP 的翼段参数驱动、三维预览、设计参数保存加载，以及通过 CATIA COM Automation 自动生成 CATPart。

## 功能

- 类 OpenVSP 的翼段 Driver Group 参数组合。
- 单翼段纵向编辑界面，支持翼段选择、添加、插入和删除。
- 支持中文/英文双语参数标签。
- 支持 NACA 4 位翼型和 Selig `.dat` 翼型解析。
- 支持 WPF + HelixToolkit 三维线框和半透明曲面预览。
- 支持 JSON 保存和加载机翼设计参数。
- 支持连接已打开的 CATIA V5 R20，并自动生成带命名特征树的 CATPart。
- 支持生成曲面、封闭曲面实体和加厚曲面实体。
- 支持安装包打包，便于在已安装 CATIA V5 R20 的电脑上部署。

## 环境要求

- Windows 10/11 64 位。
- Visual Studio 2022 或 VS Build Tools。
- .NET Framework 4.8。
- CATIA V5 R20 64 位。
- CATIA COM 已注册，可通过 `CATIA.Application` 自动化访问。

本项目不会在未安装 CATIA 的环境中模拟建模；缺少 CATIA 或 CATIA COM 不可用时，CATIA 生成动作会直接报错。

## 项目结构

```text
CatiaWingDesigner.Core/     几何模型、翼型解析、参数求解、JSON 序列化
CatiaWingDesigner.App/      WPF 界面、三维预览、CATIA COM 自动建模
CatiaWingDesigner.Tests/    控制台回归测试
installer/                  安装与卸载脚本
tools/                      安装包生成脚本
Libs/Interop/               CATIA 互操作程序集留档
```

## 开发运行

1. 用 Visual Studio 2022 打开 `CatiaWingDesigner.sln`。
2. 将解决方案平台设置为 `x64`。
3. 还原 NuGet 依赖。
4. 生成解决方案。
5. 先手动打开 CATIA V5 R20，确认已进入主界面，且没有许可证、环境选择或确认弹窗。
6. 启动 `CatiaWingDesigner.App`。
7. 在界面中修改机翼参数，点击“更新预览”检查外形。
8. 点击“生成曲面”“封闭曲面实体”或“加厚曲面实体”，选择 `.CATPart` 保存路径。

CATIA 和本程序必须使用一致的权限级别运行，例如都使用普通权限，或都使用管理员权限。

## 命令行构建与测试

```powershell
& 'D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' CatiaWingDesigner.App\CatiaWingDesigner.App.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo

dotnet run --project CatiaWingDesigner.Tests\CatiaWingDesigner.Tests.csproj -c Debug -p:Platform=x64
```

## 生成安装包

项目使用 Windows 自带的 IExpress 生成单文件安装包：

```powershell
.\tools\package-installer.ps1
```

指定版本号：

```powershell
.\tools\package-installer.ps1 -Version 0.2.0
```

安装包输出目录：

```text
artifacts/installer/
```

`artifacts/` 是生成产物，不应提交到 Git。

## 安装包使用

将 `artifacts/installer/CatiaWingDesigner-版本号-Setup.exe` 发给用户即可。目标电脑需要满足：

- 已安装 .NET Framework 4.8。
- 已安装 CATIA V5 R20。
- 已完成 CATIA COM 注册。

如果安装时提示未检测到 `CATIA.Application`，请在目标电脑上运行 CATIA 自带的 `V5RegServer.exe`，例如：

```powershell
& 'D:\CATIAr20\win_b64\code\bin\V5RegServer.exe'
```

具体路径以目标电脑 CATIA 安装目录为准。

## 版本管理建议

- `main` 分支保存稳定版本。
- 新功能使用 `feature/...` 分支开发。
- 重要阶段使用 tag 标记，例如 `v0.1-wing-section-ui`。
- 不提交 `bin/`、`obj/`、`.vs/`、`.nuget/`、`.buildcheck/`、`artifacts/`、`.CATPart` 等生成物。

## 注意事项

- CATIA 建模失败时程序会直接暴露错误，不使用降级模型或伪成功结果。
- 修改 CATIA 建模逻辑后，应在 CATIA V5 R20 中实际生成 CATPart 验证。
- 当前程序面向单侧半翼参数化建模，不包含气动分析或结构分析功能。
