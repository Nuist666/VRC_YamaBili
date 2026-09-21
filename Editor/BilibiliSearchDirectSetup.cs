using System;
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
    private int _start = 550000;
    private int _capacity = 100000;

    [MenuItem("Tools/YamaPlayer/Bilibili Search/Direct Action URLs", priority = 102)]
    public static void Open() { GetWindow<BilibiliSearchDirectSetup>("Direct Action URLs"); }

    private void OnEnable()
    {
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath);
      var service = prefab == null ? null : prefab.GetComponentInChildren<BilibiliSearchService>(true);
      if (service != null) { _start = service.RecordUrlStart; _capacity = service.RecordUrlCapacity; }
    }

    private void OnGUI()
    {
      EditorGUILayout.HelpBox("Bake complete ?srid= URLs for direct page/play/queue buttons. " +
        "IDs outside this range cannot be generated at runtime. Update the range and upload the world again when needed. " +
        "100,000 URLs contain about 10.2 MB of UTF-16 text before object and Udon overhead.", MessageType.Info);
      _start = EditorGUILayout.IntField("First record ID", _start);
      _capacity = EditorGUILayout.IntField("URL count (max 200,000)", _capacity);
      using (new EditorGUI.DisabledScope(_start < 1 || _capacity < 1 || _capacity > 200000 || (long)_start + _capacity > int.MaxValue))
      {
        if (GUILayout.Button("Bake module prefab")) BakePrefab(_start, _capacity);
      }
    }

    public static void Bake(BilibiliSearchService service)
    {
      string root = service.BaseUrlString;
      if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
          !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        throw new InvalidOperationException("BilibiliSearch: Base URL must be an HTTPS endpoint without query or fragment.");
      int count = service.RecordUrlCapacity;
      if (service.RecordUrlStart < 1 || count < 1 || count > 200000 || (long)service.RecordUrlStart + count > int.MaxValue)
        throw new InvalidOperationException("BilibiliSearch: invalid direct URL pool range.");
      var urls = new VRCUrl[count];
      for (int i = 0; i < count; i++) urls[i] = new VRCUrl(root + "?srid=" + (service.RecordUrlStart + i));
      service.RecordUrls = urls;
      EditorUtility.SetDirty(service);
    }

    public static bool IsPoolValid(BilibiliSearchService service)
    {
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
