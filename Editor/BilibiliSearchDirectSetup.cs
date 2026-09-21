using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using Yamadev.YamaStream.Editor;

namespace Yamadev.YamaStream.Modules.BilibiliSearch.Editor
{
  /// <summary>Authors the finite record URL pool outside Udon runtime.</summary>
  public class BilibiliSearchDirectSetup : EditorWindow
  {
    public const string ModulePath = "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliSearch.prefab";
    private int _start = 600000;
    private int _capacity = 210003;
    private int _latest = 600000;
    private int _dailyGrowth = 25000;
    private int _days = 7;
    private const string PendingBake = "BilibiliSearch.PendingBake";
    /// <summary>Above this count the window warns that resource usage may be too high.</summary>
    public const int ResourceWarningUrlCount = 220000;
    /// <summary>Rough UTF-16 character size of six-digit and seven-digit srid URLs, in bytes.</summary>
    private const long UrlTextBytes = 105L;

    // UdonSharp caches field layouts for the lifetime of the Unity scripting domain.
    // Compiling alone cannot repair a layout cached before new fields were added.
    private static void RequestBake(int start, int count)
    {
      if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        throw new InvalidOperationException("Wait for Unity compilation and exit Play mode before baking URLs.");
      if (!IsRangeValid(start, count)) throw new InvalidOperationException("Invalid direct URL pool range.");
      UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
      if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError() || EditorUtility.scriptCompilationFailed)
        throw new InvalidOperationException("Resolve compilation errors before baking URLs.");
      SessionState.SetInt(PendingBake + ".Start", start);
      SessionState.SetInt(PendingBake + ".Count", count);
      SessionState.SetBool(PendingBake, true);
      Debug.Log("[BilibiliSearch] Refreshing UdonSharp serialization; URL baking will continue automatically after script reload.");
      EditorUtility.RequestScriptReload();
    }

    [InitializeOnLoadMethod]
    private static void ResumeBakeAfterReload()
    {
      if (SessionState.GetBool(PendingBake, false)) EditorApplication.update += ContinueBake;
    }

    private static void ContinueBake()
    {
      if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
      EditorApplication.update -= ContinueBake;
      SessionState.SetBool(PendingBake, false);
      try { BakePrefab(SessionState.GetInt(PendingBake + ".Start", 0), SessionState.GetInt(PendingBake + ".Count", 0)); }
      catch (Exception exception) { Debug.LogException(exception); }
    }

    [MenuItem("Tools/YamaPlayer/Bilibili Search/Direct Action URLs", priority = 102)]
    public static void Open() { GetWindow<BilibiliSearchDirectSetup>("Direct Action URLs"); }

    private void OnEnable()
    {
      minSize = new Vector2(480f, 340f);
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath);
      var service = prefab == null ? null : prefab.GetComponentInChildren<BilibiliSearchService>(true);
      if (service != null) { _start = service.RecordUrlStart; _capacity = service.RecordUrlCapacity; }
    }

    private void OnGUI()
    {
      float previousLabelWidth = EditorGUIUtility.labelWidth;
      try
      {
        // IMGUI's default label width clips these labels even in a wide window.
        EditorGUIUtility.labelWidth = Mathf.Max(230f,
          EditorStyles.label.CalcSize(new GUIContent("Days to cover (+20% reserve)")).x + 16f,
          EditorStyles.label.CalcSize(new GUIContent("URL count (max 2,000,000)")).x + 16f);
        DrawSettings();
      }
      finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
    }

    private void DrawSettings()
    {
      EditorGUILayout.HelpBox("Bake complete ?srid= URLs for direct page/play/queue buttons. " +
        "IDs outside this range cannot be generated at runtime. Update the range and upload the world again when needed. " +
        "Coverage is an estimate based on observed growth, not a guarantee.", MessageType.Info);
      _latest = EditorGUILayout.IntField("Latest observed record ID", _latest);
      _dailyGrowth = EditorGUILayout.IntField("Estimated IDs per day", _dailyGrowth);
      _days = EditorGUILayout.IntField("Days to cover (+20% reserve)", _days);
      int plannedCount = PlanCapacity(_start, _latest, _dailyGrowth, _days);
      using (new EditorGUI.DisabledScope(plannedCount == 0))
        if (GUILayout.Button("Apply coverage estimate")) _capacity = plannedCount;
      if (plannedCount == 0)
        EditorGUILayout.HelpBox("Invalid estimate or more than 2,000,000 URLs required. Adjust the first ID or coverage inputs.", MessageType.Warning);
      _start = EditorGUILayout.IntField("First record ID", _start);
      _capacity = EditorGUILayout.IntField("URL count (max 2,000,000)", _capacity);
      long last = (long)_start + _capacity - 1;
      EditorGUILayout.LabelField("Last record ID", last.ToString());
      if (_dailyGrowth > 0 && _latest >= _start && _latest <= last)
        EditorGUILayout.LabelField("Estimated remaining days", ((last - _latest) / (double)_dailyGrowth).ToString("F1"));
      else EditorGUILayout.HelpBox("Observed ID is outside the pool, or growth is invalid.", MessageType.Warning);
      if (_capacity > ResourceWarningUrlCount)
      {
        EditorGUILayout.HelpBox("URL count " + _capacity.ToString("N0") + " is above the recommended " +
          ResourceWarningUrlCount.ToString("N0") + " URLs. Resource usage may be too high: text alone is roughly " +
          UrlTextMegabytes(_capacity) + " MB versus " + UrlTextMegabytes(ResourceWarningUrlCount) + " MB at the recommended limit, " +
          "and serialized objects, temporary copies, Undo and client memory add more. Raise the coverage only when necessary, " +
          "and verify world size, build time and memory before upload.", MessageType.Warning);
      }
      else
      {
        EditorGUILayout.HelpBox("URL count " + _capacity.ToString("N0") + " is within the recommended " +
          ResourceWarningUrlCount.ToString("N0") + " URLs. Text alone is roughly " + UrlTextMegabytes(_capacity) +
          " MB; serialized objects, temporary copies and Udon add more.", MessageType.Info);
      }
      using (new EditorGUI.DisabledScope(!IsRangeValid(_start, _capacity)))
      {
        if (GUILayout.Button("Bake module prefab")) RequestBake(_start, _capacity);
      }
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

    private static void UpdateVersion(GameObject root)
    {
      foreach (var definition in root.GetComponentsInChildren<YamaPlayerModuleDefinition>(true))
        definition.version = BilibiliSearchPanelSetup.Version;
      foreach (var label in root.GetComponentsInChildren<Text>(true))
      {
        if (label.name == "VersionButtonText") label.text = "v" + BilibiliSearchPanelSetup.Version;
        else if (label.name == "VersionName") label.text = "BiliBili Search v" + BilibiliSearchPanelSetup.Version;
        else if (label.name == "VersionChangelogValue") label.text = BilibiliSearchPanelSetup.Changelog;
      }
    }

    public static void BakePrefab(int start, int count)
    {
      if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before baking URLs.");
      if (!IsRangeValid(start, count)) throw new InvalidOperationException("Invalid direct URL pool range.");
      if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
        throw new InvalidOperationException("Wait for successful Unity compilation before baking URLs.");
      UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
      if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
        throw new InvalidOperationException("UdonSharp compilation failed; prefab was not changed.");
      var root = PrefabUtility.LoadPrefabContents(ModulePath);
      try
      {
        var services = root.GetComponentsInChildren<BilibiliSearchService>(true);
        if (services.Length == 0) throw new InvalidOperationException("Module prefab has no search service.");
        foreach (var service in services)
        {
          service.RecordUrlStart = start;
          service.RecordUrlCapacity = count;
          Bake(service);
          UdonSharpEditorUtility.CopyProxyToUdon(service);
          var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(service);
          if (backing == null ||
              !backing.publicVariables.TryGetVariableValue<int>(nameof(service.RecordUrlStart), out var savedStart) || savedStart != start ||
              !backing.publicVariables.TryGetVariableValue<int>(nameof(service.RecordUrlCapacity), out var savedCount) || savedCount != count ||
              !backing.publicVariables.TryGetVariableValue<VRCUrl[]>(nameof(service.RecordUrls), out var savedUrls) || savedUrls == null || savedUrls.Length != count)
            throw new InvalidOperationException("UdonSharp did not serialize the URL pool. Prefab was not saved. Use the Direct Action URLs window to refresh serialization and retry.");
        }
        UpdateVersion(root);
        PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
      }
      finally { PrefabUtility.UnloadPrefabContents(root); }
      string panelPath = ModulePath.Replace("BilibiliSearch.prefab", "BilibiliSearchPanel.prefab");
      if (AssetDatabase.LoadAssetAtPath<GameObject>(panelPath) != null)
      {
        var panel = PrefabUtility.LoadPrefabContents(panelPath);
        try { UpdateVersion(panel); PrefabUtility.SaveAsPrefabAsset(panel, panelPath); }
        finally { PrefabUtility.UnloadPrefabContents(panel); }
      }
      AssetDatabase.SaveAssets();
      Debug.Log($"[BilibiliSearch] Direct URLs baked: {start} through {(long)start + count - 1} ({count} URLs). Scene prefab instances inherit this pool unless overridden.");
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
