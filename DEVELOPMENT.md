# Bilibili 视频搜索模块 —— 开发与排错笔记

> 面向二次开发：文件清单、架构取舍、布局公式、真实踩过的坑、对 YamaPlayer 核心做的改动。
> 安装、配置与使用请看 [README.md](README.md) 和 [INSTALL.md](INSTALL.md)；
> 后端接口契约看 [BACKEND.md](BACKEND.md)。

---
## 一、文件清单

```
Modules/BilibiliSearch/
├─ BilibiliSearch.cs                  模块入口（YamaPlayerModule），面板调用它
├─ BilibiliSearchService.cs           拼 URL、下载 JSON、解析
├─ BilibiliSearchResult.cs            一页搜索结果的纯数据容器
├─ BilibiliSearchUI.cs                面板显示 / 滚动 / 三个操作按钮
├─ BilibiliSearchResultAction.cs      单元格内按钮 → 面板的转发器
├─ BiliResultList.cs                  结果列表的循环滚动（模块自带，不依赖核心修复）
├─ BiliUrlUtility.cs                  请求 URL 的解析 / 改页（纯静态工具）
├─ BiliText.cs                        面板文案的内置兜底表（编辑器 + 运行时共用）
├─ *.asset                            UdonSharp 的 program asset（Unity 自动生成，跟着走）
├─ BilibiliSearch.prefab              ★ 模块 prefab（生成在这里，随模块一起分发）
├─ BilibiliSearchPanel.prefab          面板 prefab（同上）
├─ BilibiliIcon.png                    左下角/图标列用的图标（同上）
├─ DeepSeekIcon.png / CodexIcon.png   版本浮层里两个 AI 署名的图标（官方 logo，纯白 + 透明）
├─ Author.png                          版本浮层里的作者头像（自己替换即可）
├─ Localization.Runtime.json          面板文案（9 种语言）
├─ INSTALL.md                          安装与配置说明（面向使用者）
├─ LICENSE.md / NOTICE.md              许可证（MIT）与第三方署名
├─ BACKEND.md                          后端接口要求（自建服务必须满足的契约）
├─ DEVELOPMENT.md                      本文（开发与排错笔记）
├─ Yamadev.YamaStream.Modules.BilibiliSearch.asmdef
└─ Editor/
   ├─ BilibiliSearchPanelSetup.cs     一键生成 prefab 的工具
   ├─ BilibiliSearchRepair.cs         场景里模块的修复 / 绑定工具
   ├─ Localization.Editor.json        模块名 / 描述（编辑器显示）
   └─ Yamadev.YamaStream.Modules.BilibiliSearch.Editor.asmdef
```

> ★ 三个生成物**故意放在模块目录里**（和 `Modules/PitchShifter` 等官方模块一样），
> 这样整个 `Modules/BilibiliSearch` 目录就是自包含的：拷到别的工程即可用，
> ModuleManager 能直接扫描到模块并读出 `Localization.Editor.json` 里的模块名/描述。

## 二、安装步骤

> 面向使用者的完整步骤（含前置条件、后端地址配置、VRChat 设置、排错）在
> [INSTALL.md](INSTALL.md)；这里只记开发视角的要点。

### A. 在另一个工程里安装（推荐流程）

1. 把整个 `Modules/BilibiliSearch` 目录拷到那个工程的
   `Packages/net.kwxxw.yama-stream/Modules/` 下（和 `Modules/PitchShifter` 同级）。
   **连同 `.asset` / `.png` / `.meta` 一起拷**，缺 `.meta` 会丢 GUID。
   （`.prefab` 和 `BilibiliIcon.png` / `PanelFrame.png` 是**生成物**，不用拷，
   到了新工程重新 Generate 一次即可；`DeepSeekIcon.png` / `CodexIcon.png` /
   `Author.png` 是随模块分发的资源，要拷。）
2. 不需要改 YamaPlayer 核心：模块自带结果列表。第九节里那条
   `Editor/Module/ModuleManagerEditor.cs` 的改动是可选的（让模块名立刻显示正确）。
3. 等 Unity 编译完（Console 不能有红色报错）。
4. **配置后端地址**：菜单 **Tools → YamaPlayer → Bilibili Search Setup**，
   在 **Base URL** 里填你自己的后端地址（例如 `https://bili.example.com/player/`），
   然后点 **Generate Prefabs**。仓库里**不含任何服务器地址**，不填这个地址按钮是灰的。
5. 在场景里选中 YamaPlayer 的 **`ModuleManager`**，在 **可用模块** 里点我们的模块的
   **Add**，模块实例会挂到 ModuleManager 下面。
   - 如果这里显示的是 `module.bilibilisearch.name` 而不是「哔哩哔哩搜索」，
     说明编辑器翻译缓存还是旧的：重新点一下 ModuleManager（重开 Inspector）就会刷新。
6. 上传前照常走 YamaPlayer 的 build，模块会通过两个 `ModuleUISlot` 注入 UI
   （见下面第 5 条）。

### B. 直接在本仓库/本工程里用

先填好后端地址，再用菜单 **Tools → YamaPlayer → Bilibili Search Setup**，
点 **Generate Prefabs**（或菜单 **Tools / YamaPlayer / Bilibili Search / Generate Prefabs**）。
生成物就是上面 ★ 那三个，位置在模块目录里。

> 早期版本把 prefab 生成在 `Assets/Yamadev/YamaPlayerGenerated/`，现在这个工具会用
> `AssetDatabase.MoveAsset` 把它们**移动到模块目录**（保留 GUID，所以场景里已经放好的
> 模块实例不会丢引用）。移动完 `Assets/Yamadev/YamaPlayerGenerated/` 空了就可以删掉。

### 卸载

- **场景侧**：`ModuleManager` 里该模块的 **Delete** 按钮，或菜单
  **Tools → YamaPlayer → Bilibili Search → Uninstall from Scene**
  （`BilibiliSearchRepair.UninstallFromScene()`：删掉场景里所有模块实例 + 清理旧版本挂在
  YamaPlayer URL 框上的点击事件 + 标记场景已修改）。
- **资源侧**：删掉整个 `Modules/BilibiliSearch/` 目录 —— 生成物、U# program asset、
  本地化、图标都在里面，不会有残留。
- **顺序：先删场景实例，再删目录**，否则场景里会留下 Missing Prefab。
- 两处 YamaPlayer 核心修复**不用回滚**（独立 bug 修复，不依赖本模块）。

无论走哪条路，上传前照常走 YamaPlayer 的 build。模块通过两个 `ModuleUISlot` 注入 UI：

- `Canvas/User/Main` ← 面板本体（盖在主页面之上，默认隐藏）
- `Canvas/User/Main/LeftSide/Container`（`siblingIndex = 0`）← B 站图标按钮，
  位置就在主页面左侧那一列、原来「输入 URL」图标**上面**，点击展开 / 收起面板

### 在 Editor 里 Play 测试需要 ClientSim

Play 模式下如果没启用 ClientSim，`Networking.LocalPlayer` 为 null，
YamaPlayer 本体在 `Update()` 里调 `VRCPlayerApi.GetCurrentLanguage()` 会抛：

```
Udon runtime exception: EXTERN to 'VRCSDKBaseVRCPlayerApi.__GetCurrentLanguage__SystemString'
  → NullReferenceException
UIController.Localization.cs(132,49)
```

**这跟本模块无关**，是 YamaPlayer 本地化在缺本地玩家时的必然结果。
但它会让 `UIController` 的 Udon 行为被 halt，面板文字也会跟着失效。

装法：工程的 `Packages/vpm-manifest.json` 里加

```json
"com.vrchat.clientsim": { "version": "1.2.4" }
```

（`locked` 段同样加一条），再用 VCC 打开该工程让它解析安装；
或在 Unity 里走 `VRChat SDK → ClientSim → Enable ClientSim`。

## 三、使用

> **重要（VRChat 的限制）**：脚本**不能**把字符串变成 URL。
> Udon 里 `new VRCUrl(string)` 不可用，`VRCUrlInputField.text` 也不可写。
> 所以请求 URL 的**最终值只能由玩家在 URL 输入框里补全**，这正是搜索框用
> `VRCUrlInputField` 的原因。生成工具会把默认前缀**预填**进输入框（并序列化成
> `_defaultSearchUrl`），玩家只需要在光标后面补关键词。

- **入口**：主页面左侧图标列最上面那个 **B 站图标**（就在原来的「输入 URL」图标上方）。
  点它展开面板，再点一次收起；面板里的 **关闭** 按钮同样能收起。
- **搜索**：搜索框已经预填好（`{你的后端}` 是生成时在工具里填的地址，见 [INSTALL.md](INSTALL.md)）
  ```
  {你的后端}/player/?page=1&keyword=
  ```
  点搜索框弹出 VRChat 键盘，在 `keyword=` 后面补上关键词即可。点「搜索」发送请求。
  > 搜索框的内容是**每次点击都会重置**的：搜索框上挂了一个 `EventTrigger`（`PointerDown`
  > → `BilibiliSearchUI.ResetSearchInput()`），在输入框自己激活、VRChat 键盘弹出**之前**
  > 就把内容写回默认前缀，所以搜过一次之后再点搜索框不会残留上一次的完整 URL。
  > `SetUrl` 会顺手把光标放到文本末尾（正好是 `keyword=` 后面），接着打字就是补关键词。
  > 注意副作用：键盘已经打开时再点一次搜索框也会重置（会丢掉刚打的字）。
  > 另外面板第一次被激活时 `VRCUrlInputField.Awake()` 会清空保存的文本，所以
  > `ShowPanel(true)` 之后也会再补一次默认值。
- **翻页**：点「上一页 / 下一页」时脚本会弹出一个确认框 —— 因为 VRChat 只认可玩家
  输入的 URL，脚本不能代改。按提示把框里的新 URL 复制、粘贴到 URL 框并完成输入，
  再点「确认执行」。
- 每条结果之间有一条 **分割线**（结果单元格底部的 3px 亮条），按钮下面留了一行空白再画线。
- 每条结果：
  - **复制链接**：弹出链接确认框，点链接后用 VRChat 键盘的复制功能复制
    `https://www.bilibili.com/video/BV号`。这个框**只有「关闭」**（复制完就是关掉，
    「确认执行」在这里没有别的意义，所以被隐藏了）。
  - **播放**：仍然是「关闭 + 确认执行」两个按钮 —— 粘贴播放 URL 并完成输入后点确认。
    - **当前没有在播** → 直接播放，然后**自动关掉确认框和搜索面板，回到主界面**。
    - **当前已有视频在播** → 不打断它，改为把这条**加入待播队列**，面板保持打开并提示
      「当前有视频正在播放，已加入待播队列。」。
  - **添加到待播队列**：加入 YamaPlayer 的播放队列（`Controller.Queue.AddTrack`）。
    加入成功后该条的队列按钮**禁用 10 秒**，防止重复添加。
- **关于队列按钮有时还要粘贴一次 URL**：VRChat 只认可**玩家输入**的 `VRCUrl`，脚本无法
  用字符串构造 `VRCUrl`（`new VRCUrl(string)` 在 Udon 里不可用）。所以：
  - 如果你已经为这条视频粘贴确认过一次播放链接，脚本会**记住这个 VRCUrl**，
    之后点「添加到待播队列」就是**一次点击**直接入队；
  - 还没确认过时，会先弹出和播放一样的确认框让你粘贴一次播放链接。
- **版本按钮**：标题右边紧挨着一个小按钮（显示 `v1.0.0`），点开一个版本浮层。浮层仿照
  YamaPlayer 自己的版本信息页排版：
  - 顶部**居中**的项目名 + 版本号 `BiliBili Search v1.0.0`（Primary 配色），
    **竖直分割线的顶端正好接在它下面**；
  - 分割线**左半边**：作者头像在上（320×240，占分割线上半段），下面五行
    （`VRChat / Twitter / Github / DeepSeek / Codex`），第一行与头像之间**空一行**
    （= 一个行高 + 行距），整块（图标 + 值）**在头像的垂直中线上居中**
    （取最宽那一行定块宽，所以图标仍然是一列，五行上下对齐）；行高 / 行距 / 头像尺寸
    都是文件顶部的 `Version*` 常量；
    - 后两行是 **AI 辅助署名**：`DeepSeek-assisted development` / `Codex-assisted development`，
      图标是随模块分发的官方 logo（`DeepSeekIcon.png` / `CodexIcon.png`，纯白 + 透明，见第六节）；
      （这两行文字最长，行块宽度由它们决定，图标列仍然对齐、整块仍在头像中线上居中）
  - 分割线**右半边**：`更新履歴` 标题 + 更新记录，**可滚动**（`ScrollRect` +
    `ContentSizeFitter`，内容有多高就滚多高）——以后加记录直接往
    `VersionChangelogValue` 的文本里追加行就行；
  - 左上角是返回按钮（`← 返回`），**回到哔哩哔哩搜索面板**（不是 YamaPlayer 主界面）。
  - 整个浮层是按**父容器顶部中心**锚定的，不依赖 1600×900 这个具体尺寸。
  - 账号那三行**只有图标 + 值，没有标签文字**（`VRChat账号 / 推特 / Github / 项目名`
    这些标签已经去掉）；三行的 rect 完全一样，所以图标一列、值一列都对齐。
    项目名本身没有再单独占一行 —— 它和版本号已经在顶部大标题里了。
  - 账号值、顶部大标题、更新履歴标题和记录文字都是模块自带的数据，**不参与翻译**；
    整个浮层里唯一走本地化的只剩返回按钮
    （`module.bilibilisearch.version.back`，中/日/韩/俄/西/意都是短词，如 `返回 / 戻る / Back`）。
    返回按钮前面那个 `←` 由运行时拼上去（写进 prefab 的话每次切语言都会被整段替换掉，箭头会丢）。
  - **社交图标复用 YamaPlayer 自带的**：`vrchat.png / twitter.png / github.png` 直接从
    `Packages/net.kwxxw.yama-stream/Assets/Images/` 取（找不到就按文件名全工程搜一遍）。
  - 头像是模块目录里的 **`Author.png`**（320×240、圆形、四角透明）。工具只会强制它的导入
    设置为 `Sprite` + 保留透明通道，换图直接替换这个文件即可。
- **面板自己有两个标签页**（就在顶栏里、`v1.0.0` 左边那两个按钮，顺序是
  `[网址输入] [关键词搜索]`，`BilibiliSearchUI.ShowUrlTab()` / `ShowSearchTab()` 切换
  `UrlTab` / `SearchTab` 两个容器，**默认停在网址输入**：`ShowPanel(true)` 里会调 `ShowUrlTab()`）：
  - **关键词搜索**：搜索框（`_defaultSearchUrl`）+ 结果列表；切到它时会
    `SendCustomEventDelayedFrames(nameof(RefreshResults), 2)` 重新量一次视口，
    否则 `LoopScroll` 的行池会在标签页隐藏时量到 0 高度（prefab 里它默认是隐藏的）。
  - **网址输入**：`UrlInput`（`VRCUrlInputField`，预填 `_defaultPlayUrl` = `{base}?url=`）+
    `PlayUrlButton`(140) + `CloseUrlButton`(140，回主界面)，两个按钮等宽。
    - 下面两组说明：标题 `UrlLabel` / `SearchLabel`（26pt、`ColorType.Primary`，和按钮同色）
      各接一段正文 `UrlHint` / `SearchHint`（22pt 灰）。间距全部显式给：
      `UrlTab` 的 `VerticalLayoutGroup.spacing = 0`，顺序是
      输入栏 → `HintTopGap`(26) → 标题 → 正文 → `HintGap`(26) → 标题 → 正文 →
      `HintSpacer`（`flexibleHeight = 1`，吃掉剩余高度）。
    - 关闭按钮是给"已经在播 → 只入队、面板不自动关"那条路径用的。
    - 输入栏挂了 `EventTrigger(PointerDown)` → `ResetUrlInput()`：**点击即清除并写回前缀**；
      切到这个标签页时也会写一次（输入栏第一次激活时 `VRCUrlInputField.Awake` 会清空文本）。
    - `PlayUrlInput()` 直接播输入栏里的 `VRCUrl` —— 这个框是玩家自己输入的，所以**不需要**
      结果行那套"复制→粘贴→确认"流程。没在播就播并关面板，在播就入队。
      **没有客户端校验**：只要是空输入就提示 `msg.emptyUrl`，其它情况一律把输入栏里的 `VRCUrl`
      原样交给后端（后端同时支持 BV 号与完整链接）。播放失败 → `msg.urlPlayFailed`
      （用 `BiliUrlUtility.VideoId()` 取 BV 号当轨道标题，取不到就用 `BiliBili`）。
      前缀仍然必须由玩家保留：Udon 不能 string→VRCUrl，脚本没法替玩家补前缀；
      点输入栏会 `ResetUrlInput()` 把前缀写回（光标停在 `url=` 后）。
    - 轨道的标题取 URL 里的 BV 号（`BiliUrlUtility.VideoId()`），没有就用 `BiliBili`。
- **完全不动 YamaPlayer 自己的 URL 输入框**：之前几版在它们（主页面左列 40×40 的「输入 URL」
  图标、顶栏、Canvas 上的 URL 页面，一共 3 个 `VRCUrlInputField`）上挂过点击事件来补前缀，
  **已全部撤销**。YamaPlayer 那三个框保持原样逻辑，留给 YouTube 等其他站点。
  旧版本残留在场景里的事件由 `BilibiliSearchRepair.RemoveLegacyUrlBoxWiring()` 摘掉
  （打包时的 `BilibiliSearchUrlBoxCleanupProcess` 和 Generate / Repair 都会调）。
## 四、JSON 解析

**顶层数组和 `{"data": [...]}` 两种包装都支持**：

| 显示位置 | JSON 字段 |
| --- | --- |
| 标题 | `title` |
| UP 主 | `channelTitle` |
| 简介 | `description` |
| BV 号 | `id` |

没有用到的字段（`image` / `mid` / `recordsid`）会被忽略。一页最多显示 20 条
（`BilibiliSearchService` 的 `_maxResults` 可调）。

**`id` 不是合法 BV 号的条目会被直接过滤掉**，不会进入列表。因为「复制链接 / 播放 / 加入队列」
都要用 `id` 拼 `https://www.bilibili.com/video/<id>`，而 VRChat 又要求 URL 由玩家授权，
没有 BV 号的条目（例如接口返回的 AV 号视频、或 `id` 字段缺失）点了只会提示
「该结果没有有效 BV 号」—— 所以干脆在解析阶段就丢掉：

```csharp
if (!BiliUrlUtility.IsBv(ReadString(item, "id"))) continue;   // ParseResults 里两趟都过滤
```

因此「N 个视频」显示的是**过滤后真正可用的条数**，可能少于接口这一页返回的条数。
解析分两趟：第一趟数出合法条数（上限 `_maxResults`）用来开数组，第二趟填数据。

## 五、架构（为什么这样分层）

- **面板（`BilibiliSearchUI`）只跟模块（`BilibiliSearch`）说话**，
  播放/加队列也走 `_search.PlayResult(...)` / `_search.AddResultToQueue(...)`。
  原因：`_controller` 是 `YamaPlayerModule` 的 `protected` 字段，
  而面板继承的是 `YamaPlayerListener`，**拿不到它**。
  其它模块（`SlideShowerUI` 等）也是这个套路。
- **下载回调的参数必须是强类型 + `override`**：
  `public override void OnStringLoadSuccess(IVRCStringDownload result)`，
  `using VRC.SDK3.StringLoading;` 不能漏。
- **生成 prefab 时必须用 `UdonSharpUndo.AddComponent<T>()`** 而不是
  `gameObject.AddComponent<T>()`，否则组件没有合法的 program asset，
  一进 Play 模式 UdonSharp 就会报 `Program asset ... is not valid` 并 NRE。

## 六、可调参数

`BilibiliSearchService`（模块 prefab 的 `Service` 子物体上）：

| 字段 | 说明 |
| --- | --- |
| `_baseUrl` | 接口前缀，由生成工具填入你在 setup 窗口里配置的地址（仓库里没有默认域名） |
| `_maxResults` | 每页最多显示多少条 |

面板 prefab 根节点上的 `BilibiliSearchUI` 还有几个和 URL 有关的字段：

| 字段 | 说明 |
| --- | --- |
| `_defaultSearchUrl` | 关键词搜索页里搜索框预填的内容（`{base}?page=1&keyword=`） |
| `_defaultPlayUrl` | 网址输入页里输入栏预填的内容（`{base}?url=`），点击输入栏时写回、切到该页时也写一次 |
| `_searchTab` / `_urlTab` | 两个标签页的容器，由标签按钮切换显示 |
| `_urlInput` / `_urlHint` / `_searchHint` | 网址输入页的输入栏、以及下面两段正文（标题 `UrlLabel` / `SearchLabel` 由 `UpdateTranslation` 按名字写） |

面板布局参数都在 `BilibiliSearchPanelSetup.cs` 的 `BuildPanel()` / `BuildResultCell()` 里
（顶栏 36 = 两个标签按钮 + 版本按钮 + 状态文字 + 页码同一行、搜索行 48 = 和结果里那三个按钮一样高、结果单元格 `CellHeight` 240、
文字块 `InfoHeight` 140、按钮离底部 36 ↔ 分割线 3 等），改完重新 Generate 一次即可。

> 面板顶栏**没有标题**了（为了省纵向空间删掉了 `TitleText`）：现在是
> `[网址输入] [关键词搜索] [v1.0.0] 状态文字 … 页码` 一行（默认停在网址输入页）。
> `module.bilibilisearch.title` 这个 key 仍留在 `Localization.Runtime.json` / `BiliText.cs` 里，
> 但面板已经没有任何地方用它（`UpdateTranslation()` 只写标签、状态、按钮和浮层文案）。

**结果单元格是"标题 / UP主+BV号 同一行 / 简介"三段式**，用 `VerticalLayoutGroup` 而不是绝对定位：

```
Cell (240)
├─ Info (VerticalLayoutGroup, 140)     ← 140 是这整块的高度预算
│  ├─ Title        (无固定高度 = 文本自身 preferred 高，1~2 行)
│  ├─ Meta         (HorizontalLayoutGroup, 30) ← Channel、Id 依次紧排
│  └─ Description   (preferredHeight=0 + flexibleHeight=1，吃掉剩余高度)
├─ Actions (距底 36，高 48)
└─ Separator (底部 3px)
```

- **标题后面不留空行**：Meta 行紧跟在 Title 的**真实高度**下面，而不是固定偏移 —— 用绝对定位
  时单行标题下面必然空一行。
- **BV 号跟在 UP 主后面**：Channel / Id 都开着 `horizontalOverflow = Overflow`，
  `HorizontalLayoutGroup` 会用它们各自的 `preferredWidth` 依次紧凑排列，所以 BV 号跟着名字走，
  不是固定列。
- **简介不会被挤出去**：Description 给 `LayoutElement.preferredHeight = 0` + `flexibleHeight = 1`
  （`LayoutElement.layoutPriority = 1` 高于 `Text` 的 0，所以这个 0 会生效），
  它只占标题用剩下的高度，标题长或短都不会压到下面的按钮。
- 文字颜色全部保持原样（标题白、UP 主 `#CCA6BF`、BV 号灰、简介浅灰）。

> **两个必须记住的布局坑**（都真踩过，表现为"搜索栏高得离谱"）：
> 1. `ResultsScroll` 的 `LayoutElement` 要有 `flexibleHeight = 1`（`minHeight` 只给下限），
>    否则富余高度没有归属。
> 2. `TopBar` / `SearchRow` 用的是 `NewLayoutRow()`，它内部是
>    `HorizontalLayoutGroup` + `childForceExpandHeight = true`。**`LayoutGroup` 本身也是
>    `ILayoutElement`**，会把这种"强制撑开子物体"上报成自己的 `flexibleHeight = 1`；
>    而 `LayoutUtility.GetFlexibleHeight()` 只在 `LayoutElement.flexibleHeight >= 0` 时
>    才用更高 `layoutPriority` 的 `LayoutElement` 覆盖它。所以 `NewLayoutRow()` 里必须写
>    `layout.flexibleHeight = 0f` —— 留着默认的 `-1` 会被跳过，那一行就会被当成弹性行，
>    分走三分之一富余高度，渲染成 150 多像素高（设计值 30）。

图标形状在 `ResolveBilibiliIcon()` 里用像素画出来（128×128，形状按 256 空间描述再降采样），
`BilibiliIcon.png` 删掉后会重新生成。版本浮层里的 **DeepSeek / Codex** 两个图标则是随模块
分发的图片资源（各 128×128、纯白 + 透明通道，2～3 KB）：

| 文件 | 来源 |
| --- | --- |
| `DeepSeekIcon.png` | DeepSeek 官方 favicon（`favicon.svg`，50×50 单路径）离屏光栅化成 128×128 |
| `CodexIcon.png` | 用户提供的 `codex.png`（640×640，白色 + 透明）按 alpha 归一化后降采样到 128×128 |

两者都只保留 alpha 通道、颜色统一为白色，所以放在深色浮层上和 YamaPlayer 自带的社交图标是
同一种观感。想换图标直接替换这两个 png（保持文件名）即可，工具的 `ResolveIcon()` 会强制它们
按 UI sprite 导入。

## 七、排错（这几条都真实踩过）

**最快的自查方式：直接读 Unity 的 Editor.log**（比 Console 显示得全）：

```
%LOCALAPPDATA%\Unity\Editor\Editor.log
```

搜 `error CS`，能拿到**全部**报错 —— Console 面板只列前几条，真正的元凶常常被挤掉。

| 报错 | 真实原因 | 改法 |
| --- | --- | --- |
| `CS0246: BilibiliSearchUI could not be found`（Editor 脚本里，4 条） | **Editor asmdef 没引用模块自己的 asmdef** | Editor asmdef 的 `references` 里补 `GUID:b963ea13fc7fbf54d8842912fa2c92ca`（= 模块 asmdef 的 guid）。可对照 `Modules/PitchShifter/Editor/*.Editor.asmdef` |
| 移植到别的项目后**关键词搜索最多只显示一条视频**、或**结果行整体偏右 / 行叠在一起**，本项目正常 | 那个项目的 `LoopScroll.cs` 是 YamaPlayer 原版（模块单独拷过去了，两处核心修复没跟过去）。原版在列表第一次初始化时量视口高度，而这时列表所在页面刚被激活、视口还没算出高度，行池就固定成 1 行且永不增长 | ① 合入 `LoopScroll.cs` 的修复（见第 2 步 / 第九节）—— 生成窗口会用 `BilibiliSearchRepair.HasLoopScrollFix()` 检测并弹红框；② 模块侧也已经迁就：结果列表会等它所在标签页显示一帧后才激活，让 `LoopScroll` 在视口有高度之后才首次初始化（解决"只有一行"）—— 但行复用坐标归位、底部半行仍在核心代码里，不打补丁仍会偏右/叠行 |
| `CS0246: IVRCStringDownload could not be found` | 漏 `using VRC.SDK3.StringLoading;` | 补 using。**不要**改用 `object` 参数，会丢掉 Udon 的强类型派发 |
| `CS0103: The name 'Utilities' does not exist` | 漏 `using VRC.SDKBase;` | 补 using |
| `CS1061: 'BilibiliSearch' does not contain 'Search'` | 模块里没有对应方法 | 面板调用的每个动作都要在 `BilibiliSearch` 上有对应 public 方法 |
| `CS0103: The name '_controller' does not exist`（面板里） | 面板继承 `YamaPlayerListener`，没有这个字段 | 改成走模块方法（见第五节） |
| `ArgumentException: Arial.ttf is no longer a valid built in font` | Unity 2022.2+ 移除了内置 Arial | 已在 `ResolvePanelFont()` 里改成按名搜资产（优先 `LegacyRuntime.ttf`，其次 YamaPlayer 的 `ZenMaruGothic-Regular`） |
| `Program asset on ... is not valid` / `NullReferenceException in SanitizeProxyBehaviours`（进 Play 时） | 生成工具用普通 `AddComponent<T>()` 创建 UdonSharpBehaviour，**跳过了 proxy ↔ UdonBehaviour 接线** | 所有 UdonSharpBehaviour 必须用 `UdonSharpUndo.AddComponent<T>()`（原生 Unity 组件如 `Text`/`Image`/`Button` 仍用 `AddComponent`）。Editor asmdef 需引用 `UdonSharp.Editor` |
| `PostProcessing failed: ... being used by another process` | 编译期间有别的进程锁着 dll | 关掉 Unity，删 `Library/Bee` + `Library/ScriptAssemblies`，重开工程；**不要在 Unity 编译时往工程里写文件** |
| `Assembly has duplicate references` | 同一个程序集同时用名字和 GUID 引用 | 二选一 |
| 面板上显示 `module.bilibilisearch.title` 这种**原始本地化 key** | 模块的 `Localization.Runtime.json` 只在**打包时**被合并进 UIController，编辑器里（或没走 build）查不到 | 已加 `BiliText.cs` 兜底表：`GetTranslation()` 查不到就用内置中文，prefab 里也直接写好中文 |
| 往模块目录**新加一个 .cs** 后报 `CS0103: The name 'XXX' does not exist` | UdonSharp 的脚本路径缓存来自 `CompilationPipeline.GetAssemblies()`，新文件要等 Unity 重新编译一次才进列表 | 回到 Unity 让它编译（Play 模式下脚本编译会被推迟，先**退出 Play 模式**再按 Ctrl+R） |
| ModuleManager 里模块名/描述显示成 `module.bilibilisearch.name` | `EditorLocalization` 的翻译表是**每个编辑器会话只构建一次**的静态缓存；如果模块 prefab 是在这次会话里才生成的（或才拷进来），缓存里没有它的 `Localization.Editor.json` | 已修：`ModuleManagerEditor.OnEnable()` 里加了 `EditorLocalization.ReloadTranslations()`，生成工具跑完也会主动 reload。手动临时解：重开一次 ModuleManager 的 Inspector，或让 Unity 重载一次程序集（Ctrl+R） |
| 结果列表下面一大块空白，只能看到 1～2 行；滑动时最下面那行整块消失 | `LoopScroll` 只在初始化那一刻量一次视口高度来决定行池大小，而面板是"先隐藏、点图标才激活"的，激活时布局还没算完，量到的高度可能是 0 或一行 | 已修（见第九节）：行池会在 `SetUp()` 时按当前视口高度自动扩容，底部半行也会画出来 |
| 滑动时**隔几行有一两行整体偏右** | `LoopScroll` 复用行对象时只写 `anchoredPosition.y` 并保留 x，被复用的那几行会继承克隆来源的横向偏移 | 已修（见第九节）：`UpdatePosition()` 现在每次都用模板的行几何（anchors/pivot/sizeDelta/anchoredPosition）把每一行重新盖一遍 |
| 切换成英文后，第一行按钮是英文、后面几行按钮还是中文，状态栏也还是中文 | 结果单元格的按钮文字是**生成时写死进 prefab** 的（`BiliText.Get`），而 `UpdateTranslation()` 是**按名字**找控件的（`FindText` 返回第一个同名对象）—— 于是只有模板那一格（也就是第 1 行）被翻译到，运行时克隆出来的第 2 行以后全是生成时的中文。状态栏则是"写进去就不动"，切语言时不会重渲染 | 已修：`UpdateCell()` 里对每个被填充的格子调用 `SetCellLabel()` 重新翻译 `CopyButtonText` / `PlayButtonText` / `QueueButtonText`；`AfterLanguageChanged()` 除 `UpdateTranslation()` 外还会 `ForceRefreshVisibleCells()`（让当前可见行重新填一遍）和 `ApplyStatus()`。状态栏现在存的是**翻译 key + 前后缀**（`_statusKey/_statusPrefix/_statusSuffix`），所以切语言会跟着重渲染 |
| 标题（或某些视频标题/简介）在**某些语言下整块空白**，中文下却正常 | `UIController.UpdateFont(lang)` 会把**该语言配置的字体套到 UIController 下所有 Text 上**，而面板就被注入在那棵子树里。默认配置里 en / ja / es / it 用的是日文字体 `ZenMaruGothic-Regular`，它**没有「哔」「哩」这类只在中文里存在的字形** → 整段渲染成空白；zh-CN / zh-HK / ko / uk / ru 这几套语言没配字体，打包时会被 `LocalizationBuildProcess` 换成内置 `LegacyRuntime.ttf`，它有动态字体回退，所以中文下正常 | 已修：① 生成工具改用 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`（和 YamaPlayer 给"无字体语言"用的同一个），并写进面板的 `_panelFont` 字段；② `BilibiliSearchUI.RestorePanelFont()` 在 `Start()` 和每次 `AfterLanguageChanged()` 之后把面板里所有 Text 的字体恢复成它，面板不再受语言字体影响 |
| 某几条视频的三个按钮全部点了没反应，状态栏提示「该结果没有有效 BV 号」 | 接口返回的条目里 `id` 不是 BV 号（例如 AV 号视频、或字段缺失），而复制/播放/入队都要用 `id` 拼 bilibili 链接 | 已改成**在解析阶段就过滤掉**：`BilibiliSearchService.ParseResults` 只保留 `BiliUrlUtility.IsBv(id)` 为真的条目，界面上不会再出现点了没反应的行 |

**排查顺序建议**：先看 Editor.log 里**最后一条** `error CS` 和 `## Script Compilation Error`
标题，判断是"源码错"还是"程序集引用错"，再动手 —— 别只看 Console 前几条。

## 八、外观与配色（跟随 YamaPlayer 的配色方案）

面板的按钮、搜索栏、输入栏都是**圆角胶囊**，颜色**跟随 YamaPlayer 的外观设置**，
靠的是 YamaPlayer 自己的外观管线，不需要任何运行时脚本：

1. 工具给这些元素的 `Image` / `Text` 挂上 `ColorDefinition` 组件（YamaPlayer 官方的
   扩展点，`ScreenUI` 里的 Fill / Handle / CheckMark / Background 等都是这么用的）。
2. 打包时 `AppearanceBuildProcess`（`callbackOrder = -2000`）先把
   `AppearanceSettings.DefaultColorSet` 的 primary / secondary 写进 UIController，
   再遍历 `AppearanceSettings.GetComponentsInChildren<ColorDefinition>(true)`，
   把带 `ColorDefinition` 的对象直接染成对应颜色。
3. 顺序上 `YamaPlayerModuleBuildProcess`（-3000）比它**先**跑，所以我们的面板早就被注入到
   `Canvas/User/Main`、也就是 ScreenUI 的子树里了 —— 一定扫得到。

| 元素 | ColorType |
| --- | --- |
| 两个标签按钮、版本浮层的顶部大标题 `VersionName` 与 `更新履歴` 标题、搜索按钮、关闭按钮、确认执行 | `Primary` |
| 其他按钮（翻页 / 复制链接 / 播放 / 加入队列 / 取消 / 版本 / 返回）、搜索栏、确认输入框、复制栏、分割线、版本浮层的竖直分割线 | `Secondary` |
| 面板底板、确认框底板、版本浮层底板 | 不加（保持深色，保证文字可读性） |
| 版本浮层里的社交图标、头像 | 不加（`Image` 自己带图，染色会盖掉图标本身的颜色） |
| 版本浮层右半边的更新记录滚动区 | 不加（是一块文本 + `Mask`，跟着 YamaPlayer 的滚动条走，和结果列表一致） |

形状来自 `PanelFrame.png`：工具用代码画的 96×96 白色圆角矩形，**9-slice border = 32**。
画布 `CanvasScaler.referencePixelsPerUnit = 100`，且 `Image.pixelsPerUnitMultiplier = 1`，
所以 32 像素圆角就是 32 canvas 单位；48 / 64 高的控件会把 border 夹到高度的一半，
于是正好变成**胶囊**。想改圆润程度：改 `FrameRadius`（顺便把 `PanelFrame.png` 删掉让它重生成）；
想让某个控件不是胶囊，把它单独设回 `Image.Type.Simple` 即可。

prefab 里内嵌的颜色写的是 YamaPlayer 的默认配色
（primary `#F06292`、secondary `rgba(248,187,208,0.12)`），只是为了在编辑器里预览正确；
**实际颜色由打包时按世界的配色方案覆盖**（YamaPlayer 的配色只在 build 时应用，
运行期没有切换配色的入口，所以这就是"动态跟随"的最大范围）。

## 九、和 YamaPlayer 核心的关系

**模块不依赖对核心的任何修改。** 早期版本让面板复用 YamaPlayer 的 `LoopScroll`，
因此必须给核心打补丁（行池按当前视口高度扩容、每行用模板几何重新盖章），
移植到新工程时常常忘了这一步，表现就是"关键词搜索只显示一条"或"结果行整体偏右、行叠行"。

现在模块自带 **`BiliResultList.cs`**（同一套循环滚动逻辑的模块内版本，公开 API 一致：
`LineCount` / `SetUp` / `Indexes` / `OnScroll` / `ScrollToTop`），面板 prefab 里放的是它，
所以：

- 装上模块、跑一次 Generate Prefabs 就能用，**不用动 YamaPlayer**；
- 生成窗口不再做"核心有没有打补丁"的检查（那个检查已经删掉）。

仍然**可选**的一条核心改进：

| 文件 | 改动 | 效果 |
| --- | --- | --- |
| `Editor/Module/ModuleManagerEditor.cs` | `OnEnable()` 里在 `FindYamaPlayerModules()` 之后补一句 `EditorLocalization.ReloadTranslations()` | `EditorLocalization` 的翻译表每会话只建一次，会话中途才拷进来的模块会一直显示 `module.xxx.name` 这种原始 key；补上之后重开 Inspector 立即正常 |

面板侧还有一条配合：结果列表会**等它的标签页显示一帧之后才激活**，然后才 `SetUp`，
让列表在视口有高度之后才建立行池（`BilibiliSearchUI.ShowTab` / `ActivateResults`）。
YamaPlayer 自己的 `Modal.cs` 也是用"延迟等布局"这个套路。