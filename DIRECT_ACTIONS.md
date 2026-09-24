# 直接操作与记录 URL 池

## Unity Editor SRID 测试工具

菜单 **Tools → YamaPlayer → Bilibili Search → SRID Test Tool**，无需进入 Play Mode。

**Search results and prediction** 上方常驻 **SRID Range** 显示框：有有效结果时显示 `最小 SRID - 最大 SRID`，无结果、清空或开始新请求时显示 `-`，不需要滚动结果文本查找范围。

1. 测试窗口界面统一为英文。首次打开时，Base URL、First record ID、URL count、Latest observed record ID、Estimated IDs per day、Days to cover 默认读取 **Bilibili Search Setup** 设置。这些字段均支持手动输入，估算和请求使用窗口内的值；修改不会自动写回 Setup。点击 **Reload from Setup** 可重新载入 Setup 当前设置。请求期间输入暂时禁用，修改配置、关键词或页码后旧测试结果会清空。
2. 输入 **Search keyword** 和 **Search page (1-9999)**，点击 **Search and check SRID coverage**。工具显示完整响应中有效 `recordsid` 的最小值和最大值、池内／池外数量、无效记录数量及覆盖判断。仅所有记录有效且均在池内时显示 PASS；分页地址的覆盖情况单独显示。原始编号连续时计算上一页 `L+1`、下一页 `L+2`。支持裸数组及 `data` / `result` / `list` 包装；空数组、非法编号、不连续编号和整数溢出不会启用翻页。
3. 结果中的 **Response-based prediction** 以本次响应的最大编号和手填每日增长值，计算编号池上界的剩余空间、预计剩余天数、覆盖指定天数所需的建议范围和 URL 数量（含 20% 余量及分页）。单次搜索无法测出每日增长，也不能保证响应最大编号就是全局最新编号；出现池外或无效记录时，剩余天数不代表当前结果可用。窗口上方的 Setup-based 估算仍以输入的 Latest observed record ID 为依据。
4. 可继续测试推算出的上一页 / 下一页。窗口显示 HTTP 状态、响应类型和原始响应；结果与预测、Raw response 各有独立的纵向滚动区域，长文本自动换行。有效 JSON 按缩进多行格式显示，非 JSON 保留原文；显示最多 32,000 字符，分析始终使用完整原始响应。请求超时为 20 秒、间隔至少 5.1 秒，可取消，关闭窗口会清理请求。重定向只显示目标，不跟随下载媒体。
5. 有有效记录且测试后端与 Setup 一致时，可点击 **Write highest response ID to Setup (same backend only)**，再回 Setup 使用 **Apply coverage estimate** 和 **Generate Prefabs** 更新编号池。该操作不自动改动池起点、数量或 prefab；测试窗口可用 **Reload from Setup** 读取更新值。

Setup 的最新观测值和每日增长只能估算容量，不能推算某个视频对应的 SRID，也不能证明估算编号已经存在。搜索请求可能分配新记录。测试工具检查的是 UnityWebRequest 响应及 Setup 配置范围，不验证已生成 prefab 的实际池，也不能替代 VRChat / AVPro / yt-dlp 媒体链路测试。

离线边界断言已加入现有 **Run Direct Action Regression Tests** 菜单，覆盖响应包装、连续性、非法编号、空响应、溢出和池边界。

> **本文件是「直接翻页 / 播放 / 入队」行为的唯一出处**：原理、安装与升级、编号池配置、范围限制、后端约定。
> 版本号与更新内容见 [CHANGELOG.md](CHANGELOG.md)；安装全流程见 [INSTALL.md](INSTALL.md)；后端字段契约见 [BACKEND.md](BACKEND.md)。
> 其他文档不要再重复这里的原理与限制，只写各自主题。

搜索框仍由玩家在 `keyword=` 后输入关键词（初次搜索必须由玩家填写，见下）。搜索成功后：

- **上一页 / 下一页**：直接加载对应搜索页，不再弹出复制粘贴确认框。
- **播放**：直接提交该结果的 `?srid=` 地址。已有视频在播放时仍改为入队，并同时检查队列权限。
- **添加到待播队列**：直接入队；保留每条结果 10 秒的防重复冷却。
- **复制链接**：仍打开复制窗口，复制的是 B 站视频网页地址。

## 一、原理解释：为什么需要编号池

Udon 不能在运行时把字符串转成 `VRCUrl`，`VRCUrl` 只能来自序列化字段或 `VRCUrlInputField`。所以：

| 动作 | 地址来源 |
| --- | --- |
| 初次关键词搜索 | 玩家在搜索框里填写的 `{Base URL}?page=N&keyword=…` |
| 上/下页、播放、入队 | 编辑器预先烘出的完整 `{Base URL}?srid=编号` 数组，运行时只按编号查表 |

编辑器工具生成整个地址数组，运行时**只做查表**，绝不拼接、也绝不把地址写进普通文本框。

## 二、编号池配置

在 **Tools → YamaPlayer → Bilibili Search Setup** 窗口里配置，位置就在 **Base URL 下方**的 **Direct Action URLs** 一栏；v1.1.2 起不再有独立的 Direct Action URLs 窗口，烘池与版本文字更新都在 **Generate Prefabs** 里完成。

| 项 | 默认值 | 说明 |
| --- | --- | --- |
| Latest observed record ID | `500000` | 最近从后端响应中观测到的 `recordsid`；用于估算，不会自动联网获取 |
| Estimated IDs per day | `5000` | 预计每天增加的编号数量，须大于 0；可用两次观测编号之差除以间隔天数估算 |
| Days to cover (+20% reserve) | `7` | 每 7 天更新地图；计算时自动增加 20% 增长余量，默认预计覆盖约 8.4 天 |
| Apply coverage estimate | — | 根据上述输入和 First record ID 更新 URL count；此按钮不会生成或上传 |
| First record ID | `500000` | 池内最小编号，须为正整数且不大于最新观测编号；保留较早的起点可继续覆盖旧记录 |
| URL count (max 2,000,000) | `42003` | 新模块默认数量，覆盖 `500000`–`542002`；允许 `1`–`2000000`；**超过 `50000` 会显示资源占用可能过高的警告** |
| Last record ID | 自动计算 | 池内最大编号，等于起始编号 + 数量 − 1，不得超过 `2147483647` |
| Estimated remaining days | 自动计算 | （末编号 − 最新观测编号）÷ 每日增长量；观测编号不在池内或增长量无效时显示警告 |
| Generate Prefabs | — | 按当前起点和数量生成并保存 `Service.RecordUrls`，同时生成面板与模块 prefab |

这五个数值（起点、数量、最新观测编号、每日增长、覆盖天数）保存在 `EditorPrefs`，与 Base URL 同一命名空间，因此不开窗口的菜单项 **Bilibili Search / Generate Prefabs** 也用同一份数值；换机器、换工程需要重新填写。窗口打开时读到的是上次填写的值，**不会**自动读取已有 prefab 里的池配置。

生成时会校验：编号范围有效、生成结果与 `Base URL` 一致、数量与 `RecordUrlCapacity` 相等；保存 prefab 前还会确认起点、数量与 `VRCUrl[]` 确实写入了 Udon，写入失败就中止保存并抛错。不满足的池会被运行期判定为无效。

范围无效（起点非正、数量越界、末编号溢出）时 **Generate Prefabs 按钮置灰**并显示原因；菜单项路径会直接报错返回，不生成半成品。生成流程会先补齐 UdonSharp 程序资产、等待脚本版本升级与编译完成，再构建 prefab，因此更新脚本后不需要再单独烘焙旧 prefab。

已有 prefab 保留原有配置，不会因脚本默认值变化自动扩容或缩容。要换池就在这一栏填写 **Latest observed record ID**、**Estimated IDs per day** 和 **Days to cover**，点击 **Apply coverage estimate**，再点 **Generate Prefabs**。计算保留起始编号，加上预计增长的 20% 余量，并预留分页编号；超出上限会拒绝估算，不会静默截断。

以当前编号和起始编号均为 `500000`、每天增长 `5000` 为例，按 7 天及 20% 余量得到 **42003 条**，覆盖 **500000–542002**，预计约 **8.4 天**，适合每 7 天更新地图。每次更新应重新观测编号并调整起点和估算；后端增长变化可能缩短覆盖时间，不能保证固定天数。

旧大池若要缩减到当前默认值，在 Direct Action URLs 一栏填写起点 `500000`、数量 `42003` 后点 **Generate Prefabs**。仅更新模块脚本不会缩减已有 prefab 的数组。

容量计算公式：`最新观测编号 − 起始编号 + 向上取整(每日增长 × 覆盖天数 × 1.2) + 3`。最后的 `3` 包含当前编号本身和两个分页记录。增长速度只是观测值，应在每次发布前更新；填写 `500000` 不代表后端始终处于这个编号。

推荐操作步骤：

1. 从近期搜索响应读取 `recordsid`，填写 **Latest observed record ID**，并根据观测调整每日增长和覆盖天数。
2. 检查 **First record ID** 是否需要保留旧记录；不要将起点设到仍需使用的记录之上。
3. 点击 **Apply coverage estimate**，核对数量、末编号和预计剩余天数。修改估算输入或起点后，需要再次点击此按钮；也可以直接手动填写数量。
4. 若估算按钮禁用，检查输入是否为有效正数、最新编号是否低于起点，以及所需数量是否超过 200 万或末编号是否溢出。按实际需要缩短覆盖天数或调整起点。
5. 点击 **Generate Prefabs** 生成面板、模块与编号池，检查场景实例覆盖，再完成 Unity/Udon 编译和世界上传。仅修改窗口数值或点击估算不会让已上传世界生效。

窗口里的最新观测编号、每日增长和天数是估算输入，不会写进 Service；随 prefab 保存的只有起点、数量与烘焙出的地址数组。

资源开销参考：默认 42003 条的 UTF-16 地址字符内容约 4.4 MB；v1.1.1 的 210003 条约 22 MB，最早的 120 万条约 126 MB。实测参考：以 210003 条默认池做初次构建时，Unity Editor 占用内存由约 **2931 MB 升至 6219 MB**，所以默认值进一步缩减到 42003 条。Unity 序列化、临时副本和 Undo 的真实开销更大，不能仅按字符大小推算 Editor 内存峰值。已有模块改好范围后重新 **Generate Prefabs** 即可缩容，不需要改布局。

**URL count 超过 `50000` 时会显示黄色警告**（资源占用可能过高），并按 105 字节/条给出「当前约 xx MB，而推荐上限约 5 MB」的对比估算。这是提示而非硬性限制：上限仍是 200 万条，超过推荐值时请确认世界体积、构建时间与客户端内存可接受再烘焙。

## 三、安装与升级

把运行时文件放入 `Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/`。以下编辑器脚本位于仓库 `Editor/`，安装时保留该目录结构：

- `BilibiliSearchPanelSetup.cs`
- `BilibiliSearchRepair.cs`
- `BilibiliSearchDirectSetup.cs`
- `BilibiliSearchDirectTests.cs`

保留它们的 `.meta`。**新生成模块会自动生成 URL 池**，无需额外操作。

升级或换池已有模块时，直接重新生成一次（prefab 覆盖保存、保留 GUID，场景实例沿用新池）：

**Tools → YamaPlayer → Bilibili Search Setup → 填好 Base URL 与 Direct Action URLs 一栏 → Generate Prefabs**

该流程同时把模块版本、版本按钮与版本浮层更新为当前版本（见 [CHANGELOG.md](CHANGELOG.md)），并写入日语更新履历。v1.1.2 之前用独立窗口 **Bake module prefab** 的路径已移除。

> 现有场景的模块 prefab 实例会继承池；若 Service 的相关字段有**实例覆盖**，需更新覆盖，否则实例仍用旧池。重新上传世界后才对玩家生效。
> 世界构建时也会对活动模块的池做检查，必要时按该模块配置重建；该检查**不会**自动改变编号范围。

## 四、必须理解的有限范围

- **不会兜底**：编号超出范围、编号无效、地址池尚未生成或与 Base URL 不一致时，只显示「此结果暂不支持直接操作」提示，不弹手动粘贴框，也不会取模误播。
- **池是有限的**：后端编号继续增长后，地图作者必须更新池并重新上传世界；无法无限期使用同一份池。
- **队列存的是解析入口**：队列保存 `srid` 地址，不保存临时 CDN 签名地址。后端需保证已创建记录在队列使用期间仍可解析，且不能复用为其他视频。
- **初次搜索仍需玩家输入**：脚本无法替玩家构造 `VRCUrl`，关键词搜索框是唯一由玩家提供的请求地址来源。

## 五、后端协议

本版本采用已实测的 BiliPlayer 协议（完整字段表见 [BACKEND.md](BACKEND.md)）：

1. 初次搜索：`?page=N&keyword=…`，返回数组（或 `data` / `result` / `list` 包装数组）。
2. 每条视频应包含 `recordsid`，支持正整数字符串或 JSON 整数。
3. 设**原始完整响应**最后一条记录编号为 L，上一页是 `?srid=L+1`，下一页是 `?srid=L+2`。
4. 该规则要求原始页记录**连续**；不连续或存在无效编号时禁用推算，不猜编号。
5. 视频记录的 `?srid=` 可供 VRChat 媒体解析链路使用；UnityWebRequest 对同一地址可能取得配套 JSON。客户端不从 JSON 构造媒体 URL。

分页在 BV 过滤与显示条数截断**之前**计算，所以 `_maxResults` 不会改变分页编号。分页请求保留逻辑关键词和页码，不再要求 srid URL 含 `page` / `keyword` 参数。

下载或 JSON 解析失败时保留上一页的数据和分页编号。若下一页返回空数组，则显示无结果并保留「返回刚才那页」的原始请求地址。多面板场景会拒绝已过期的结果行，防止点击旧行误播新搜索中的同一位置。

## 六、验证工具

**Tools → YamaPlayer → Bilibili Search → Run Direct Action Regression Tests** 检查原始分页、结果截断、编号边界、坏 JSON 保留旧页、空页、错误地址和旧结果拒绝等逻辑。

这套**编辑器回归测试不代替 Udon 编译和 VRChat 播放验证**；升级后应完成世界编译，并验证权限、视频实际播放以及多人队列同步。
