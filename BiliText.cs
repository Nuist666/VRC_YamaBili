namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// Built in fallback strings for the panel labels.
  /// YamaPlayer merges the module translation files into the UIController only while the world
  /// is built (<c>LocalizationBuildProcess</c>). While the panel is authored in the editor, and
  /// whenever no merged file is present, <c>UIController.GetTranslation</c> returns an empty
  /// string, which made the panel display its raw localization keys. These defaults keep the
  /// panel readable in the prefab, in editor play mode and in game alike.
  /// </summary>
  public static class BiliText
  {
    public static string Get(string key)
    {
      if (key == "module.bilibilisearch.title") return "哔哩哔哩搜索";
      if (key == "module.bilibilisearch.name") return "哔哩哔哩搜索";
      if (key == "module.bilibilisearch.description") return "在 YamaPlayer 中搜索并播放哔哩哔哩视频。";
      if (key == "module.bilibilisearch.placeholder") return "补全关键词，例如 音乐";
      if (key == "module.bilibilisearch.copyField") return "复制完整链接";
      if (key == "module.bilibilisearch.confirmPlaceholder") return "粘贴链接后回车完成输入";
      if (key == "module.bilibilisearch.page") return "页码";
      if (key == "module.bilibilisearch.confirm") return "确认执行";
      if (key == "module.bilibilisearch.cancel") return "关闭";

      // Tabs of the panel: the keyword search and the url page.
      if (key == "module.bilibilisearch.tab.search") return "关键词搜索";
      if (key == "module.bilibilisearch.tab.url") return "网址输入";
      if (key == "module.bilibilisearch.urlPlaceholder") return "在 url= 后面粘贴 B 站视频链接";
      if (key == "module.bilibilisearch.urlPlay") return "播放";
      if (key == "module.bilibilisearch.urlLabel") return "网址输入标签：";
      if (key == "module.bilibilisearch.urlHint")
        return "上面的输入栏已经填好解析前缀，请保留前缀，直接在 url= 后面粘贴 B 站视频链接或 BV 号，再点「播放」。\n"
             + "播放时如果已经有视频在播，这条会加入待播队列，面板不会自动关闭，可以点「关闭」回到主界面。";
      if (key == "module.bilibilisearch.searchLabel") return "关键词搜索标签：";
      if (key == "module.bilibilisearch.searchHint")
        return "在搜索框的 keyword= 后面补上关键词，点「搜索」浏览结果；每条结果都可以复制链接、播放或加入待播队列。\n"
             + "其他站点（YouTube 等）请用 YamaPlayer 自己的「输入 URL」播放。";

      // The version overlay only translates its back button: the account values, the project
      // name, the version string and the changelog heading are the module's own data.
      if (key == "module.bilibilisearch.version.back") return "返回";

      if (key == "module.bilibilisearch.msg.searching") return "正在搜索…";
      if (key == "module.bilibilisearch.msg.noResult") return "没有搜索到视频";
      if (key == "module.bilibilisearch.msg.resultCount") return "个视频";
      if (key == "module.bilibilisearch.msg.failed") return "搜索失败";
      if (key == "module.bilibilisearch.msg.playing") return "已提交播放请求。";
      if (key == "module.bilibilisearch.msg.queued") return "已加入待播队列。";
      if (key == "module.bilibilisearch.msg.queuedWhilePlaying") return "当前有视频正在播放，已加入待播队列。";
      if (key == "module.bilibilisearch.msg.invalidUrl") return "请输入完整的 page=N&keyword=关键词 搜索 URL。";
      if (key == "module.bilibilisearch.msg.invalidBv") return "该结果没有有效 BV 号。";
      if (key == "module.bilibilisearch.msg.emptyUrl") return "请先在 url= 后面粘贴 B 站视频链接或 BV 号。";
      if (key == "module.bilibilisearch.msg.keepPrefix") return "请保留解析前缀，只在 url= 后面粘贴 B 站视频链接或 BV 号。";
      if (key == "module.bilibilisearch.msg.urlPlayFailed") return "播放失败：请确认 url= 后面是 B 站视频链接或 BV 号，且保留了解析前缀。";
      if (key == "module.bilibilisearch.msg.needRebuild") return "请重新生成面板以使用链接确认框。";

      if (key == "module.bilibilisearch.hint.copy") return "点击下方链接，用 VRChat 键盘的复制功能复制。";
      if (key == "module.bilibilisearch.hint.page") return "复制上方链接，粘贴到下方 URL 框并完成输入，再点确认翻页。";
      if (key == "module.bilibilisearch.hint.play") return "复制上方链接，粘贴到下方 URL 框并完成输入，再点确认播放。";
      if (key == "module.bilibilisearch.hint.queue") return "复制上方播放链接，粘贴到下方 URL 框并完成输入，再点确认加入待播队列。";
      if (key == "module.bilibilisearch.hint.mismatch") return "链接不一致，请完整复制上方链接并完成 URL 输入。";
      if (key == "module.bilibilisearch.hint.playFailed") return "播放器未绑定、权限不足，或结果已变化。";

      if (key == "button.search") return "搜索";
      if (key == "button.previousPage") return "上一页";
      if (key == "button.nextPage") return "下一页";
      if (key == "button.close") return "关闭";
      if (key == "button.copyUrl") return "复制链接";
      if (key == "button.playVideo") return "播放";
      if (key == "button.addQueue") return "加入队列";

      return key;
    }
  }
}
