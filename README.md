# Bilibili 视频搜索模块（YamaPlayer 扩展）

给 [YamaPlayer](https://github.com/koorimizuw/YamaPlayer) 的屏幕 UI 加一个 B 站面板：
**关键词搜索**结果列表，或者直接**粘贴 B 站链接 / BV 号**播放，支持复制链接与待播队列。

> **本模块不含后端。** 搜索和播放都请求一个**你自己部署的** bilibili 播放服务；
> 仓库里没有任何服务器地址，地址由使用者在生成工具里自己填。
> 后端要实现什么见 [BACKEND.md](BACKEND.md)。
>
> **本仓库只有模块本身**（`Modules/BilibiliSearch/`），不含 YamaPlayer 本体：请先自备
> YamaPlayer（**v2.0.0 及以上**），再把模块放进它的 `Modules/` 下。

> ### 🤖 关于 AI 辅助
> 本模块的代码与文档是在 **DeepSeek** 与 **Codex** 辅助下编写完成的（设计、实现、
> 排错与文档均有 AI 参与，人工负责需求与验收）。版本浮层里的两个 AI 图标就是这个用途的署名。

---

## 功能

- **两个标签页**（默认停在「网址输入」）
  - **网址输入**：输入栏预填 `{base}?url=`，点一下会重置前缀，玩家补上 B 站链接或 BV 号即可播放
  - **关键词搜索**：`{base}?page=1&keyword=` + 结果列表（标题 / UP主 + BV号 / 简介）
- 每条结果：**复制链接**、**播放**（已在播则自动加入待播队列）、**加入待播队列**（10 秒防重复）
- 上一页 / 下一页（VRChat 限制：翻页需要玩家粘贴一次新 URL 并确认）
- 版本浮层：作者头像、社交账号、AI 署名、更新履历（右半边可滚动）
- 9 种语言；配色跟随 YamaPlayer 的外观设置（`ColorDefinition`）

## 环境要求

| 项 | 要求 |
| --- | --- |
| Unity | **2022.3.22f1** |
| VRChat SDK | Worlds **3.8.1 或更高**（开发用 3.10.5） |
| YamaPlayer | **v2.0.0 及以上**（开发时使用 **v2.0.0-beta.7**） |
| 后端服务 | 必须，自建，见 [BACKEND.md](BACKEND.md) |

## 安装与配置

完整步骤见 **[INSTALL.md](INSTALL.md)**，要点：

1. 把 `Modules/BilibiliSearch/` 放进 YamaPlayer 包的 `Modules/` 下（**不用改 YamaPlayer 核心**）；
2. `Tools → YamaPlayer → Bilibili Search Setup` 里填 **Base URL**（你自己的后端，
   例如 `https://bili.example.com/player/`），点 **Generate Prefabs**。
   没填之前生成按钮是灰的 —— 仓库不发布任何服务器地址；
3. 把生成出来的模块 prefab 加到场景里 YamaPlayer 的 `ModuleManager` 上；
4. VRChat 客户端里打开 **Allow Untrusted URLs**（自建域名不在信任列表里）。

## 使用

1. 点主页面左侧图标列最上面的 **B 站图标** 展开面板；
2. **网址输入**页：点一下输入栏（前缀会自动填好）→ 粘贴 B 站链接或 BV 号 → 点 **播放**。
   没在播就直接播，已经在播就加入待播队列（面板不自动关，用旁边的 **关闭** 回主界面）；
3. **关键词搜索**页：在搜索框 `keyword=` 后面补关键词 → 搜索 → 结果里复制 / 播放 / 加队列；
4. 其他站点（YouTube 等）请用 YamaPlayer 自己的「输入 URL」，本模块不碰它。

## 目录

```
Modules/BilibiliSearch/
├─ BilibiliSearch.cs / Service / Result / ResultAction / UI   模块与面板逻辑
├─ BiliUrlUtility.cs / BiliText.cs                            请求 URL 解析、文案兜底
├─ Localization.Runtime.json                                  面板文案（9 种语言）
├─ Author.png / DeepSeekIcon.png / CodexIcon.png              版本浮层用的图片
├─ INSTALL.md / BACKEND.md / DEVELOPMENT.md                   安装 / 后端 / 开发笔记
└─ Editor/                                                    生成 prefab 的工具与修复工具
```

> `BilibiliSearch.prefab`、`BilibiliSearchPanel.prefab`、`BilibiliIcon.png`、`PanelFrame.png`
> 是**生成物**，不在仓库里：第一次用要先跑上面第 3 步生成。

## 文档

| 文件 | 内容 |
| --- | --- |
| [INSTALL.md](INSTALL.md) | 安装、配置后端地址、VRChat 设置、排错 |
| [BACKEND.md](BACKEND.md) | 后端接口契约（搜索接口、播放接口、字段表、自测清单） |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 文件清单、架构、布局公式、踩过的坑、核心改动 |
| [LICENSE.md](LICENSE.md) / [NOTICE.md](NOTICE.md) | 本模块的许可证（MIT）与第三方署名 |

> **卸载**：先从场景移除模块实例（`ModuleManager` 的 Delete，或
> `Tools → YamaPlayer → Bilibili Search → Uninstall from Scene`），保存场景，
> 再删掉 `Modules/BilibiliSearch/` 目录即可；详见 [INSTALL.md](INSTALL.md) 4.1。

## 许可与致谢

- **本模块自身**（`Modules/BilibiliSearch/`，除少数例外）采用 **MIT License**，见 [LICENSE.md](LICENSE.md)。
- 例外与第三方署名见 [NOTICE.md](NOTICE.md)：`BilibiliResultList.cs` 改编自 YamaPlayer 的
  `LoopScroll.cs`、社交图标复用 YamaPlayer 自带资源、DeepSeek / Codex 标识归各自所有者。
- 基于 [YamaPlayer](https://github.com/koorimizuw/YamaPlayer)（作者 **kwxxw / Yamadev**）开发：
  上游条款允许改変、允许作为世界的一部分公开、允许用于有偿/无偿世界资产，
  **打包进售卖的世界资产时必须署名**。
- 后端不在本仓库内；B 站数据的使用请自行评估并遵守相关服务条款。
