using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using Yamadev.YamaStream.Editor;

namespace Yamadev.YamaStream.Modules.BilibiliSearch.Editor
{
  /// <summary>
  /// Authors the finite record URL pool outside Udon runtime.
  /// </summary>
  /// <remarks>
  /// The range is configured in the Bilibili Search Setup window, directly below Base URL, and is
  /// remembered in EditorPrefs next to the backend address. "Generate Prefabs" therefore bakes the
  /// pool the window shows, and the window-less menu action generates the same pool. There is no
  /// separate Direct Action URLs window any more.
  /// </remarks>
  public static class BilibiliSearchDirectSetup
  {
    /// <summary>Shipped pool defaults, i.e. 500000 through 542002 at the default estimate.</summary>
    public const int DefaultStart = 500000;
    public const int DefaultCapacity = 42003;
    public const int DefaultLatest = 500000;
    public const int DefaultDailyGrowth = 5000;
    public const int DefaultDays = 7;

    /// <summary>Above this count the setup window warns that resource usage may be too high.</summary>
    public const int ResourceWarningUrlCount = 50000;
    /// <summary>Rough UTF-16 character size of six-digit and seven-digit srid URLs, in bytes.</summary>
    private const long UrlTextBytes = 105L;

    private const string StartPrefKey = "Yamadev.YamaStream.BilibiliSearch.RecordUrlStart";
    private const string CapacityPrefKey = "Yamadev.YamaStream.BilibiliSearch.RecordUrlCapacity";
    private const string LatestPrefKey = "Yamadev.YamaStream.BilibiliSearch.LatestObservedRecordId";
    private const string GrowthPrefKey = "Yamadev.YamaStream.BilibiliSearch.EstimatedIdsPerDay";
    private const string DaysPrefKey = "Yamadev.YamaStream.BilibiliSearch.DaysToCover";

    private static bool _loaded;
    private static int _start;
    private static int _capacity;
    private static int _latest;
    private static int _dailyGrowth;
    private static int _days;

    /// <summary>Reads the remembered pool settings once per scripting domain.</summary>
    private static void EnsureLoaded()
    {
      if (_loaded) return;
      _loaded = true;
      _start = EditorPrefs.GetInt(StartPrefKey, DefaultStart);
      _capacity = EditorPrefs.GetInt(CapacityPrefKey, DefaultCapacity);
      _latest = EditorPrefs.GetInt(LatestPrefKey, DefaultLatest);
      _dailyGrowth = EditorPrefs.GetInt(GrowthPrefKey, DefaultDailyGrowth);
      _days = EditorPrefs.GetInt(DaysPrefKey, DefaultDays);
    }

    /// <summary>Lowest record id of the pool the tool bakes; also the pool origin at runtime.</summary>
    public static int ConfiguredStart
    {
      get { EnsureLoaded(); return _start; }
      set { EnsureLoaded(); _start = value; EditorPrefs.SetInt(StartPrefKey, value); }
    }

    /// <summary>Amount of record URLs the tool bakes.</summary>
    public static int ConfiguredCapacity
    {
      get { EnsureLoaded(); return _capacity; }
      set { EnsureLoaded(); _capacity = value; EditorPrefs.SetInt(CapacityPrefKey, value); }
    }

    /// <summary>Latest record id observed in a backend response; used for the estimate only.</summary>
    public static int ConfiguredLatest
    {
      get { EnsureLoaded(); return _latest; }
      set { EnsureLoaded(); _latest = value; EditorPrefs.SetInt(LatestPrefKey, value); }
    }

    /// <summary>Estimated amount of new record ids per day; used for the estimate only.</summary>
    public static int ConfiguredDailyGrowth
    {
      get { EnsureLoaded(); return _dailyGrowth; }
      set { EnsureLoaded(); _dailyGrowth = value; EditorPrefs.SetInt(GrowthPrefKey, value); }
    }

    /// <summary>Days the pool should cover before the map is updated again.</summary>
    public static int ConfiguredDays
    {
      get { EnsureLoaded(); return _days; }
      set { EnsureLoaded(); _days = value; EditorPrefs.SetInt(DaysPrefKey, value); }
    }

    /// <summary>
    /// Draws the pool settings and persists every change to EditorPrefs.
    /// </summary>
    /// <returns>True while the configured range can be baked.</returns>
    public static bool DrawSettings()
    {
      float previousLabelWidth = EditorGUIUtility.labelWidth;
      try
      {
        // IMGUI's default label width clips these labels even in a wide window.
        EditorGUIUtility.labelWidth = Mathf.Max(230f,
          EditorStyles.label.CalcSize(new GUIContent("Days to cover (+20% reserve)")).x + 16f,
          EditorStyles.label.CalcSize(new GUIContent("URL count (max 2,000,000)")).x + 16f);

        int latest = EditorGUILayout.IntField("Latest observed record ID", ConfiguredLatest);
        if (latest != ConfiguredLatest) ConfiguredLatest = latest;
        int dailyGrowth = EditorGUILayout.IntField("Estimated IDs per day", ConfiguredDailyGrowth);
        if (dailyGrowth != ConfiguredDailyGrowth) ConfiguredDailyGrowth = dailyGrowth;
        int days = EditorGUILayout.IntField("Days to cover (+20% reserve)", ConfiguredDays);
        if (days != ConfiguredDays) ConfiguredDays = days;

        int plannedCount = PlanCapacity(ConfiguredStart, latest, dailyGrowth, days);
        using (new EditorGUI.DisabledScope(plannedCount == 0))
          if (GUILayout.Button("Apply coverage estimate")) ConfiguredCapacity = plannedCount;
        if (plannedCount == 0)
          EditorGUILayout.HelpBox("Invalid estimate or more than 2,000,000 URLs required. Adjust the first ID or coverage inputs.", MessageType.Warning);

        int start = EditorGUILayout.IntField("First record ID", ConfiguredStart);
        if (start != ConfiguredStart) ConfiguredStart = start;
        int capacity = EditorGUILayout.IntField("URL count (max 2,000,000)", ConfiguredCapacity);
        if (capacity != ConfiguredCapacity) ConfiguredCapacity = capacity;

        long last = (long)ConfiguredStart + ConfiguredCapacity - 1;
        bool rangeValid = IsRangeValid(ConfiguredStart, ConfiguredCapacity);
        EditorGUILayout.LabelField("Last record ID", rangeValid ? last.ToString() : "-");
        if (ConfiguredDailyGrowth > 0 && ConfiguredLatest >= ConfiguredStart && ConfiguredLatest <= last)
          EditorGUILayout.LabelField("Estimated remaining days", ((last - ConfiguredLatest) / (double)ConfiguredDailyGrowth).ToString("F1"));
        else EditorGUILayout.HelpBox("Observed ID is outside the pool, or growth is invalid.", MessageType.Warning);

        if (ConfiguredCapacity > ResourceWarningUrlCount)
        {
          EditorGUILayout.HelpBox("URL count " + ConfiguredCapacity.ToString("N0") + " is above the recommended " +
            ResourceWarningUrlCount.ToString("N0") + " URLs. Resource usage may be too high: text alone is roughly " +
            UrlTextMegabytes(ConfiguredCapacity) + " MB versus " + UrlTextMegabytes(ResourceWarningUrlCount) + " MB at the recommended limit, " +
            "and serialized objects, temporary copies, Undo and client memory add more. Raise the coverage only when necessary, " +
            "and verify world size, build time and memory before upload.", MessageType.Warning);
        }
        else
        {
          EditorGUILayout.HelpBox("URL count " + ConfiguredCapacity.ToString("N0") + " is within the recommended " +
            ResourceWarningUrlCount.ToString("N0") + " URLs. Text alone is roughly " + UrlTextMegabytes(ConfiguredCapacity) +
            " MB; serialized objects, temporary copies and Udon add more.", MessageType.Info);
        }
        return IsRangeValid(ConfiguredStart, ConfiguredCapacity);
      }
      finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
    }

    /// <summary>Rough UTF-16 text size of the baked pool, for display only.</summary>
    private static long UrlTextMegabytes(int count)
    {
      return Math.Max(1L, (long)count * UrlTextBytes / 1000000L);
    }

    public static bool IsRangeValid(int start, int count)
    {
      return start > 0 && count > 0 && count <= BilibiliSearchService.MaxRecordUrlCapacity &&
        (long)start + count - 1 <= int.MaxValue;
    }

    // Include the current ID and both pagination records; keep older configured IDs usable.
    public static int PlanCapacity(int start, int latest, int dailyGrowth, int days)
    {
      if (start < 1 || latest < start || dailyGrowth < 1 || days < 1) return 0;
      decimal growth = decimal.Ceiling((decimal)dailyGrowth * days * 1.2m);
      decimal count = latest - (decimal)start + growth + 3;
      if (count > BilibiliSearchService.MaxRecordUrlCapacity) return 0;
      int planned = (int)count;
      return IsRangeValid(start, planned) ? planned : 0;
    }

    public static void Bake(BilibiliSearchService service)
    {
      string root = service.BaseUrlString;
      if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
          !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        throw new InvalidOperationException("BilibiliSearch: Base URL must be an HTTPS endpoint without query or fragment.");
      int count = service.RecordUrlCapacity;
      if (!IsRangeValid(service.RecordUrlStart, count))
        throw new InvalidOperationException("BilibiliSearch: invalid direct URL pool range.");
      var urls = new VRCUrl[count];
      for (int i = 0; i < count; i++) urls[i] = new VRCUrl(root + "?srid=" + (service.RecordUrlStart + i));
      service.RecordUrls = urls;
      EditorUtility.SetDirty(service);
    }

    public static bool IsPoolValid(BilibiliSearchService service)
    {
      if (!IsRangeValid(service.RecordUrlStart, service.RecordUrlCapacity)) return false;
      if (service.RecordUrls == null || service.RecordUrls.Length != service.RecordUrlCapacity || service.RecordUrls.Length == 0) return false;
      for (int i = 0; i < service.RecordUrls.Length; i++)
      {
        var url = service.RecordUrls[i];
        if (VRCUrl.IsNullOrEmpty(url) || url.Get() != service.BaseUrlString + "?srid=" + (service.RecordUrlStart + i)) return false;
      }
      return true;
    }

    /// <summary>
    /// Fails when the freshly built pool did not reach the backing UdonBehaviour, so a prefab is
    /// never saved with an empty or truncated URL array.
    /// </summary>
    public static void VerifySerializedPool(GameObject root)
    {
      if (root == null) throw new InvalidOperationException("BilibiliSearch: nothing to verify.");
      var services = root.GetComponentsInChildren<BilibiliSearchService>(true);
      if (services.Length == 0) throw new InvalidOperationException("BilibiliSearch: module prefab has no search service.");
      foreach (var service in services)
      {
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(service);
        if (backing == null ||
            !backing.publicVariables.TryGetVariableValue<int>(nameof(service.RecordUrlStart), out var savedStart) || savedStart != service.RecordUrlStart ||
            !backing.publicVariables.TryGetVariableValue<int>(nameof(service.RecordUrlCapacity), out var savedCount) || savedCount != service.RecordUrlCapacity ||
            !backing.publicVariables.TryGetVariableValue<VRCUrl[]>(nameof(service.RecordUrls), out var savedUrls) ||
            savedUrls == null || savedUrls.Length != service.RecordUrlCapacity)
          throw new InvalidOperationException("UdonSharp did not serialize the URL pool. Prefabs were not saved; run Generate Prefabs again and check the Console.");
      }
    }
  }

  // Validate/bake configured URLs on the build copy before module injection.
  public class BilibiliSearchDirectUrlBuildProcess : IYamaPlayerBuildProcess
  {
    public int callbackOrder => -3200;
    public void Process()
    {
      foreach (var service in UnityEngine.Object.FindObjectsByType<BilibiliSearchService>(FindObjectsInactive.Include, FindObjectsSortMode.None))
      {
        if (!service.gameObject.activeInHierarchy) continue;
        if (!BilibiliSearchDirectSetup.IsPoolValid(service)) BilibiliSearchDirectSetup.Bake(service);
      }
    }
  }
}
