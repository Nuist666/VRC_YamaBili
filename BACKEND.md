# 后端要求（Backend requirements）

模块自己**不抓 B 站**：它把「搜索」和「播放」两件事交给一个 **你自己部署的 HTTP 服务**。
本文件规定这个服务必须满足的接口，照着实现就能和模块对接。

> 本文只描述**接口契约**，不含任何抓取实现，也不指向任何现成服务。
> 具体怎么从 B 站拿到数据、如何合法合规地使用，由部署者自行决定并承担责任。

---

## 0. 为什么必须是自建后端

| VRChat / Udon 的限制 | 后果 |
| --- | --- |
| 脚本不能用字符串构造 `VRCUrl` | URL 只能由玩家在 URL 输入框里补全 → 需要预填的固定地址 |
| `VRCStringDownloader` 只能发 **GET**，不能带自定义请求头、Cookie、Body | 需要 B 站签名 / Referer / UA / Cookie 的那一步必须放在服务端 |
| 客户端直连 | 域名必须公网可达、`https` 且证书有效 |
| 播放器（AVPro Video）只吃媒体流 | 播放接口必须最终给出可播放的视频流，不能是网页 |

所以：**客户端 → 你的后端 → B 站**。后端是唯一的「重型」部分。

---

## 1. 部署硬性要求

| 项 | 要求 |
| --- | --- |
| 协议 | `https://`，证书由公共 CA 签发（自签会被 VRChat 拒绝） |
| 方法 | 只用到 **GET**，匿名访问，不能要求登录 / Cookie / 自定义头 |
| 请求头 | 客户端只会带 VRChat 自己的 UA，**不会**带 `Referer`、`Origin`、`Authorization` |
| 端口 | 443（URL 里不要带端口，除非确实需要且公网可达） |
| CORS | 不需要（不是浏览器发的请求） |
| 响应编码 | **UTF-8**（模块用 UTF-8 解码 JSON 与关键词） |
| 响应体积 | 建议 **< 32 KB**：只回当前页需要的字段。VRChat 的字符串下载有体积上限，Udon 解析也在主线程上 |
| 延迟 | 建议 < 3 s；模块自己的请求节流是 **5.1 秒一次搜索** |
| 稳定性 | 建议加缓存与限流；不要每个请求都直连 B 站 |

路径没有强制要求：模块只关心「基地址」这一件事。文档与示例里统一用
`https://<你的域名>/player/` 作为基地址（`Base URL`）。

---

## 2. 接口一：搜索

```
GET {Base URL}?page={页码}&keyword={关键词}
```

| 参数 | 说明 |
| --- | --- |
| `page` | 页码，从 1 开始，模块限制在 **1..9999** |
| `keyword` | 搜索关键词。UTF-8；客户端可能把它百分号编码，后端按 UTF-8 解码即可 |

示例：

```
GET https://bili.example.com/player/?page=1&keyword=音楽
```

### 响应

UTF-8 JSON。**两种包装都支持**：

```json
[ { … }, { … } ]
```
```json
{ "data":   [ { … }, { … } ] }     // "result" 或 "list" 键同样接受
```

每条结果的字段（键名**大小写敏感**）：

| 键 | 类型 | 用途 | 备注 |
| --- | --- | --- | --- |
| `id` | string | **BV 号**，用来拼 `https://www.bilibili.com/video/{id}` | 必须是 **12 位、以 `BV` 开头、后面全是字母数字**；**不满足的条目会被直接丢弃**（`av` 号视频因此不显示） |
| `title` | string | 结果标题 | 可空 |
| `channelTitle` | string | UP 主 | 可空 |
| `description` | string | 简介 | 可空，太长会被面板截断 |
| `image` | string | 封面 URL | 模块**不显示封面**（不下载图片），可以省略，但保留它对以后兼容更好 |

- 每页最多显示 **20** 条（模块 prefab 上 `BilibiliSearchService._maxResults` 可改，1..50）；
  多出来的会被忽略，所以后端按 20 条左右返回即可。
- 数组顺序就是显示顺序。
- 非字符串类型的值一律当空字符串处理。

示例响应：

```json
[
  {
    "id": "BV1xx411c7mD",
    "title": "【测试】示例视频标题",
    "channelTitle": "示例UP主",
    "description": "示例简介",
    "image": "https://i0.hdslb.com/bfs/archive/xxxx.jpg"
  }
]
```

### 失败时

- **不要**返回 HTML 错误页（会被当成「不是 JSON 数组」）。
- 空结果直接返回 `[]` 即可，面板会显示「没有搜索到视频」。
- HTTP 4xx/5xx 会让面板显示下载失败；可以对失败请求做短时缓存避免连续打后端。

---

## 3. 接口二：播放

```
GET {Base URL}?url={B 站视频页地址}
```

| 参数 | 说明 |
| --- | --- |
| `url` | B 站视频地址**或** BV 号。由模块**直接拼接**，`url=` 后面就是原样的内容（**没有做百分号编码**）。客户端会校验这一项：只要里面能找到一个 12 位 BV 号就算合法，所以下面这些都能进来 —— 建议后端**两种都接受** |

示例（模块实际发出的形式）：

```
GET https://bili.example.com/player/?url=https://www.bilibili.com/video/BV1xx411c7mD
GET https://bili.example.com/player/?url=BV1xx411c7mD
GET https://bili.example.com/player/?url=https://www.bilibili.com/video/BV1xx411c7mD?p=2&t=30
```

> 这是玩家在面板「网址输入」标签页里补全的那条 URL（面板会预填 `{base}?url=`，玩家只补后半段）。
> 标准的 query 解析（取 `url` 参数的值）就能拿到它。
> 面板**不做客户端校验**：只要输入栏不是空的，里面的 URL 就原样发给你，所以后端要自己判断
> `url` 参数是 **裸 BV 号**（`BV1xx411c7mD`）还是**完整视频页链接**（可能带 `/XXX`、`?p=2`、`#reply`），
> 两种都要能解析（最简单的做法：先从字符串里抽出 BV 号，再走同一套解析）。
> 面板不会做百分号编码，但也请容忍被编码过的形式。

### 响应

必须让 AVPro Video **直接播起来**，两种做法都行：

| 做法 | 说明 |
| --- | --- |
| **302 / 307 跳转**到真实媒体直链（推荐） | 最简单，CDN 流量不经过你的服务器 |
| 自己代理媒体流 | 直链有 Referer/UA 防盗链、或需要改 m3u8 时用 |

约束：

- **不能返回 HTML 播放页**（`Content-Type: text/html` 会被播放器当媒体流打开，直接失败）。
- 建议 `Content-Type: video/mp4`（或 `application/vnd.apple.mpegurl`）；直链场景下由 CDN 给出即可。
- 需支持 HTTP **Range** 请求（拖动进度条要用），否则只能顺序播放。
- 建议把清晰度限制在 **1080p 及以下**：VRChat 客户端解码能力有限，4K 会卡或直接失败。
- 音视频最好是 AVPro 能解的封装（H.264/AAC 的 mp4、或 HLS m3u8）。

---

## 4. URL 约定

- 模块**不校验域名**：解析只看 query 里的 `page` / `keyword`。所以任何域名都能用，
  换域名也不用改代码，重新 Generate 一次 prefab 即可。
- 搜索 URL 必须**恰好**有 `page` 和 `keyword` 两个参数（多一个参数就会被判为非法 URL，
  面板会提示「请输入完整的搜索 URL」）。这是为了避免玩家误把随便一个地址贴进搜索框。
- 播放 URL 不需要模块解析，它只是交给播放器的字符串。

---

## 5. 实现提示（经验，不是契约）

- **搜索**：B 站搜索接口需要 `User-Agent`、可能还需要 WBI 签名 / Cookie（`buvid3` 等），
  直接透传客户端的 UA 经常吃 `412`。建议在服务端固定一套可用凭据并做结果缓存
  （同一 `page+keyword` 缓存几十秒到几分钟）。
- **取流**：B 站视频流有 Referer 防盗链，通常需要带 `Referer: https://www.bilibili.com`
  和正常 UA；直链有时效（几十分钟），不要长期缓存，或每次跳转都重新解析。
- **清晰度**：优先挑 H.264（avc）而不是 HEVC/AV1，兼容性最好。
- **并发与限流**：世界可能有多个玩家同时搜；建议按 IP 与全局两个维度限流，
  并对上游做连接池/退避重试，避免把 B 站打挂、也避免自己的 IP 被封。
- **日志与隐私**：不要记录玩家的 VRChat 身份信息；如果记录 IP，请在文档里说明。
- **合规提醒**：B 站没有对外开放的官方视频 API，相关接口属于非公开接口。
  自建仅供个人学习/自用，请自行评估服务条款、版权与分发风险；
  **不要把带 Cookie 的服务公开共享**。

---

## 6. 自测清单

部署完先用 `curl` 确认，再接进 VRChat：

```bash
# 1. 搜索：应返回 JSON 数组，且 id 是 12 位 BV 号
curl -s "https://bili.example.com/player/?page=1&keyword=music" | head -c 400

# 2. 应能被 json 解析，且至少有 1 条
curl -s "https://bili.example.com/player/?page=1&keyword=music" | python -m json.tool | head -20

# 3. 播放：应看到 302 到 CDN，或直接是 video/* 流
curl -sI "https://bili.example.com/player/?url=https://www.bilibili.com/video/BV1xx411c7mD"

# 4. 证书：必须是公共 CA 签发
curl -svo /dev/null "https://bili.example.com/player/?page=1&keyword=test" 2>&1 | grep -i "SSL certificate"
```

| 检查项 | 期望 |
| --- | --- |
| 搜索响应 | `Content-Type` 不强制，但 body 必须是 UTF-8 JSON 数组（或 `data`/`result`/`list` 包装） |
| `id` | 12 位、`BV` 开头；不是的话该条会被面板丢弃 |
| 播放响应 | `302`/`307` 到媒体直链，或 `200` + `video/*`；**不能是 HTML** |
| Range | `curl -r 0-1023` 应返回 `206 Partial Content` |
| 证书 | 公共 CA，无自签、无过期 |
| 鉴权 | 不能要求 Cookie / Token / 自定义头 |

最后在 VRChat 里确认客户端设置 **Allow Untrusted URLs** 已打开，否则请求仍然会被拒。
