# 冷启动安装验证（给新机器 / 干净 DSH 环境）

> 目的：验证"**别人第一次装**"是否真的能跑起来 —— 开发机上的 30 多次验证都在既有配置下完成，这是唯一还缺的一环。

## 0. 在新机器上准备
- 系统：**Windows 10 / 11**
- 运行时：**.NET Framework 4.x**（Win10/11 自带；若不确定，安装后看日志是否报缺 Framework）
- DSH Desktop 已安装并**已登录**（未登录会导致余额取不到）

## 1. 把插件目录拷到新机器
从旧机器拷贝整个目录（约 15 MB）：
```
D:\DeepSeek\deepseek-pet-plugin   →   新机器任意位置，例如 D:\dsh-deepseek-pet
```
> `.git` 目录可一并拷贝（便于后续 pull）；不拷也不影响安装。

## 2. 安装（二选一）
**方式 A：本地路径安装（最快，先验机制）**
在新机器的 DSH 会话里执行：
```
plugin_manager install_bundle file:D:\dsh-deepseek-pet
```
**方式 B：GitHub 安装（社区真实路径）**
先把仓库 push 到 GitHub（见下方"发布命令"），然后：
```
plugin_manager install_bundle github:Yoshino-JF/dsh-deepseek-pet
```
**方式 C：npm 安装（发布到 npm 之后）**
```
plugin_manager install_bundle dsh-deepseek-pet
```
安装成功的标志（工具输出）：`stage: enable` / `application: applied`，并且 pnpm 输出里出现 `+ <包名> file:...`。

## 3. 重启 DSH
插件首次生效需要重启宿主（若安装时提示 pending builds，需先授权执行安装脚本）。

## 4. 验收清单（全过才算通过）
- [ ] **托盘图标**出现
- [ ] **她在桌面右下角**（默认启动延迟 3 秒）
- [ ] **贴纸显示余额**（不是 `--`）；点"立即刷新余额"能更新
- [ ] **单击她**：蹦跳 + 头顶星星 + 一句台词
- [ ] **右键菜单是用户版**：顶层为 `余额 / 让她做什么（状态）/ 表情 / 动画 / 游戏 / 外观与位置 / 关于 / 退出桌宠`，**没有**"开发者：…"项
- [ ] **点游戏屏**能换游戏（MC 3D ↔ 2D ↔ galgame）
- [ ] 日志 `%APPDATA%\deepseek-pet\pet.log` 有启动行与 `say:` 记录
- [ ] **卸载插件后她仍在**（设计如此）；右键"退出桌宠"能正常关掉

## 5. 常见问题排查
| 现象 | 排查 |
| --- | --- |
| 她没出现 | 看 `%APPDATA%\deepseek-pet\pet.log`；常见是杀软拦 exe 或缺 .NET Framework |
| 余额一直 `--` | DSH 未登录 / 网络不可达 `api.deepseek.com`；连续 4 次失败她会提示一次 |
| 安装报 `bundle-in-use` | 该 bundle 正被运行中的实例占用 → **重启 DSH** 再操作 |
| 插件装上但没反应 | 确认插件面板里是 `enabled`；确认宿主已重启 |

## 6. 发布命令（在开发机执行，一次性）
```bash
cd D:\DeepSeek\deepseek-pet-plugin
git add -A
git commit -m "v0.2.2: DeepSeek 桌宠（立绘 / 游戏屏 / 台词 / 余额播报）"
# 在 GitHub 新建空仓库 dsh-deepseek-pet（不要勾 README/.gitignore）
git remote add origin https://github.com/Yoshino-JF/dsh-deepseek-pet.git
git branch -M main
git push -u origin main
git tag v0.2.2 && git push origin v0.2.2
# 可选：发到 npm（需本机有 npm 命令）
# npm login --auth-type=web
# npm publish --access public
```