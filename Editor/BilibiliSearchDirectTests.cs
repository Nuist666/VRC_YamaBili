using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace Yamadev.YamaStream.Modules.BilibiliSearch.Editor
{
  public static class BilibiliSearchDirectTests
  {
    private static int _assertions;
    private static void Check(bool condition, string name)
    {
      if (!condition) throw new InvalidOperationException("Direct action regression: " + name);
      _assertions++;
    }
    private static void Parse(BilibiliSearchService service, string body, int page = 1)
    {
      typeof(BilibiliSearchService).GetMethod("ParseResults", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(service, new object[] { body, "test", page });
    }
    private static string Row(string id, string record)
    {
      return "{\"id\":\"" + id + "\",\"title\":\"sample\",\"recordsid\":" + record + "}";
    }

    [MenuItem("Tools/YamaPlayer/Bilibili Search/Run Direct Action Regression Tests", priority = 103)]
    public static void Run() { Debug.Log("[BilibiliSearch] " + RunChecks() + " regression assertions passed."); }

    public static int RunChecks()
    {
      _assertions = 0;
      var root = new GameObject("BiliDirectRegression");
      try
      {
        var service = root.AddComponent<BilibiliSearchService>();
        var result = root.AddComponent<BilibiliSearchResult>();
        service.SetResult(result);
        typeof(BilibiliSearchService).GetField("_baseUrl", BindingFlags.Instance | BindingFlags.NonPublic)
          .SetValue(service, new VRCUrl("https://example.com/player/"));
        service.RecordUrlStart = 100;
        service.RecordUrlCapacity = 24;
        BilibiliSearchDirectSetup.Bake(service);
        Check(service.GetRecordUrl(100).Get() == "https://example.com/player/?srid=100", "first pool URL");
        Check(service.GetRecordUrl(123).Get().EndsWith("srid=123"), "last pool URL");
        Check(VRCUrl.IsNullOrEmpty(service.GetRecordUrl(99)), "lower pool bound");
        Check(VRCUrl.IsNullOrEmpty(service.GetRecordUrl(124)), "upper pool bound");
        Check(VRCUrl.IsNullOrEmpty(service.GetRecordUrl(int.MaxValue)), "overflow index rejected");
        service.RecordUrlStart = 101;
        Check(VRCUrl.IsNullOrEmpty(service.GetRecordUrl(101)), "changed pool origin rejected");
        service.RecordUrlStart = 100;
        typeof(BilibiliSearchService).GetField("_baseUrl", BindingFlags.Instance | BindingFlags.NonPublic)
          .SetValue(service, new VRCUrl("https://other.example/player/"));
        Check(VRCUrl.IsNullOrEmpty(service.GetRecordUrl(100)), "stale pool host rejected");
        typeof(BilibiliSearchService).GetField("_baseUrl", BindingFlags.Instance | BindingFlags.NonPublic)
          .SetValue(service, new VRCUrl("https://example.com/player/"));

        // The visible cap excludes later valid entries, and the last raw entry is not a BV.
        typeof(BilibiliSearchService).GetField("_maxResults", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, 1);
        Parse(service, "[" + Row("BV1fw411j7m1", "\"100\"") + "," + Row("BV13iunzQEHj", "101") + "," + Row("av123", "\"102\"") + "]");
        Check(result.Count == 1 && result.RecordIds[0] == 100, "visible records remain aligned");
        Check(result.PreviousRecordId == 103 && result.NextRecordId == 104, "raw pagination before filtering and cap");
        Check(service.MatchesResultUrl(0, service.GetRecordUrl(100)), "matching result URL accepted");
        Check(!service.MatchesResultUrl(0, service.GetRecordUrl(101)), "other row URL rejected");
        Check(!service.MatchesResultUrl(0, new VRCUrl("https://attacker.example/?srid=100")), "foreign URL rejected");
        int[] previous = result.RecordIds;
        Parse(service, "{\"error\":\"failure\"}", 2);
        Check(result.RecordIds == previous && result.Page == 1 && result.NextRecordId == 104 && !string.IsNullOrEmpty(result.Error), "bad response preserves previous page");
        Parse(service, "[]", 2);
        Check(result.Count == 0 && result.Page == 2 && result.NextRecordId == 0 && result.PreviousRecordId == 0, "empty page clears pagination");
        Parse(service, "[" + Row("BV1fw411j7m1", "\"100\"") + "," + Row("BV13iunzQEHj", "\"102\"") + "]");
        Check(result.NextRecordId == 0, "noncontiguous raw records fail closed");
        foreach (var invalid in new[] { "\"-1\"", "\"999999999999\"", "100.5", "null", "\"+100\"" })
        {
          Parse(service, "[" + Row("BV1fw411j7m1", invalid) + "]");
          Check(result.RecordIds[0] == 0 && result.NextRecordId == 0, "invalid record: " + invalid);
        }
        Parse(service, "{\"data\":[" + Row("BV1fw411j7m1", "100") + "]}", 3);
        Check(result.RecordIds[0] == 100 && result.NextRecordId == 102 && result.Page == 3, "wrapped numeric record and logical page");
        var ui = root.AddComponent<BilibiliSearchUI>();
        typeof(BilibiliSearchUI).GetField("_ids", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, result.Ids);
        typeof(BilibiliSearchUI).GetField("_recordIds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, result.RecordIds);
        Check(ui.MatchesDisplayedResult(0, "BV1fw411j7m1", 100), "current UI row accepted");
        Check(!ui.MatchesDisplayedResult(0, "BV1fw411j7m1", 101), "stale UI row rejected");
        return _assertions;
      }
      finally { UnityEngine.Object.DestroyImmediate(root); }
    }
  }
}
