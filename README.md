# Bilibili 视频搜索模块（YamaPlayer 扩展）

> **当前版本 v1.1.2（2026-09-22）**：编号池配置并入 **Bilibili Search Setup**（Base URL 下方），填好后一次 **Generate Prefabs** 连面板、模块与 `?srid=` 地址池一起生成；默认池为 `500000`–`542002`（42003 条），推荐上限 `50000`。更新内容见 [CHANGELOG.md](CHANGELOG.md)，配置与限制见 [DIRECT_ACTIONS.md](DIRECT_ACTIONS.md)。

给 [YamaPlayer](https://github.com/koorimizuw/YamaPlayer) 的屏幕 UI 加一个 B 站面板：
**关键词搜索**结果列表，或者直接**粘贴 B 站链接 / BV 号**播放，支持复制链接与待播队列。

> **本模块不含后端。** 搜索和播放都请求一个**你自己部署的** bilibili 播放服务；
> 仓库里没有任何服务器地址，地址由使用者在生成工具里自己填。
> 后端要实现什么见 [BACKEND.md](BACKEND.md)。

> ### 📦 本仓库与 YamaPlayer 的对应关系
> 本仓库是**独立的模块包，不含 YamaPlayer 本体**。仓库根目录的内容（见下方「目录」一节）
> 在 YamaPlayer 包里的位置是 **`Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/`**。
> 请先自备 **YamaPlayer v2.0.0 及以上**，再把仓库根目录的内容整体放进那个 `Modules/BilibiliSearch/`
> 文件夹（保留 `Editor/` 子目录与全部 `.meta`）。仓库根目录**没有** `Assets/`、`Packages/` 包裹层，
> 所以不要把整份仓库当成 Unity 工程打开。

> ### 🤖 关于 AI 辅助
> 本模块的代码与文档是在 **DeepSeek** 与 **Codex** 辅助下编写完成的（设计、实现、
> 排错与文档均有 AI 参与，人工负责需求与验收）。版本浮层里的两个 AI 图标就是这个用途的署名。

---

## 功能

- **两个标签页**（默认停在「网址输入」）
  - **网址输入**：输入栏预填 `{base}?url=`，点一下会重置前缀，玩家补上 B 站链接或 BV 号即可播放
  - **关键词搜索**：`{base}?page=1&keyword=` + 结果列表（标题 / UP主 + BV号 / 简介）
- 每条结果：**复制链接**、**播放**（已在播则自动加入待播队列）、**加入待播队列**（10 秒防重复）
- 上一页 / 下一页：点击直接刷新结果，无需复制粘贴
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

1. 把本仓库根目录的内容整体放进 YamaPlayer 包的 `Modules/BilibiliSearch/`
   （保留 `Editor/` 子目录与全部 `.meta`）（**不用改 YamaPlayer 核心**）；
2. `Tools → YamaPlayer → Bilibili Search Setup` 里填 **Base URL**（你自己的后端，
   例如 `https://bili.example.com/player/`），并在同一窗口的 **Direct Action URLs** 一栏
   设置编号范围（默认 `500000`–`542002`），点 **Generate Prefabs**；
   没填 Base URL 之前生成按钮是灰的 —— 仓库不发布任何服务器地址；
3. 生成结果包含模块 prefab、面板 prefab 和完整的 `?srid=` 地址池；
4. 把生成出来的模块 prefab 加到场景里 YamaPlayer 的 `ModuleManager` 上；
5. VRChat 客户端里打开 **Allow Untrusted URLs**（自建域名不在信任列表里）。

## 使用

1. 点主页面左侧图标列最上面的 **B 站图标** 展开面板；
2. **网址输入**页：点一下输入栏（前缀会自动填好）→ 粘贴 B 站链接或 BV 号 → 点 **播放**。
   没在播就直接播，已经在播就加入待播队列（面板不自动关，用旁边的 **关闭** 回主界面）；
3. **关键词搜索**页：在搜索框 `keyword=` 后面补关键词 → 搜索 → 结果里复制 / 播放 / 加队列；
4. 其他站点（YouTube 等）请用 YamaPlayer 自己的「输入 URL」，本模块不碰它。

## 目录

```
Modules/BilibiliSearch/                    ← = 本仓库根目录的全部内容
├─ BilibiliSearch.cs / Service / Result / ResultAction / UI   模块与面板逻辑
├─ BilibiliResultList.cs / BiliUrlUtility.cs / BiliText.cs    结果列表、URL 解析、文案兜底
├─ Localization.Runtime.json                                  面板文案（9 种语言）
├─ Author.png / DeepSeekIcon.png / CodexIcon.png              版本浮层用的图片
├─ *.asset                                                     UdonSharp program asset
├─ README.md / INSTALL.md / BACKEND.md / DEVELOPMENT.md       说明文档
├─ CHANGELOG.md / DIRECT_ACTIONS.md                           版本履历 / 直接操作与编号池
├─ LICENSE.md / NOTICE.md                                     许可证与第三方署名
└─ Editor/                                                    生成 prefab、烘焙编号池、修复与回归测试工具
```

> `BilibiliSearch.prefab`、`BilibiliSearchPanel.prefab`、`BilibiliIcon.png`、`PanelFrame.png`
> 是**生成物**，不在仓库里：第一次用要先跑上面第 2 步生成。

## 文档

| 文件 | 内容 |
| --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | **版本号与更新内容的唯一出处** |
| [INSTALL.md](INSTALL.md) | 安装、配置后端地址、VRChat 设置、排错 |
| [DIRECT_ACTIONS.md](DIRECT_ACTIONS.md) | 直接操作与记录 URL 池：原理、烘焙、范围限制、后端约定 |
| [BACKEND.md](BACKEND.md) | 后端接口契约（搜索接口、记录与媒体链路、字段表、对接验证） |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 文件清单、架构、布局公式、踩过的坑、与核心的关系 |
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
