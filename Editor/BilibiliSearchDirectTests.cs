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
      string prettyJson = BilibiliSearchSridTester.FormatResponse("[{\"recordsid\":100,\"title\":\"a,b: {test}\"}]");
      Check(prettyJson.Contains("\n") && prettyJson.Contains("a,b: {test}"), "response JSON is multiline without changing string content");
      Check(BilibiliSearchSridTester.FormatResponse("<html>error</html>") == "<html>error</html>", "non-JSON response stays readable");
      Check(BilibiliSearchSridTester.FormatResponse("") == "", "empty response formatting");
      int previousId, nextId, observedId;
      BilibiliSearchSridTester.Analyze("[{\"recordsid\":\"100\"},{\"recordsid\":101}]", 100, 4,
        out previousId, out nextId, out observedId);
      Check(previousId == 102 && nextId == 103 && observedId == 101, "SRID tester raw pagination");
      string coverageReport = BilibiliSearchSridTester.Analyze("[{\"recordsid\":99},{\"recordsid\":100},{\"recordsid\":104}]",
        100, 4, out previousId, out nextId, out observedId, 10, 2);
      Check(coverageReport.Contains("Returned SRID range: 99 - 104") && coverageReport.Contains("Inside pool: 1; outside pool: 2"),
        "keyword search reports actual bounds and partial pool coverage");
      Check(coverageReport.Contains("OUTSIDE") && nextId == 0, "noncontiguous response still reports coverage without pagination");
      coverageReport = BilibiliSearchSridTester.Analyze("[{\"recordsid\":100},null]", 100, 4,
        out previousId, out nextId, out observedId);
      Check(coverageReport.Contains("INCOMPLETE"), "invalid rows cannot produce a coverage pass");
      coverageReport = BilibiliSearchSridTester.Analyze("[{\"recordsid\":100}]", 100, 1,
        out previousId, out nextId, out observedId);
      Check(coverageReport.Contains("PASS") && coverageReport.Contains("Next page: 102 / Outside pool"),
        "result coverage and pagination coverage reported separately");
      string forecast = BilibiliSearchSridTester.Forecast(100, 100, 149, 151, 10, 2);
      Check(forecast.Contains("Upper-bound headroom: 48") && forecast.Contains("Estimated remaining days: 4.8"),
        "response forecast reserves pagination IDs");
      Check(forecast.Contains("Suggested URL count: 76"), "response forecast uses observed ID and reserve");
      Check(BilibiliSearchSridTester.Forecast(100, 4, 110, 112, 10, 2).Contains("Estimated remaining days: 0.0"),
        "exhausted pool has zero remaining coverage");
      Check(BilibiliSearchSridTester.Forecast(100, 4, 99, 0, 10, 2).Contains("below the pool"), "below-pool forecast is qualified");
      Check(BilibiliSearchSridTester.Forecast(100, 4, 101, 103, 0, 2).Contains("Prediction unavailable"), "invalid growth blocks prediction");
      foreach (string wrapper in new[] { "data", "result", "list" })
      {
        BilibiliSearchSridTester.Analyze("{\"" + wrapper + "\":[{\"recordsid\":100}]}", 100, 3,
          out previousId, out nextId, out observedId);
        Check(previousId == 101 && nextId == 102 && observedId == 100, "SRID tester wrapper " + wrapper);
      }
      foreach (string body in new[] { "[]", "{}", "not json", "[null]", "[{\"recordsid\":100},{\"recordsid\":102}]",
        "[{\"recordsid\":2147483647}]", "[{\"recordsid\":1.5}]", "[{\"recordsid\":\"+100\"}]",
        "[{\"recordsid\":\"999999999999\"}]" })
      {
        BilibiliSearchSridTester.Analyze(body, 100, 3, out previousId, out nextId, out observedId);
        Check(previousId == 0 && nextId == 0, "SRID tester rejects unsafe pagination: " + body);
      }
      Check(BilibiliSearchSridTester.Coverage(int.MaxValue, int.MaxValue, 1) == "Inside pool", "SRID tester inclusive upper bound");
      Check(BilibiliSearchSridTester.Coverage(103, 100, 3).StartsWith("Outside pool"), "SRID tester outside pool");
      Check(BilibiliSearchSridTester.IsBackendValid("https://example.com/player/"), "SRID tester HTTPS endpoint");
      Check(!BilibiliSearchSridTester.IsBackendValid("https://example.com/player/#fragment"), "SRID tester rejects fragment");
      Check(BilibiliSearchDirectSetup.PlanCapacity(550000, 580000, 30000, 30) == 1110003, "30 days plus reserve and pagination");
      Check(BilibiliSearchDirectSetup.PlanCapacity(550000, 580000, 30000, 90) == 0, "oversized plan rejected without truncation");
      Check(BilibiliSearchDirectSetup.PlanCapacity(550000, 549999, 30000, 30) == 0, "observed ID below start rejected");
      Check(BilibiliSearchDirectSetup.PlanCapacity(1, 1, int.MaxValue, int.MaxValue) == 0, "planner arithmetic does not overflow");
      Check(BilibiliSearchDirectSetup.PlanCapacity(1, 1, 0, 30) == 0, "zero growth rejected");
      Check(BilibiliSearchDirectSetup.IsRangeValid(int.MaxValue, 1), "inclusive maximum ID accepted");
      Check(!BilibiliSearchDirectSetup.IsRangeValid(int.MaxValue, 2), "range overflow rejected");
      Check(BilibiliSearchDirectSetup.DefaultStart == 500000 && BilibiliSearchDirectSetup.DefaultCapacity == 42003, "shipped pool defaults");
      Check(BilibiliSearchDirectSetup.DefaultLatest == 500000 && BilibiliSearchDirectSetup.DefaultDailyGrowth == 5000 && BilibiliSearchDirectSetup.DefaultDays == 7, "shipped coverage inputs");
      Check(BilibiliSearchDirectSetup.PlanCapacity(BilibiliSearchDirectSetup.DefaultStart, BilibiliSearchDirectSetup.DefaultLatest,
        BilibiliSearchDirectSetup.DefaultDailyGrowth, BilibiliSearchDirectSetup.DefaultDays) == BilibiliSearchDirectSetup.DefaultCapacity,
        "shipped defaults match the documented plan");
      Check(BilibiliSearchDirectSetup.ResourceWarningUrlCount == 50000, "resource warning threshold");
      Check(BilibiliSearchDirectSetup.ResourceWarningUrlCount > BilibiliSearchDirectSetup.DefaultCapacity, "default pool stays below the warning threshold");
      Check(BilibiliSearchDirectSetup.ResourceWarningUrlCount < BilibiliSearchService.MaxRecordUrlCapacity, "warning threshold below the hard limit");
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
