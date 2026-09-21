# 第三方组件与署名

版本说明：v1.1.1 为编辑器编号池工具更新（容量估算、默认池缩容与烘焙修复）；本次更新不变更下述许可与署名条款。

本模块是 [YamaPlayer](https://github.com/koorimizuw/YamaPlayer) 的扩展，依赖并复用了它的部分内容。
下面列出这些东西的来源与适用条款。

## 1. YamaPlayer（必需依赖）

- 名称：**YamaPlayer**（VPM 包名 `net.kwxxw.yama-stream`）
- 作者：**kwxxw / Yamadev**（<https://yamadev.booth.pm>，<https://github.com/koorimizuw/YamaPlayer>）
- 许可：上游仓库**没有 LICENSE 文件**，条款写在它的 `README.md`「利用規約」里：

  > **利用規約**
  > 許可：改変 / ワールドの一部としてVRChatでの公開 / 有償・無償問わず販売ワールドアセットへの取り組み
  > クレジット記載は任意です、ただし販売ワールドアセットへ取り組む場合クレジット記載は必須です。

  （English, from `README-en.md`）
  > **Terms of Use** — Allowed: Modification / Publication in VRChat as part of a world /
  > Inclusion in paid or free world assets for sale.
  > Credit is optional, but it is **required** when including in world assets for sale.

**本模块的使用方式与这些条款一致**：它是一个被改変（编写）的扩展模块，
只作为世界的一部分使用；**打包进售卖的世界资产时请务必保留对 YamaPlayer 的署名**。

本模块**不包含** YamaPlayer 本体 —— 发布包只有 `Modules/BilibiliSearch/` 里的内容，
YamaPlayer 由使用者自行获取。

## 2. 本模块中改编自 YamaPlayer 的部分

| 文件 | 来源 | 说明 |
| --- | --- | --- |
| `BilibiliResultList.cs` | YamaPlayer `Runtime/Internal/UI/LoopScroll.cs` | 同一套循环滚动逻辑的模块内版本（改名 + 注释 + 去掉对核心补丁的依赖）。按上游条款属于「改変」，版权归 YamaPlayer 作者 |

> 之所以自带这份列表：YamaPlayer 的 `LoopScroll` 存在行池只量一次视口高度、
> 复用行保留横向偏移的问题，而模块改不了核心代码。详见 [DEVELOPMENT.md](DEVELOPMENT.md) 第九节。

## 3. 图标与图片

| 文件 | 来源 / 版权 |
| --- | --- |
| `vrchat.png` / `twitter.png` / `github.png` | 不在本包内，运行时直接复用 YamaPlayer 自带的图标（同上游条款） |
| `DeepSeekIcon.png` | DeepSeek 官方标识（`favicon.svg` 光栅化而来），版权归 DeepSeek 所有 |
| `CodexIcon.png` | Codex（OpenAI）官方标识，版权归 OpenAI 所有 |
| `Author.png` | 本模块作者提供的头像 |
| `BilibiliIcon.png` / `PanelFrame.png` | 本模块用代码生成的图形（生成物，不在源码包内） |

DeepSeek / Codex 的标识仅用于**标注使用了它们的 AI 辅助开发**（指明来源），
不表示它们对本模块有任何背书或关联。

## 4. AI 辅助声明

本模块的代码与文档在 **DeepSeek** 与 **Codex** 辅助下编写完成（见 [README.md](README.md)）；
版本浮层里的两个 AI 图标就是这个用途的署名。

## 5. 其它依赖

| 依赖 | 许可 |
| --- | --- |
| VRChat SDK – Worlds（`com.vrchat.worlds`） | VRChat SDK 许可（<https://vrchat.com/home/download>） |
| UdonSharp（随 VRChat SDK 分发） | MIT |
| Unity 2022.3 内置模块 | Unity 条款 |
