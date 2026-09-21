# 安装与配置（BiliBili Search for YamaPlayer）

在 YamaPlayer 的屏幕上加一个 B 站视频搜索面板：搜关键词 → 从列表里挑一条 →
**复制链接 / 播放 / 加入待播队列**，支持翻页。

> **这个模块不带后端。**
> 搜索和播放都请求一个**你自己部署的** bilibili 播放服务；仓库里没有任何服务器地址，
> 地址由你在下面第 3 步自己填。服务端要实现什么，见 [BACKEND.md](BACKEND.md)。
>
> 想了解内部实现 / 二次开发 → [DEVELOPMENT.md](DEVELOPMENT.md)

---

## 1. 前置条件

| 项 | 要求 | 说明 |
| --- | --- | --- |
| Unity | **2022.3.22f1** | 开发与验证环境 |
| VRChat SDK | Worlds **3.8.1 或更高** | 开发环境是 3.10.5 |
| UdonSharp | 随 VRChat SDK 附带 | 不用单独装 |
| YamaPlayer | **v2.0.0 及以上** | 开发时使用 **v2.0.0-beta.7**；模块用的是它的 `YamaPlayerModuleDefinition` / `ModuleUISlot` / `ColorDefinition` 扩展点 |
| 后端服务 | 一个自建的 bilibili 播放服务 | **必须**，见 [BACKEND.md](BACKEND.md) |

模块本体不需要第三方 C# 包，也不需要额外导入 UdonSharp 的 whitelist。

---

## 2. 把模块放进工程

> **本仓库只有模块本身**（`Modules/BilibiliSearch/` 及里面的内容），**不含 YamaPlayer 本体**。
> 先自备 YamaPlayer（**v2.0.0 及以上**，开发与验证用的是 **v2.0.0-beta.7**），
> 再把模块放进去。

1. 从仓库或发布页的 `BilibiliSearch-v1.0.0.zip` 拿到 **`BilibiliSearch/`** 这个文件夹，
   整个拷到已有 YamaPlayer 包的 `Modules/` 下（和 `Modules/PitchShifter` 同级）：

   ```
   <你的工程>/
   └─ Packages/
      └─ net.kwxxw.yama-stream/          ← YamaPlayer 本体（自备）
         └─ Modules/
            ├─ PitchShifter/
            └─ BilibiliSearch/           ← 本模块
   ```

   **`.cs` / `.asset` / `.png` / `.meta` 都要拷**，缺 `.meta` 会丢 GUID。

2. **不需要改 YamaPlayer 核心**：模块自带结果列表（`BilibiliResultList.cs`），
   不依赖 YamaPlayer `LoopScroll` 的修复，装上重新 Generate 一次就能正常显示多行。
   （早期版本需要改核心，现在已经移除这个依赖。）

   下面这条是**可选**的顺手改进，不打也不影响功能：

   | 文件 | 改动 | 效果 |
   | --- | --- | --- |
   | `Editor/Module/ModuleManagerEditor.cs` | `OnEnable()` 里补一句 `EditorLocalization.ReloadTranslations()` | 刚装好的模块在 ModuleManager 里会立刻显示成「哔哩哔哩搜索」；不打的话第一次会显示 `module.bilibilisearch.name` 这种原始 key，重开 Inspector / 重载程序集后就正常 |

   具体差异见 [DEVELOPMENT.md](DEVELOPMENT.md) 第九节。

3. 打开工程，等 Unity 编译完，Console 不能有红色报错。

---

## 3. 配置你自己的后端地址（必做）

菜单 **Tools → YamaPlayer → Bilibili Search Setup**（打开配置窗口；`Generate Prefabs` / `Uninstall from Scene` 在旁边的 **Bilibili Search** 子菜单里）

```
Backend
  Base URL: https://bili.example.com/player/
```

- 填**你自己部署的**服务地址，**以 `/player/` 结尾**（末尾斜杠会自动补）。
  填 `http://` 或 `https://` 开头的完整地址；填了 query（`?…`）会被自动去掉。
- **没填之前 `Generate Prefabs` 按钮是灰的** —— 这是故意的：仓库不发布任何服务器地址。
- 地址存在 `EditorPrefs`（键 `Yamadev.YamaStream.BilibiliSearch.BackendBaseUrl`），
  换机器、换工程要重新填一次。

点 **Generate Prefabs**，工具会用这个地址预填三处：

| 位置 | 生成的内容 |
| --- | --- |
| 面板搜索框（`_defaultSearchUrl`） | `{Base URL}?page=1&keyword=` |
| YamaPlayer 的 URL 输入框（`_defaultPlayUrl`） | `{Base URL}?url=` |
| 模块 `Service` 组件的 `_baseUrl` | `{Base URL}` |

### 3.1 面板的标签页：网址输入（默认）与关键词搜索

面板顶栏里有两个标签按钮（在 `v1.0.0` 左边），点它们切换；面板没有标题栏，那一行就是全部顶栏：

| 标签页 | 内容 |
| --- | --- |
| **网址输入**（默认，打开面板就是它） | 输入栏（预填 `{Base URL}?url=`）+ **播放** + **关闭** + 下方两段说明 |
| **关键词搜索** | 搜索框（预填 `{Base URL}?page=1&keyword=`）+ 结果列表 + 上一页/下一页/关闭 |

顶栏从左到右是 `[网址输入] [关键词搜索] [v1.0.0] 状态文字 … 页码`。

网址输入页的行为：

- **点击输入栏就会清除并写回默认前缀**（`PointerDown` → `BilibiliSearchUI.ResetUrlInput()`），
  所以玩家只需要在 `url=` 后面粘贴 B 站视频链接，再点「播放」；
- 切到这个标签页时也会自动写一次前缀（输入栏第一次被激活时 `VRCUrlInputField.Awake` 会清空文本）；
- 「播放」按当前播放状态处理：没在播就直接播并关掉面板，已经在播就加入待播队列并提示；
- **输入栏里是什么就发什么**：除空输入外，面板**不做任何校验**，直接把输入栏里的 URL 交给后端。
  后端同时支持 **BV 号**和**完整 B 站视频链接**（见 [BACKEND.md](BACKEND.md)），所以这两种都能播：
  1. `…?url=BV1xx411c7mD`（裸 BV 号）；
  2. `…?url=https://www.bilibili.com/video/BV1xx411c7mD`（完整链接，带 `/XXX`、`?p=2`、`#reply` 都行）。
- **只有输入栏是空的时候才会拦下**，提示「请先在 url= 后面粘贴 B 站视频链接或 BV 号。」。
- 播放失败（后端不可达、前缀被删等）时提示
  「播放失败：请确认 url= 后面是 B 站视频链接或 BV 号，且保留了解析前缀。」。
- **下面有两组说明**，每组是「标题 + 换行 + 正文」，标题用按钮同款颜色、字号比正文略大：
  - `网址输入标签：` —— 讲这一页（保留前缀、粘贴链接、队列不自动关闭）；
  - `关键词搜索标签：` —— 讲搜索页（在 `keyword=` 后面补关键词、结果可以复制/播放/加队列）。
  面板默认就停在这一页，所以两组都放在这里；输入栏与第一组之间、两组之间各空一行。
- **播放按钮右边的「关闭」**用来回到 YamaPlayer 主界面。需要它是因为：
  当前已经有视频在播时，这条只会**加入待播队列**，面板**不会**自动关闭（这是故意的，
  免得抢掉正在看的视频），这时用「关闭」退出即可。
- **其他站点（YouTube 等）不走这里**，用 YamaPlayer 自己的「输入 URL」播放。

> **本模块完全不动 YamaPlayer 自己的 URL 输入框**（主页面左列那个「输入 URL」图标、顶栏那个、
> 以及 Canvas 上那个 URL 页面）。之前几版尝试过在它们上面挂点击事件来补前缀，已全部撤销；
> 现在那三个框是 YamaPlayer 原样逻辑，留给其他站点用。
> 如果你之前跑过带那套挂载的版本，**生成/修复时会自动把残留的事件摘掉**
> （`RemoveLegacyUrlBoxWiring()`，会打印 `Removed N url box handler(s)...`）。
生成物（都在模块目录里）：

```
Modules/BilibiliSearch/
├─ BilibiliSearch.prefab          ← 模块 prefab
├─ BilibiliSearchPanel.prefab     ← 面板 prefab
├─ BilibiliIcon.png               ← 图标列用的 B 站图标
└─ PanelFrame.png                 ← 圆角胶囊用的 9-slice 白图
```

> **运行时代码里没有域名**：搜索框里的 URL 是唯一的请求来源，解析器只看
> `page=` / `keyword=` 两个参数，跟主机名无关。所以以后换域名只要重新 Generate
> 一次（或者直接在场景里改 prefab 上那三个字段），不用改代码。

---

## 4. 在场景里放一个模块实例

1. 打开你的 YamaPlayer 场景（用 `YamaPlayer.prefab` 那个，不是 NoScreen）。
2. 选中 `Controller/Modules` 上的 **`ModuleManager`**，
   在可用模块列表里找到 **哔哩哔哩搜索**，点 **Add**。
3. 一个场景只放**一个**实例（模块定义里 `allowMultiple = false`）。
4. 模块通过两个 `ModuleUISlot` 注入 UI，不需要你手动摆放：
   - `Canvas/User/Main` ← 面板本体（盖在主页面之上，默认隐藏）
   - `Canvas/User/Main/LeftSide/Container`（`siblingIndex = 0`）← B 站图标按钮，
     位置在主页左侧图标列最上面、「输入 URL」图标**上方**

> 如果模块名显示成 `module.bilibilisearch.name`，重开一下 Inspector 或重新点
> ModuleManager 就会刷新（编辑器翻译表是每会话缓存的）。

### 4.1 卸载（安全移除）

**可以安全卸载**，但顺序不能反 —— 模块的 prefab 就在模块目录里，
场景里还留着模块实例时直接删目录，场景会丢引用（Missing Prefab）。

1. **先从场景移除模块实例**，两种方式都行：
   - 选中 YamaPlayer 的 `ModuleManager`，在 **已安装模块** 列表里点该模块的 **Delete**（YamaPlayer 自带）；
   - 或者用菜单 **Tools → YamaPlayer → Bilibili Search → Uninstall from Scene**：
     它会把**当前场景里所有** BilibiliSearch 模块实例（含隐藏的）连面板一起删掉
     （支持 Undo），顺手清理旧版本残留在 YamaPlayer URL 框上的点击事件，并标记场景已修改。
     —— 不需要指定是哪个 YamaPlayer，场景里所有实例都会被移除。
2. **保存场景**。
3. **删掉 `Modules/BilibiliSearch/` 整个目录**：脚本、`.asset`（UdonSharp program asset）、
   本地化、图标、以及生成物（`BilibiliSearch.prefab`、`BilibiliSearchPanel.prefab`、
   `BilibiliIcon.png`、`PanelFrame.png`）都在里面，一起删干净，不会有残留。
4. **两处 YamaPlayer 核心修复可以保留**：它们是独立的 bug 修复，不依赖本模块，
   留着不会报错；想完全回到原版就把那两个文件换回上游版本。
5. 生成工具存在 `EditorPrefs` 里的后端地址（`Yamadev.YamaStream.BilibiliSearch.BackendBaseUrl`）
   是编辑器本地设置，留着无副作用；想清掉删注册表
   `HKCU\Software\Unity Technologies\Unity Editor 5.x` 下的同名键即可。
6. **已经上传过的世界不会自动变**：模块已经烘进那份世界资源里了，
   要让模块消失必须重新上传一次。

> 卸载不会动其它模块，也不会动 YamaPlayer 本体；反过来，删掉模块目录后
> ModuleManager 的模块列表里也不会再出现它（它靠扫描 prefab 发现模块）。

---

## 5. 上传前：VRChat 侧的设置

| 设置 | 位置 | 说明 |
| --- | --- | --- |
| **Allow Untrusted URLs** | VRChat 客户端设置 | **必须打开**。自建域名不在 VRChat 的信任列表里，不开的话搜索请求和视频播放都会失败（YamaPlayer 会显示 `Access Denied`） |
| HTTPS | 你的后端 | VRChat 只接受有效证书的 `https`，自签证书会被拒绝 |

在 Editor 里 Play 测试需要 **ClientSim**，否则 `Networking.LocalPlayer` 为 null，
YamaPlayer 本地化会抛异常、面板文字失效。装法：

```json
// Packages/vpm-manifest.json
"com.vrchat.clientsim": { "version": "1.2.4" }
```

（`locked` 段也要加一条），或用 `VRChat SDK → ClientSim → Enable ClientSim`。

---

## 6. 用法

1. 进世界后，点主页面左侧图标列最上面的 **B 站图标** 展开面板。顶栏是 [网址输入] [关键词搜索] [v1.0.0] 状态 … 页码 一行，点前两个按钮切标签页，**默认停在「网址输入」**。

**网址输入页（默认）**

2. 输入栏里已经有 `{Base URL}?url=`，直接**在 `url=` 后面粘贴 B 站视频链接**，点 **播放**。
   输入栏点一下就会把前缀写回来（粘贴前不用手动清空）。
   - 没在播 → 直接播放，然后面板自动关闭回到主界面。
   - 已经在播 → 加入待播队列并提示，**面板不会自动关闭**，点播放右边的 **关闭** 回主界面。
   - 输入栏下方有两段说明：一段讲这一页，一段讲关键词搜索页。
   - 其他站点（YouTube 等）请用 YamaPlayer 自己的「输入 URL」，本模块不碰它。

**关键词搜索页**

3. 点搜索框 → 弹出 VRChat 键盘 → 在 `keyword=` 后面补关键词 → 点 **搜索**。
   - 搜索框每次点击都会重置成默认前缀，所以不会残留上一次的 URL。
4. 列表里每条结果是「标题 / UP主 + BV号 / 简介」，按钮：
   - **复制链接** → 弹窗里复制 `https://www.bilibili.com/video/BV…`
   - **播放** → 没在播就直接播；**已经在播就不打断，改成加入待播队列**
   - **加入队列** → 加进 YamaPlayer 的待播队列，加完该按钮禁用 10 秒
5. 翻页用 **上一页 / 下一页**（会弹出确认框，需要你把新 URL 粘进 URL 框并确认 —— 
   这是 VRChat 的限制：脚本不能自己构造 URL）。
   右端的 **关闭** 同样回到主界面。

**其他**

6. 面板顶栏里的 **`v1.0.0`** 按钮打开版本浮层，左上角 `← 返回` 回到搜索面板。

> 搜索结果里的「播放 / 加入队列」第一次点会要求你粘贴一次播放链接（因为脚本不能构造 URL）；
> 而**网址输入页不需要这一步**：那个框就是玩家自己输入的。

---

## 7. 排错

| 现象 | 原因 / 处理 |
| --- | --- |
| 搜索结果一直失败、Console 里 `Access Denied` | VRChat 没开 **Allow Untrusted URLs**，或后端证书无效 |
| 列表里空空如也，状态栏写「该结果没有有效 BV 号」 | 后端返回的条目 `id` 不是 12 位 BV 号；见 [BACKEND.md](BACKEND.md) 的字段表 |
| 点搜索没反应 | 搜索框内容不是合法请求 URL（必须同时有 `page=` 和 `keyword=`，且关键词非空） |
| 面板上的按钮点了没用 / 列表底部空一块 / 结果行偏右、行叠行 | 用的是旧版本模块（依赖 YamaPlayer `LoopScroll` 的修复）。当前版本自带结果列表，重新 Generate 一次即可 |
| **结果行整体偏右 / 行与行叠在一起** | 同样是那个项目的 `LoopScroll.cs` 没打补丁（原版 `UpdatePosition()` 只改 y、保留每个复用行自带的横向位置）。生成窗口现在会直接弹红框提示：`This YamaPlayer does not have the LoopScroll fix the module needs`；按 DEVELOPMENT 第九节把改动合进去即可 |
| 模块列表里显示 `module.bilibilisearch.name` | 重开 Inspector / 重新点 ModuleManager；仍不行说明 `Localization.Editor.json` 没随模块拷过去 |
| `Generate Prefabs` 按钮是灰的 | 还没填 Base URL，见第 3 步 |
| **菜单里找不到 Bilibili Search Setup** | 路径是 `Tools → YamaPlayer → Bilibili Search Setup`（和 `Repair Bilibili Search Scene` 同一层）。若整个 YamaPlayer 子菜单都没有，说明模块的 Editor 程序集没编译过：看 Console 有没有 `error CS`，并确认 `.cs` / `.asmdef` / `.meta` 都拷全了 |
| 换域名后还是请求旧地址 | 重新跑一次 Generate Prefabs（搜索框与网址输入页的预填值都来自那个地址） |
| 网址输入页点「播放」没反应 | 输入栏里必须保留 `{Base URL}?url=` 前缀，只在后面粘 B 站链接；缺前缀会提示"请保留解析前缀" |
| Console 出现 `Removed N url box handler(s)...` | 正常：这是在清理旧版本残留在 YamaPlayer URL 框上的点击事件 |

详细的现象、成因、修法（开发视角）在 [DEVELOPMENT.md](DEVELOPMENT.md) 第七节。

---

## 8. 目录结构

```
Modules/BilibiliSearch/
├─ BilibiliSearch.cs                  模块入口（YamaPlayerModule）
├─ BilibiliSearchService.cs           下载 JSON、解析结果
├─ BilibiliSearchResult.cs            一页结果的纯数据容器
├─ BilibiliSearchUI.cs                面板显示 / 滚动 / 操作按钮
├─ BilibiliSearchResultAction.cs      单元格按钮 → 面板的转发器
├─ BiliUrlUtility.cs                  请求 URL 的解析（与域名无关）
├─ BiliText.cs                        面板文案的内置兜底表
├─ Localization.Runtime.json          面板文案（9 种语言）
├─ Author.png                         版本浮层里的作者头像
├─ DeepSeekIcon.png / CodexIcon.png   版本浮层里的两个 AI 署名图标
├─ *.asset                            UdonSharp program asset（Unity 自动生成）
├─ BilibiliSearch.prefab              ★ 生成物：模块 prefab
├─ BilibiliSearchPanel.prefab         ★ 生成物：面板 prefab
├─ BilibiliIcon.png / PanelFrame.png  ★ 生成物：图标与圆角图
└─ Editor/
   ├─ BilibiliSearchPanelSetup.cs     生成 prefab 的工具（也就是第 3 步那个窗口）
   ├─ BilibiliSearchRepair.cs         场景里模块的修复 / 自检工具
   ├─ Localization.Editor.json        模块名 / 描述
   └─ *.asmdef                        程序集定义
```

★ 的三个是**生成物**：仓库里不提供（也不提供任何服务器地址），第一次用必须先跑
第 3 步生成；换域名、换配色、改布局之后重新生成一次即可。

---

## 9. 许可与致谢

- 本模块自身采用 **MIT License**（见 [LICENSE.md](LICENSE.md)），例外部分与第三方署名见
  [NOTICE.md](NOTICE.md)。
- 基于 [YamaPlayer](https://github.com/koorimizuw/YamaPlayer)（作者 **kwxxw / Yamadev**）开发。
  上游条款（其 README 的「利用規約」）允许改変、允许作为世界的一部分公开、
  允许用于有偿/无偿的世界资产；**署名在免费发布时可选，打包进售卖的世界资产时必须署名**。
- `BilibiliResultList.cs` 改编自 YamaPlayer 的 `LoopScroll.cs`；社交图标复用 YamaPlayer 自带的
  `vrchat.png` / `twitter.png` / `github.png`；DeepSeek / Codex 图标来自各自官方标识，
  仅用于标注 AI 辅助开发。
- 后端不在本仓库内，B 站数据的使用请自行评估并遵守相关服务条款。
