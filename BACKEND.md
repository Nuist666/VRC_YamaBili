# 后端要求

模块不包含后端。作者配置自己的 HTTPS 播放服务，初次搜索由玩家填写 URL，后续翻页、播放及入队使用编辑器预置的记录 URL。安装和编号池设置见 [INSTALL.md](INSTALL.md)、[DIRECT_ACTIONS.md](DIRECT_ACTIONS.md)。

## 请求与环境

- Base URL 示例：`https://bili.example.com/player/`，不带 query 或 fragment，必须使用有效 HTTPS 证书。
- Udon 字符串下载使用 GET，不能自定义 UA、Cookie、Authorization 或请求体。服务必须兼容目标 Unity / VRChat 版本的请求头。
- 本次调查的后端会根据 UA 区分响应：UnityWebRequest 获得 JSON，媒体解析链路获得媒体重定向。普通浏览器或 curl 返回 403 不足以认定 Unity 请求也失败。
- 字符串下载和视频播放是不同链路；VRChat 客户端需要允许对应域名，非信任域名需启用 Allow Untrusted URLs。
- 模块搜索请求间隔至少 5.1 秒；服务应控制响应大小、缓存及超时。

## 初次搜索

可在 Unity Editor 使用 **Tools → YamaPlayer → Bilibili Search → SRID Test Tool** 输入关键词与页码检查响应。工具显示 HTTP 状态、格式化 JSON、返回 SRID 范围与配置池覆盖；操作和预测限制见 [DIRECT_ACTIONS.md](DIRECT_ACTIONS.md)。测试请求使用 UnityWebRequest，不能替代下文的媒体链路验证。

```text
GET {Base URL}?page=1&keyword=关键词
```

`page` 为 1..9999，`keyword` 非空。输入地址必须匹配配置的 Base URL，参数只允许 page 与 keyword。关键词按 UTF-8 解码。

响应支持顶层数组，或 `data` / `result` / `list` 包装数组：

```json
[
  {
    "id": "BV1xx411c7mD",
    "title": "示例标题",
    "channelTitle": "示例UP主",
    "description": "示例简介",
    "recordsid": "550577"
  }
]
```

| 字段 | 类型与用途 |
| --- | --- |
| `id` | 12 位 BV 号，BV 后为字母数字；无效条目不显示 |
| `title` / `channelTitle` / `description` | 字符串；缺失或非字符串按空文本处理 |
| `recordsid` | 正整数字符串或 JSON 整数，不超过 2147483647；对应后端记录 |
| `image` / `mid` | 当前不使用 |

每页默认最多显示 20 条，`_maxResults` 可设置 1..50。`recordsid` 不是 BV 的固定映射：同一视频再次解析可分配新的记录。记录一旦分配，在播放或排队使用期间必须保持可解析且不得改指向其他视频。

空结果返回 `[]`。下载或 JSON 解析失败会保留客户端旧页；不要用 HTTP 200 + HTML 错误页伪装有效搜索响应。

## 分页记录

本模块采用本次实测的 BiliPlayer 记录约定（版本号见 [CHANGELOG.md](CHANGELOG.md)）：设**完整原始响应**最后一条记录编号为 L，则：

| 动作 | 请求 |
| --- | --- |
| 上一页 | `GET {Base URL}?srid=L+1` |
| 下一页 | `GET {Base URL}?srid=L+2` |

这些记录应在服务端绑定对应关键词及目标页，响应仍为上述搜索 JSON。客户端要求原始数组的所有记录编号连续有效后才推算分页，在过滤无效 BV 与截断显示条数之前计算 L。

例如原始结果为 549866..549885，下一页请求为 `?srid=549887`。后续页应根据**新响应**重新推算，不能把请求编号机械地加 22；其他用户请求可能使全局编号跳跃。

这不是通用的 B 站 API 规则。自建后端必须实现相同的记录分配约定；只返回 `recordsid` 并不足够。客户端的连续性检查不能证明分页记录没有被并发请求抢占，服务端必须保证当前结果及其分页记录的正确关联。空页不产生可推算记录，客户端保留刚才成功页的原始请求用于返回。

## 视频记录与媒体链路

```text
GET {Base URL}?srid=550577
```

当编号对应结果中的视频记录时，这就是该视频的播放解析入口。模块把预置的完整 `VRCUrl` 交给 YamaPlayer 的 AVPro 播放 / 队列管线，不从 JSON 拼出临时 CDN 地址。

本次抓包观察到的链路：yt-dlp 请求 srid → 后端 302 → Bili CDN MP4；VRChat 媒体请求 CDN 并使用 Range；UnityWebRequest 请求同一 srid 则返回 JSON。服务端应兼容实际媒体解析器和 Unity 请求，不能假定所有 UA 都返回相同内容。

后端最终需要提供播放器可用的媒体重定向或媒体流，支持 Range；若 Unity 请求该记录返回元数据，不能因此把 JSON 当成视频交给播放器。入队保存的是 srid 解析入口，避免长期保存会过期的 CDN 签名 URL。

## 网址输入标签页

```text
GET {Base URL}?url=BV1xx411c7mD
GET {Base URL}?url=https://www.bilibili.com/video/BV1xx411c7mD
```

该页仍由玩家补全输入，模块只拦截空输入，将用户提供的 VRCUrl 交给播放器。后端负责校验、解析 BV 或完整视频页链接。输入框预填 `{Base URL}?url=`，玩家应保留前缀。不要依赖客户端替服务端做参数验证。

## 对接验证

1. 使用目标 Unity 版本的 UnityWebRequest 获取搜索结果，检查 BV、recordsid、原始编号连续性及分页记录。
2. 点击上一页、下一页，比较实际返回视频与相同关键词的 page=N 响应。检查第一页、末页、空页及并发搜索。
3. 验证编号位于编辑器生成的池内；默认 500000..542002，超出范围必须重新配置、烘焙及上传。
4. 用真实媒体链路 GET 视频 srid，检查重定向、最终媒体和 Range；HEAD 或通用 curl UA 不能代替这一步。
5. 在 VRChat 检查第一次点击即播放、第一次点击即入队、播放中点击播放改为入队、权限和多人同步。

后端上游凭据与取流逻辑由服务端负责，不应把 Cookie 或私有凭据分发到世界资源中。
