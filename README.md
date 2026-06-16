# CATIA Wing Designer

基于 C# WPF 的 CATIA V5 R20 机翼曲面二次开发课程项目。

## 功能

- 类 OpenVSP 的翼段 Driver Group 参数驱动。
- NACA 4 位翼型和 Selig 风格 `.dat` 翼型导入。
- WPF + HelixToolkit 线框和半透明曲面预览。
- JSON 保存/加载设计参数。
- 通过 CATIA COM Automation 新建 CATPart 并生成带命名特征树的机翼曲面。

## 环境

- Windows
- Visual Studio / Build Tools，支持 .NET Framework 4.8
- CATIA V5 R20，用于实际建模联调

当前代码不在未安装 CATIA 的环境中模拟建模；缺少 CATIA 时生成动作会直接报错。
