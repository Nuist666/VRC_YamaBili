using System;
using System.Collections.Generic;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine.EventSystems;
using VRC.SDKBase;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using Yamadev.YamaStream.Modules.BilibiliSearch;
using Yamadev.YamaStream.UI;
using Yamadev.YamaStream.Editor;

namespace Yamadev.YamaStream.Modules.BilibiliSearch.Editor
{
  /// <summary>
  /// Builds the bilibili search panel prefab and the matching module prefab.
  /// The panel is authored from code so that every reference is guaranteed to be wired.
  /// </summary>
  public class BilibiliSearchPanelSetup : EditorWindow
  {
    /// <summary>
    /// Generated assets live inside the module folder so they travel with it. Every other
    /// YamaPlayer module ships its prefab inside the package as well, and ModuleManager can
    /// only list a module the project has a prefab for: dropping Modules/BilibiliSearch into
    /// another project is then enough to make the module available there.
    /// </summary>
    public const string Version = "1.1.1";
    public const string Changelog = "v1.1.1\nDirect Action URLs に、URL数の試算機能とリソース使用量の警告を追加しました。\n\nv1.1.0\n検索結果のページ切り替え・再生・キュー追加をワンクリックで実行できるようにしました。\n\nv1.0.0\n初回リリース";

    private const string DefaultOutputFolder = "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch";
    /// <summary>Where the prefabs used to be generated before they moved into the package.</summary>
    private const string LegacyOutputFolder = "Assets/Yamadev/YamaPlayerGenerated";
    private const string PanelPrefabName = "BilibiliSearchPanel";
    private const string ModulePrefabName = "BilibiliSearch";
    // The panel overlays the main page. It used to be injected into Canvas/User/Main/Pages,
    // but that page container is inactive on the main page, so the panel could not be opened
    // from there. LeftSide/Container is the icon column that also holds the url input icon.
    private const string DefaultTargetPath = "Canvas/User/Main";
    private const string LauncherTargetPath = "Canvas/User/Main/LeftSide/Container";
    // The package ships no server of its own. Every user points the module at their own bilibili
    // player backend, and the tool refuses to generate anything while the address is still the
    // placeholder below. See BACKEND.md for what that server has to provide.
    private const string BackendPlaceholder = "https://your-backend.example.com/player/";
    /// <summary>EditorPrefs key the address the user typed is remembered under.</summary>
    private const string BackendPrefKey = "Yamadev.YamaStream.BilibiliSearch.BackendBaseUrl";
    private const string IconFileName = "BilibiliIcon.png";
    /// <summary>Author avatar shown by the version overlay. Shipped next to the module.</summary>
    private const string AuthorFileName = "Author.png";
    /// <summary>
    /// Icons of YamaPlayer itself. The version overlay reuses the social marks of the package it
    /// is installed in instead of shipping its own copies of them.
    /// </summary>
    private const string YamaPlayerImageFolder = "Packages/net.kwxxw.yama-stream/Assets/Images";
    /// <summary>White 9-sliced rounded rectangle used for every button and input field.</summary>
    private const string FrameFileName = "PanelFrame.png";
    /// <summary>
    /// Icons of the two AI assistants credited by the version overlay, shipped next to the module.
    /// DeepSeekIcon.png is the DeepSeek mark, CodexIcon.png the Codex CLI mark.
    /// </summary>
    private const string AiIconDeepSeek = "DeepSeekIcon.png";
    private const string AiIconCodex = "CodexIcon.png";
    /// <summary>Social marks reused from YamaPlayer's own package.</summary>
    private const string SocialIconVRChat = "vrchat.png";
    private const string SocialIconTwitter = "twitter.png";
    private const string SocialIconGithub = "github.png";
    private const int FrameTextureSize = 96;
    private const int FrameRadius = 32;

    /// <summary>Height of one result row. The result list reads it from the cell template.</summary>
    private const float CellHeight = 240f;
    /// <summary>Height of the text block (title + uploader/BV line + summary) inside a row.</summary>
    private const float InfoHeight = 140f;
    /// <summary>Height of the uploader + BV id line.</summary>
    private const float MetaHeight = 30f;

    /// <summary>
    /// YamaPlayer's default appearance colours, only used as authoring values so the prefab
    /// looks right in the editor. At build time YamaPlayer's AppearanceBuildProcess overwrites
    /// the colour of everything carrying a ColorDefinition with the world's colour set.
    /// </summary>
    private static readonly Color PrimaryColorDefault = new Color(240f / 255f, 98f / 255f, 146f / 255f, 1f);
    private static readonly Color SecondaryColorDefault = new Color(248f / 255f, 187f / 255f, 208f / 255f, 31f / 255f);

    /// <summary>Vertical axis of the left half of the version overlay, measured from its centre.</summary>
    private const float VersionLeftAxis = -400f;
    /// <summary>Author avatar of the version overlay. 4:3, the shape Author.png itself has.</summary>
    private const float VersionAvatarTop = -150f;
    private const float VersionAvatarWidth = 320f;
    private const float VersionAvatarHeight = 240f;
    /// <summary>One account row of the version overlay.</summary>
    private const float VersionRowHeight = 40f;
    /// <summary>Gap between two account rows, i.e. one blank line of the overlay.</summary>
    private const float VersionRowSpacing = 12f;
    /// <summary>Padding on both sides of a row's content, see BuildVersionRow.</summary>
    private const float VersionRowInset = 8f;

    private string _outputFolder = DefaultOutputFolder;
    private string _targetPath = DefaultTargetPath;
    private int _siblingIndex = -1;
    private bool _instantiateInScene = true;
    private static Font _cachedFont;
    private static Sprite _cachedIcon;
    private static Sprite _cachedFrame;
    /// <summary>Icons resolved by file name, see ResolveIcon.</summary>
    private static readonly Dictionary<string, Sprite> _cachedIcons = new Dictionary<string, Sprite>();
    /// <summary>Folder of the current run, used by the static UI helpers below.</summary>
    private static string _assetFolder;

    /// <summary>
    /// Address of the backend the panel talks to, as typed in the setup window. It is stored in
    /// EditorPrefs so it survives a domain reload and so the menu entry that generates without a
    /// window uses the address that was configured before.
    /// </summary>
    private static string _backendBase;

    private static string BackendBase
    {
      get
      {
        if (_backendBase == null) _backendBase = EditorPrefs.GetString(BackendPrefKey, string.Empty);
        return _backendBase;
      }
      set
      {
        _backendBase = value == null ? string.Empty : value;
        EditorPrefs.SetString(BackendPrefKey, _backendBase);
      }
    }

    /// <summary>Base url of the backend, e.g. https://bili.example.com/player/ - empty when unset.</summary>
    private static string BackendUrl => NormalizeBackend(BackendBase);

    /// <summary>Search request url the search box is pre-filled with.</summary>
    private static string SearchUrl => BackendUrl + "?page=1&keyword=";

    /// <summary>Playback request url YamaPlayer's url inputs are pre-filled with.</summary>
    private static string PlayUrl => BackendUrl + "?url=";

    /// <summary>True once a plausible backend address was configured in the setup window.</summary>
    public static bool BackendConfigured => BackendUrl.Length > 0 && BackendUrl != BackendPlaceholder;

    /// <summary>
    /// Cleans up the address the user typed: trims it, drops a query string and makes sure it ends
    /// with a single slash, so "?page=1&amp;keyword=" can simply be appended to it.
    /// </summary>
    private static string NormalizeBackend(string value)
    {
      if (string.IsNullOrEmpty(value)) return string.Empty;

      value = value.Trim();
      if (!value.StartsWith("http://") && !value.StartsWith("https://")) return string.Empty;

      int query = value.IndexOf('?');
      if (query >= 0) value = value.Substring(0, query);
      if (!value.EndsWith("/")) value += "/";
      return value;
    }

    /// <summary>
    /// Temporary objects are destroyed one editor frame later on purpose. UdonSharp queues a
    /// delayed callback that touches the backing UdonBehaviour, and destroying the object in
    /// the same frame makes that callback throw a MissingReferenceException.
    /// </summary>
    private static void DiscardTemporary(GameObject temp)
    {
      if (temp == null) return;
      temp.hideFlags = HideFlags.HideAndDontSave;
      EditorApplication.delayCall += () =>
      {
        if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
      };
    }

    /// <summary>
    /// Font used by every text of the panel.
    /// Unity's built in LegacyRuntime.ttf is preferred, exactly like YamaPlayer's own build
    /// process does for the languages that ship without a font: it is the only one that renders
    /// CJK through the dynamic font fallback, which the panel needs because bilibili titles and
    /// the panel title are Chinese. A project font is only used when the built in one cannot be
    /// resolved.
    /// </summary>
    private static Font ResolvePanelFont()
    {
      if (_cachedFont != null) return _cachedFont;

      try
      {
        _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      }
      catch (Exception e)
      {
        Debug.LogWarning("[BilibiliSearch] Built in font unavailable: " + e.Message);
      }
      if (_cachedFont != null) return _cachedFont;

      string[] preferredNames = { "ZenMaruGothic-Regular", "Arial.ttf" };
      foreach (string preferredName in preferredNames)
      {
        string[] guids = AssetDatabase.FindAssets(preferredName);
        foreach (string guid in guids)
        {
          string path = AssetDatabase.GUIDToAssetPath(guid);
          string fileName = Path.GetFileName(path);
          if (!fileName.Equals(preferredName, StringComparison.OrdinalIgnoreCase) &&
              !fileName.Equals(preferredName + ".ttf", StringComparison.OrdinalIgnoreCase)) continue;

          Font font = AssetDatabase.LoadAssetAtPath<Font>(path);
          if (font != null)
          {
            _cachedFont = font;
            return _cachedFont;
          }
        }
      }

      // Last resort: any font asset in the project so the panel is never left with a null font.
      string[] anyFonts = AssetDatabase.FindAssets("t:Font");
      if (anyFonts.Length > 0)
      {
        _cachedFont = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(anyFonts[0]));
      }

      if (_cachedFont == null) Debug.LogWarning("[BilibiliSearch] No font asset found for the search panel.");
      return _cachedFont;
    }

    /// <summary>
    /// Draws the bilibili "TV" mark procedurally and stores it as a sprite asset, so the
    /// launcher button never depends on an icon file that may not ship with the project.
    /// </summary>
    private static Sprite ResolveBilibiliIcon(string folder)
    {
      if (_cachedIcon != null) return _cachedIcon;
      if (string.IsNullOrEmpty(folder)) folder = DefaultOutputFolder;

      string path = folder + "/" + IconFileName;
      Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
      if (existing != null)
      {
        _cachedIcon = existing;
        return _cachedIcon;
      }

      const int size = 128;
      const int samples = 4;
      // The shapes below are authored in a 256px space and sampled down to 'size'.
      const float shapeScale = 256f / size;

      // Shapes are laid out in pixels with y measured from the top, which is easier to read.
      const float bodyLeft = 26f, bodyRight = 230f, bodyTop = 76f, bodyBottom = 208f, bodyRadius = 34f;
      const float eyeTop = 120f, eyeBottom = 172f, eyeRadius = 10f;
      const float antennaRadius = 13f;

      Color pink = new Color(0.9843f, 0.4471f, 0.6f, 1f);
      var pixels = new Color[size * size];

      for (int y = 0; y < size; y++)
      {
        for (int x = 0; x < size; x++)
        {
          float bodyCoverage = 0f;
          float eyeCoverage = 0f;

          for (int sy = 0; sy < samples; sy++)
          {
            for (int sx = 0; sx < samples; sx++)
            {
              float sampleX = (x + (sx + 0.5f) / samples) * shapeScale;
              float sampleY = (y + (sy + 0.5f) / samples) * shapeScale;

              bool inBody =
                InsideRoundedRect(sampleX, sampleY, bodyLeft, bodyTop, bodyRight, bodyBottom, bodyRadius) ||
                InsideCapsule(sampleX, sampleY, 78f, 86f, 40f, 34f, antennaRadius) ||
                InsideCapsule(sampleX, sampleY, 178f, 86f, 216f, 34f, antennaRadius);
              bool inEye =
                InsideRoundedRect(sampleX, sampleY, 78f, eyeTop, 110f, eyeBottom, eyeRadius) ||
                InsideRoundedRect(sampleX, sampleY, 146f, eyeTop, 178f, eyeBottom, eyeRadius);

              if (inBody) bodyCoverage += 1f;
              if (inEye) eyeCoverage += 1f;
            }
          }

          bodyCoverage /= samples * samples;
          eyeCoverage /= samples * samples;

          float alpha = eyeCoverage + bodyCoverage * (1f - eyeCoverage);
          Color color = Color.clear;
          if (alpha > 0f)
          {
            color = (Color.white * eyeCoverage + pink * (bodyCoverage * (1f - eyeCoverage))) / alpha;
            color.a = alpha;
          }

          // SetPixels starts at the bottom left, the shapes are authored top down.
          pixels[(size - 1 - y) * size + x] = color;
        }
      }

      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
      texture.SetPixels(pixels);
      texture.Apply();
      byte[] png = texture.EncodeToPNG();
      UnityEngine.Object.DestroyImmediate(texture);

      if (png == null || png.Length == 0)
      {
        Debug.LogWarning("[BilibiliSearch] Could not encode the launcher icon.");
        return null;
      }

      Directory.CreateDirectory(folder);
      File.WriteAllBytes(path, png);
      AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
      ApplyIconImportSettings(path);

      _cachedIcon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
      if (_cachedIcon == null) Debug.LogWarning($"[BilibiliSearch] '{path}' did not import as a sprite.");
      return _cachedIcon;
    }

    /// <summary>
    /// Import settings shared by the icons the tool writes itself: a plain single sprite that keeps
    /// its alpha, sampled bilinearly and never tiled.
    /// </summary>
    private static void ApplyIconImportSettings(string path)
    {
      var importer = AssetImporter.GetAtPath(path) as TextureImporter;
      if (importer == null) return;

      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.alphaIsTransparency = true;
      importer.mipmapEnabled = false;
      importer.filterMode = FilterMode.Bilinear;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.SaveAndReimport();
    }

    /// <summary>
    /// Builds the 9-sliced rounded rectangle that gives every button and input field its
    /// pill shape. The border is written into the sprite importer so Image.type = Sliced can
    /// stretch the middle without stretching the corners.
    /// </summary>
    private static Sprite ResolvePanelFrameSprite(string folder)
    {
      if (_cachedFrame != null) return _cachedFrame;
      if (string.IsNullOrEmpty(folder)) folder = DefaultOutputFolder;

      string path = folder + "/" + FrameFileName;
      Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
      if (existing != null)
      {
        _cachedFrame = existing;
        return _cachedFrame;
      }

      const int size = FrameTextureSize;
      const int samples = 4;
      var pixels = new Color[size * size];

      for (int y = 0; y < size; y++)
      {
        for (int x = 0; x < size; x++)
        {
          float coverage = 0f;
          for (int sy = 0; sy < samples; sy++)
          {
            for (int sx = 0; sx < samples; sx++)
            {
              float sampleX = x + (sx + 0.5f) / samples;
              float sampleY = y + (sy + 0.5f) / samples;
              if (InsideRoundedRect(sampleX, sampleY, 0f, 0f, size, size, FrameRadius)) coverage += 1f;
            }
          }
          coverage /= samples * samples;
          pixels[(size - 1 - y) * size + x] = new Color(1f, 1f, 1f, coverage);
        }
      }

      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
      texture.SetPixels(pixels);
      texture.Apply();
      byte[] png = texture.EncodeToPNG();
      UnityEngine.Object.DestroyImmediate(texture);

      if (png == null || png.Length == 0)
      {
        Debug.LogWarning("[BilibiliSearch] Could not encode the panel frame sprite.");
        return null;
      }

      Directory.CreateDirectory(folder);
      File.WriteAllBytes(path, png);
      AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

      var importer = AssetImporter.GetAtPath(path) as TextureImporter;
      if (importer != null)
      {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        // 1 sprite pixel = 1 canvas unit (the canvas scaler uses 100 reference pixels per
        // unit), so this radius is 32 canvas units and clamps to half the height, which is
        // exactly what turns a 48 or 64 tall control into a capsule.
        settings.spriteBorder = new Vector4(FrameRadius, FrameRadius, FrameRadius, FrameRadius);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spritePixelsPerUnit = 100f;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
      }

      _cachedFrame = AssetDatabase.LoadAssetAtPath<Sprite>(path);
      if (_cachedFrame == null) Debug.LogWarning($"[BilibiliSearch] '{path}' did not import as a sprite.");
      return _cachedFrame;
    }

    /// <summary>
    /// Resolves a sprite by file name, preferring the given folder. The whole project is searched as
    /// a fallback, so an icon still resolves when the package sits somewhere else than its usual
    /// folder. A file that is not imported as a sprite yet gets the import settings a UI sprite
    /// needs.
    /// </summary>
    private static Sprite ResolveIcon(string fileName, string preferredFolder)
    {
      Sprite cached;
      // The cache also remembers a failure, so a missing icon is only reported once per run.
      if (_cachedIcons.TryGetValue(fileName, out cached)) return cached;

      string path = null;
      if (!string.IsNullOrEmpty(preferredFolder))
      {
        string candidate = preferredFolder + "/" + fileName;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(candidate) != null) path = candidate;
      }

      if (path == null)
      {
        string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName) + " t:Sprite");
        foreach (string guid in guids)
        {
          string candidate = AssetDatabase.GUIDToAssetPath(guid);
          if (Path.GetFileName(candidate) != fileName) continue;
          path = candidate;
          break;
        }
      }

      Sprite sprite = null;
      if (path != null)
      {
        EnsureSpriteImport(path);
        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
      }

      if (sprite == null) Debug.LogWarning($"[BilibiliSearch] Icon '{fileName}' was not found, the matching row of the version overlay stays without an icon.");
      _cachedIcons[fileName] = sprite;
      return sprite;
    }

    /// <summary>
    /// Forces the import settings a texture needs to be used as a UI sprite: a single sprite that
    /// keeps its alpha, sampled bilinearly and never tiled. Nothing is reimported when the file
    /// already has them.
    /// </summary>
    private static void EnsureSpriteImport(string path)
    {
      var importer = AssetImporter.GetAtPath(path) as TextureImporter;
      if (importer == null) return;

      bool changed = false;
      if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
      if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
      if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
      if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
      if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; changed = true; }
      if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; changed = true; }
      if (changed) importer.SaveAndReimport();
    }

    /// <summary>
    /// Marks an Image or Text as driven by YamaPlayer's appearance settings. YamaPlayer's
    /// AppearanceBuildProcess recolours every ColorDefinition below the UIController, and the
    /// injected panel is part of that hierarchy.
    /// </summary>
    private static void SetColorRole(GameObject target, ColorType role)
    {
      if (target == null) return;
      var definition = target.GetComponent<ColorDefinition>();
      if (definition == null) definition = target.AddComponent<ColorDefinition>();
      definition.colorType = role;
    }

    /// <summary>Background of a button / input field: rounded capsule in the given role's colour.</summary>
    private static Image AddPill(GameObject target, Color color, ColorType role)
    {
      var image = AddImage(target, color);
      var frame = ResolvePanelFrameSprite(_assetFolder);
      if (frame != null)
      {
        image.sprite = frame;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
      }
      SetColorRole(target, role);
      return image;
    }

    private static bool InsideRoundedRect(float x, float y, float left, float top, float right, float bottom, float radius)    {
      if (x < left || x > right || y < top || y > bottom) return false;
      float centerX = Mathf.Clamp(x, left + radius, right - radius);
      float centerY = Mathf.Clamp(y, top + radius, bottom - radius);
      float dx = x - centerX, dy = y - centerY;
      return dx * dx + dy * dy <= radius * radius;
    }

    private static bool InsideCapsule(float x, float y, float ax, float ay, float bx, float by, float radius)
    {
      float abx = bx - ax, aby = by - ay;
      float apx = x - ax, apy = y - ay;
      float lengthSq = abx * abx + aby * aby;
      float t = lengthSq <= 0f ? 0f : Mathf.Clamp01((apx * abx + apy * aby) / lengthSq);
      float dx = apx - abx * t, dy = apy - aby * t;
      return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>
    /// Opens the setup window, where the backend address is configured and the prefabs are built.
    /// </summary>
    /// <remarks>
    /// The path of this item must not be the prefix of another menu item: Unity turns a path that
    /// has children into a submenu, and the plain item silently disappears. The two actions that
    /// used to live below "Bilibili Search Setup" therefore sit in their own "Bilibili Search"
    /// menu (see BilibiliSearchRepair).
    /// </remarks>
    [MenuItem("Tools/YamaPlayer/Bilibili Search Setup", priority = 100)]
    public static void Open()
    {
      var window = GetWindow<BilibiliSearchPanelSetup>(true, "Bilibili Search Setup");
      window.minSize = new Vector2(460f, 220f);
      window.Show();
    }

    private void OnGUI()
    {
      EditorGUILayout.LabelField("Bilibili Search", EditorStyles.boldLabel);
      EditorGUILayout.HelpBox(
        "Creates a search panel prefab and a module prefab, then optionally places it in the open scene.\n" +
        "The module is injected into the YamaPlayer UI at build time through the path below.",
        MessageType.Info);

      _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
      _targetPath = EditorGUILayout.TextField("Target Path", _targetPath);
      _siblingIndex = EditorGUILayout.IntField("Sibling Index (-1 = last)", _siblingIndex);
      _instantiateInScene = EditorGUILayout.Toggle("Place In Current Scene", _instantiateInScene);

      EditorGUILayout.Space();
      EditorGUILayout.LabelField("Backend", EditorStyles.boldLabel);
      BackendBase = EditorGUILayout.TextField("Base URL", BackendBase);

      if (BackendConfigured)
      {
        EditorGUILayout.HelpBox(
          "The panel is pre-filled with:\n" +
          SearchUrl + "\n" + PlayUrl, MessageType.None);
      }
      else
      {
        EditorGUILayout.HelpBox(
          "Enter the address of your own bilibili player backend, for example\n" +
          "  https://bili.example.com/player/\n" +
          "This package ships no server: the search box and the playback url are built from the " +
          "address above. See Modules/BilibiliSearch/BACKEND.md for the endpoints the server has " +
          "to provide.", MessageType.Warning);
      }

      EditorGUILayout.Space();
      EditorGUILayout.HelpBox(
        "The panel has two tabs:\n" +
        "  • keyword search, pre-filled with " + SearchUrl + "\n" +
        "  • url input, pre-filled with " + PlayUrl + "\n" +
        "The url tab resets its box to that prefix on every click, so the player only appends the " +
        "bilibili link. YamaPlayer's own url input is left untouched - it stays available for other " +
        "sites (YouTube and friends).", MessageType.None);

      EditorGUILayout.Space();

      using (new EditorGUI.DisabledScope(!BackendConfigured))
      {
        if (GUILayout.Button("Generate Prefabs", GUILayout.Height(32f)))
        {
          Generate();
        }
      }
    }

    [MenuItem("Tools/YamaPlayer/Bilibili Search/Generate Prefabs", priority = 101)]
    public static void GenerateFromMenu()
    {
      GenerateWithDefaults();
    }

    private static void GenerateWithDefaults()
    {
      var window = CreateInstance<BilibiliSearchPanelSetup>();
      window._outputFolder = DefaultOutputFolder;
      window._targetPath = DefaultTargetPath;
      window._siblingIndex = -1;
      window._instantiateInScene = false;
      window._destroyAfterGeneration = true;
      window.Generate();
      if (_generationOwner != window) DestroyImmediate(window);
    }

    private static BilibiliSearchPanelSetup _generationOwner;
    private bool _destroyAfterGeneration;
    private double _generationDeadline;
    private static readonly Type[] ProgramTypes = {
      typeof(BilibiliSearch), typeof(BilibiliSearchService), typeof(BilibiliSearchResult),
      typeof(BilibiliSearchUI), typeof(BilibiliSearchResultAction), typeof(BilibiliResultList)
    };

    private void Generate()
    {
      if (_generationOwner != null)
      {
        Debug.LogWarning("[BilibiliSearch] Prefab generation is already waiting for UdonSharp.");
        return;
      }
      if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
      {
        Debug.LogError("[BilibiliSearch] Exit Play mode and wait for Unity C# compilation before generating prefabs.");
        return;
      }
      if (string.IsNullOrEmpty(_outputFolder))
      {
        Debug.LogError("[BilibiliSearch] Output folder is empty.");
        return;
      }

      if (!BackendConfigured)
      {
        Debug.LogError(
          "[BilibiliSearch] No backend configured. Open Tools > YamaPlayer > Bilibili Search Setup " +
          "and enter the address of your own bilibili player backend (for example " +
          "https://bili.example.com/player/). This package ships no server of its own; " +
          "Modules/BilibiliSearch/BACKEND.md describes what the server has to provide.");
        return;
      }

      // Never delete existing scene objects when regenerating assets.

      Directory.CreateDirectory(_outputFolder);
      AssetDatabase.Refresh();

      // Assets generated by an older version of this tool are moved, not recreated, so the
      // prefab keeps its guid and the scene instance that references it stays intact.
      MigrateLegacyAssets(_outputFolder);

      // Generated up front: importing a texture in the middle of building the hierarchy
      // would trigger an extra asset refresh while the temporary objects are alive.
      _assetFolder = _outputFolder;
      ResolveBilibiliIcon(_outputFolder);
      ResolvePanelFrameSprite(_outputFolder);
      // Same reason as the two above: the png assets of the overlay have to be imported as sprites
      // before the hierarchy is built.
      ResolveIcon(AuthorFileName, _outputFolder);
      ResolveIcon(AiIconDeepSeek, _outputFolder);
      ResolveIcon(AiIconCodex, _outputFolder);
      ResolveIcon(SocialIconVRChat, YamaPlayerImageFolder);
      ResolveIcon(SocialIconTwitter, YamaPlayerImageFolder);
      ResolveIcon(SocialIconGithub, YamaPlayerImageFolder);

      // UdonSharp only compiles behaviours whose assembly is registered as a U# assembly,
      // and UdonSharpUndo.AddComponent needs the program asset of every behaviour it creates.
      EnsureUdonSharpAssemblyDefinition();
      EnsureProgramAssets();

      // Creating those assets invalidates UdonSharp's assembly caches. Let the pending
      // imports settle so the behaviours built below can be compiled right away.
      AssetDatabase.Refresh();

      // Script upgrades are queued by the asset importer and run on EditorApplication.update.
      // CompileSync only compiles; it does not perform those upgrades on newly created assets.
      _generationOwner = this;
      _generationDeadline = EditorApplication.timeSinceStartup + 120;
      EditorApplication.update += WaitForProgramsAndGenerate;
      Debug.Log("[BilibiliSearch] Preparing UdonSharp programs; prefab generation will continue automatically.");
    }

    private static void WaitForProgramsAndGenerate()
    {
      var owner = _generationOwner;
      if (owner == null)
      {
        EditorApplication.update -= WaitForProgramsAndGenerate;
        return;
      }
      try
      {
        if (EditorApplication.timeSinceStartup > owner._generationDeadline)
          throw new InvalidOperationException("UdonSharp programs did not become ready within 120 seconds. Check the Console for script upgrade or compilation errors.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
          throw new InvalidOperationException("Prefab generation cancelled because Play mode started.");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorUtility.scriptCompilationFailed)
          throw new InvalidOperationException("Resolve Unity C# compilation errors before generating prefabs.");
        foreach (var type in ProgramTypes)
        {
          var asset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(type);
          if (asset == null) throw new InvalidOperationException("Missing UdonSharp program asset: " + type.Name);
          if (asset.ScriptVersion < UdonSharpProgramVersion.CurrentVersion) return;
        }
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
          throw new InvalidOperationException("UdonSharp compilation failed. Prefabs were not generated; see the compiler errors above.");
        foreach (var type in ProgramTypes)
        {
          var asset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(type);
          if (asset.CompiledVersion < UdonSharpProgramVersion.CurrentVersion)
            throw new InvalidOperationException("UdonSharp program did not compile: " + type.Name);
        }
        owner.BuildAndSavePrefabs();
      }
      catch (Exception exception) { Debug.LogException(exception); }
      // Returns while waiting leave the callback registered. Success and failure release it.
      EditorApplication.update -= WaitForProgramsAndGenerate;
      _generationOwner = null;
      if (owner._destroyAfterGeneration) DestroyImmediate(owner);
    }

    private void BuildAndSavePrefabs()
    {
      string panelPath = $"{_outputFolder}/{PanelPrefabName}.prefab";
      string modulePath = $"{_outputFolder}/{ModulePrefabName}.prefab";
      // Save over the existing assets so GUIDs and scene prefab links survive.
      GameObject panelInstance = null;
      GameObject panelPrefab;
      try
      {
        panelInstance = BuildPanel();
        SyncProxiesToUdon(panelInstance);
        panelPrefab = SaveFreshPrefab(panelInstance, panelPath);
      }
      finally { DiscardTemporary(panelInstance); }

      if (panelPrefab == null)
      {
        Debug.LogError($"[BilibiliSearch] Failed to save panel prefab at {panelPath}.");
        return;
      }

      GameObject moduleInstance = null;
      GameObject modulePrefab;
      try
      {
        moduleInstance = BuildModule(panelPrefab);
        SyncProxiesToUdon(moduleInstance);
        modulePrefab = SaveFreshPrefab(moduleInstance, modulePath);
      }
      finally { DiscardTemporary(moduleInstance); }

      if (modulePrefab == null)
      {
        Debug.LogError($"[BilibiliSearch] Failed to save module prefab at {modulePath}.");
        return;
      }

      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      // The module translation file has to be re-merged before ModuleManager draws the module,
      // otherwise it keeps showing "module.bilibilisearch.name" until the next domain reload.
      EditorLocalization.ReloadTranslations();

      Debug.Log($"[BilibiliSearch] Generated {panelPath} and {modulePath}.");

      if (_instantiateInScene)
      {
        var instance = PrefabUtility.InstantiatePrefab(modulePrefab) as GameObject;
        if (instance != null)
        {
          instance.name = ModulePrefabName;
          Undo.RegisterCreatedObjectUndo(instance, "Create Bilibili Search Module");
          Selection.activeGameObject = instance;
        }
      }

      // Older versions wired a click handler into YamaPlayer's own url boxes. The panel has its own
      // url page now, so those handlers are dropped again (a no-op when there are none).
      BilibiliSearchRepair.RemoveLegacyUrlBoxWiring();

      Selection.activeObject = modulePrefab;
      EditorGUIUtility.PingObject(modulePrefab);
    }

    /// <summary>
    /// Moves assets generated into Assets/ by an older version of this tool into the module
    /// folder. AssetDatabase.MoveAsset keeps the guid, so a prefab instance that already
    /// lives in the open scene keeps working after the move.
    /// </summary>
    private static void MigrateLegacyAssets(string outputFolder)
    {
      if (string.IsNullOrEmpty(outputFolder) || outputFolder == LegacyOutputFolder) return;
      if (!AssetDatabase.IsValidFolder(LegacyOutputFolder)) return;

      string[] names = { PanelPrefabName + ".prefab", ModulePrefabName + ".prefab", IconFileName };
      for (int i = 0; i < names.Length; i++)
      {
        string from = LegacyOutputFolder + "/" + names[i];
        string to = outputFolder + "/" + names[i];
        if (!File.Exists(from) || File.Exists(to)) continue;

        string error = AssetDatabase.MoveAsset(from, to);
        if (string.IsNullOrEmpty(error)) Debug.Log($"[BilibiliSearch] Moved {from} to {to}.");
        else Debug.LogWarning($"[BilibiliSearch] Could not move {from}: {error}");
      }
    }

    private static GameObject SaveFreshPrefab(GameObject root, string path)
    {
      // A corrupt existing prefab can retain stale proxy components during matching/overwrite.
      // Serialize to a new asset first, then replace only the prefab bytes, keeping its GUID.
      string staging = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(path) + "/BilibiliSearch-Staging.prefab");
      try
      {
        PrefabUtility.SaveAsPrefabAsset(root, staging);
        // Reimporting a selected prefab can leave Unity's Inspector tracking removed components.
        // Generate selects the finished module again after both assets have been saved.
        if (AssetDatabase.GetAssetPath(Selection.activeObject) == path)
        {
          Selection.activeObject = null;
          ActiveEditorTracker.sharedTracker.ForceRebuild();
        }
        File.Copy(staging, path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
      }
      finally { AssetDatabase.DeleteAsset(staging); }
    }

    /// <summary>
    /// Creates the UdonSharpAssemblyDefinition asset for this module's assembly.
    /// UdonSharp refuses to compile a behaviour whose assembly is not flagged as a U# assembly
    /// ("does not belong to a U# assembly"). Every other module in this package ships one,
    /// for example Modules/PitchShifter/Yamadev.YamaStream.Modules.PitchShifter.asset with
    /// sourceAssembly pointing at its asmdef guid.
    /// </summary>
    private static void EnsureUdonSharpAssemblyDefinition()
    {
      const string sourceAsmdefName = "Yamadev.YamaStream.Modules.BilibiliSearch.asmdef";
      const string sourceAsmdefPath = "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/" + sourceAsmdefName;
      const string assetPath = "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/" +
                               "Yamadev.YamaStream.Modules.BilibiliSearch.asset";

      if (AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(assetPath) != null) return;

      // Loaded as a plain Object on purpose: AssemblyDefinitionAsset lives in
      // UnityEditorInternal, which is not worth referencing just for a type check.
      UnityEngine.Object asmdef = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sourceAsmdefPath);
      if (asmdef == null)
      {
        Debug.LogError($"[BilibiliSearch] Cannot find '{sourceAsmdefPath}' to register as a U# assembly.");
        return;
      }

      UdonSharpAssemblyDefinition definition = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();

      // Unity cannot create assets inside Packages, so create in Assets and move it.
      string tempPath = $"Assets/pkgUsharp_{Guid.NewGuid():N}.asset";
      AssetDatabase.CreateAsset(definition, tempPath);

      var serialized = new SerializedObject(definition);
      SerializedProperty sourceAssemblyProperty = serialized.FindProperty("sourceAssembly");
      if (sourceAssemblyProperty == null)
      {
        Debug.LogError("[BilibiliSearch] UdonSharpAssemblyDefinition has no 'sourceAssembly' field.");
        AssetDatabase.DeleteAsset(tempPath);
        return;
      }
      sourceAssemblyProperty.objectReferenceValue = asmdef;
      serialized.ApplyModifiedPropertiesWithoutUndo();

      string moveError = AssetDatabase.MoveAsset(tempPath, assetPath);
      if (!string.IsNullOrEmpty(moveError))
      {
        AssetDatabase.DeleteAsset(tempPath);
        Debug.LogError($"[BilibiliSearch] Failed to create '{assetPath}': {moveError}");
        return;
      }

      Debug.Log($"[BilibiliSearch] Registered U# assembly: {assetPath}.");
    }

    /// <summary>
    /// Makes sure every UdonSharp script in this module has its UdonSharpProgramAsset.
    /// UdonSharp stores that asset next to the source file (BilibiliSearchUI.cs ->
    /// BilibiliSearchUI.asset) and looks it up by behaviour type. Scripts that are shipped
    /// inside a package never went through the "Create U# Script" flow, so the asset is
    /// missing and UdonSharpUndo.AddComponent throws on a null program asset.
    /// </summary>
    private static void EnsureProgramAssets()
    {
      EnsureProgramAsset(typeof(BilibiliSearch),
        "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliSearch.cs");
      EnsureProgramAsset(typeof(BilibiliSearchService),
        "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliSearchService.cs");
      EnsureProgramAsset(typeof(BilibiliSearchResult),
        "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliSearchResult.cs");
      EnsureProgramAsset(typeof(BilibiliSearchUI),
        "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliSearchUI.cs");
      EnsureProgramAsset(typeof(BilibiliSearchResultAction),
        "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliSearchResultAction.cs");
      EnsureProgramAsset(typeof(BilibiliResultList),
        "Packages/net.kwxxw.yama-stream/Modules/BilibiliSearch/BilibiliResultList.cs");
    }

    private static void EnsureProgramAsset(Type behaviourType, string fallbackScriptPath)
    {
      var existing = UdonSharpEditorUtility.GetUdonSharpProgramAsset(behaviourType);
      if (existing != null)
      {
        if (existing.ScriptVersion < UdonSharpProgramVersion.CurrentVersion)
          AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(existing), ImportAssetOptions.ForceUpdate);
        return;
      }

      MonoScript script = FindMonoScript(behaviourType) ?? AssetDatabase.LoadAssetAtPath<MonoScript>(fallbackScriptPath);
      if (script == null)
      {
        Debug.LogError($"[BilibiliSearch] Cannot find the source script for {behaviourType.Name}.");
        return;
      }

      string scriptPath = AssetDatabase.GetAssetPath(script);
      string assetPath = Path.ChangeExtension(scriptPath, ".asset");

      UdonSharpProgramAsset programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
      if (programAsset == null)
      {
        programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        // Assign before CreateAsset/MoveAsset so UdonSharp's importer can queue the upgrade.
        programAsset.sourceCsScript = script;

        // Unity cannot create assets inside Packages, so create in Assets and move it.
        if (scriptPath.StartsWith("Packages/"))
        {
          string tempPath = $"Assets/pkgUdon_{Guid.NewGuid():N}.asset";
          AssetDatabase.CreateAsset(programAsset, tempPath);
          string moveError = AssetDatabase.MoveAsset(tempPath, assetPath);
          if (!string.IsNullOrEmpty(moveError))
          {
            AssetDatabase.DeleteAsset(tempPath);
            Debug.LogError($"[BilibiliSearch] Failed to create '{assetPath}': {moveError}");
            return;
          }
        }
        else
        {
          AssetDatabase.CreateAsset(programAsset, assetPath);
        }

        Debug.Log($"[BilibiliSearch] Created program asset {assetPath}.");
      }

      programAsset.sourceCsScript = script;
      EditorUtility.SetDirty(programAsset);
      AssetDatabase.SaveAssets();
      // Also repairs assets left behind by an earlier failed first-time generation.
      AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
    }

    private static MonoScript FindMonoScript(Type behaviourType)
    {
      string[] guids = AssetDatabase.FindAssets($"{behaviourType.Name} t:MonoScript");
      foreach (string guid in guids)
      {
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
        if (script != null && script.GetClass() == behaviourType) return script;
      }
      return null;
    }

    /// <summary>
    /// Pushes every proxy behaviour's serialized state into its backing UdonBehaviour.
    /// Without this the saved prefab keeps an empty public variable table, which makes
    /// UdonSharp report "Program asset ... is not valid" when Play mode starts.
    /// </summary>
    private static void SyncProxiesToUdon(GameObject root)
    {
      if (root == null) return;

      UdonSharpBehaviour[] proxies = root.GetComponentsInChildren<UdonSharpBehaviour>(true);
      foreach (UdonSharpBehaviour proxy in proxies)
      {
        if (proxy == null) continue;
        // Fail immediately instead of saving a half-serialized, unusable prefab.
        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        if (UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy) == null || UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy).gameObject != proxy.gameObject)
          throw new InvalidOperationException("Missing backing UdonBehaviour: " + proxy.name);
      }
    }

    #region Module prefab

    private GameObject BuildModule(GameObject panelPrefab)
    {
      var root = new GameObject(ModulePrefabName);

      var definition = root.AddComponent<YamaPlayerModuleDefinition>();
      definition.moduleName = "BilibiliSearch";
      definition.moduleDescription = "Bilibili video search.";
      definition.version = Version;
      definition.allowMultiple = false;
      definition.noNeedSetUp = true;
      definition.moduleNameTranslationKey = "module.bilibilisearch.name";
      definition.moduleDescriptionTranslationKey = "module.bilibilisearch.description";
      definition.editorTranslationFile = FindAssetByName<TextAsset>("Localization.Editor.json", "BilibiliSearch");
      definition.playerTranslationFile = FindAssetByName<TextAsset>("Localization.Runtime.json", "BilibiliSearch");
      definition.uiSlots = new ModuleUISlot[2];

      var serviceHost = new GameObject("Service");
      serviceHost.transform.SetParent(root.transform, false);
      var service = UdonSharpUndo.AddComponent<BilibiliSearchService>(serviceHost);

      var serviceSerialized = new SerializedObject(service);
      SerializedProperty baseUrlProperty = serviceSerialized.FindProperty("_baseUrl");
      if (baseUrlProperty != null)
      {
        // VRCUrl is a custom serializable type, assigning stringValue on it fails with
        // "type is not a supported string value". Its inner 'url' field holds the text.
        SerializedProperty urlProperty = baseUrlProperty.FindPropertyRelative("url");
        if (urlProperty != null) urlProperty.stringValue = BackendUrl;
        else Debug.LogWarning("[BilibiliSearch] Could not pre-fill the base url; set it manually on the Service component.");
      }
      serviceSerialized.ApplyModifiedPropertiesWithoutUndo();
      BilibiliSearchDirectSetup.Bake(service);

      var resultHost = new GameObject("SearchResult");
      resultHost.transform.SetParent(root.transform, false);
      var result = UdonSharpUndo.AddComponent<BilibiliSearchResult>(resultHost);

      // The panel lives inside the module so the module is self contained, and it is copied as
      // plain objects instead of a nested prefab instance. YamaPlayer's own modules reference
      // their ui slot content without a guid (same prefab), which lets the build remap it to a
      // scene object and destroy it. A cross-prefab reference stays an asset, and destroying an
      // asset is refused by Unity ("Destroying assets is not permitted to avoid data loss").
      // Build a fresh hierarchy: UdonSharp hidden backing references must not point at a prefab asset.
      var panel = BuildPanel();
      panel.transform.SetParent(root.transform, false);
      var uiBehaviour = panel.GetComponentInChildren<BilibiliSearchUI>(true);

      // Second slot: the bilibili icon in the main page icon column. Its click event points at
      // the panel's UdonBehaviour, which sits in the first slot, so YamaPlayer's build remaps
      // the reference to the injected panel (BuildObjectMapping spans all slots of a module).
      var launcher = BuildLauncher(uiBehaviour);
      launcher.transform.SetParent(root.transform, false);

      definition.uiSlots[0] = new ModuleUISlot
      {
        targetPath = _targetPath,
        content = panel,
        siblingIndex = _siblingIndex,
      };
      definition.uiSlots[1] = new ModuleUISlot
      {
        targetPath = LauncherTargetPath,
        content = launcher,
        // 0 puts the icon on top of the url input icon, which is the first child of the column.
        siblingIndex = 0,
      };

      var module = UdonSharpUndo.AddComponent<BilibiliSearch>(root);
      var serialized = new SerializedObject(module);
      serialized.FindProperty("_service").objectReferenceValue = service;
      serialized.FindProperty("_result").objectReferenceValue = result;
      serialized.FindProperty("_uiBehaviour").objectReferenceValue = uiBehaviour;
      serialized.ApplyModifiedPropertiesWithoutUndo();
      var panelState = new SerializedObject(uiBehaviour);
      panelState.FindProperty("_search").objectReferenceValue = module;
      panelState.FindProperty("_result").objectReferenceValue = result;
      panelState.FindProperty("_service").objectReferenceValue = service;
      SerializedProperty defaultSearchUrl = panelState.FindProperty("_defaultSearchUrl");
      if (defaultSearchUrl != null)
      {
        // Same reason as _baseUrl above: VRCUrl stores its text in a nested 'url' field.
        SerializedProperty urlProperty = defaultSearchUrl.FindPropertyRelative("url");
        if (urlProperty != null) urlProperty.stringValue = SearchUrl;
        else Debug.LogWarning("[BilibiliSearch] Could not pre-fill the search url; set it manually on the panel.");
      }
      SerializedProperty defaultPlayUrl = panelState.FindProperty("_defaultPlayUrl");
      if (defaultPlayUrl != null)
      {
        SerializedProperty urlProperty = defaultPlayUrl.FindPropertyRelative("url");
        if (urlProperty != null) urlProperty.stringValue = PlayUrl;
        else Debug.LogWarning("[BilibiliSearch] Could not pre-fill the player url; set it manually on the panel.");
      }
      panelState.ApplyModifiedPropertiesWithoutUndo();

      return root;
    }

    /// <summary>
    /// Finds an asset by file name, preferring a path that contains the given folder hint.
    /// Keeps the generated prefab valid no matter where the package is installed.
    /// </summary>
    private static T FindAssetByName<T>(string fileName, string pathHint) where T : UnityEngine.Object
    {
      string[] guids = AssetDatabase.FindAssets($"{Path.GetFileNameWithoutExtension(fileName)} t:TextAsset");
      string fallback = null;

      foreach (string guid in guids)
      {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (Path.GetFileName(path) != fileName) continue;
        if (fallback == null) fallback = path;
        if (path.Replace('\\', '/').Contains(pathHint)) return AssetDatabase.LoadAssetAtPath<T>(path);
      }

      return fallback == null ? null : AssetDatabase.LoadAssetAtPath<T>(fallback);
    }

    #endregion

    #region Panel prefab

    private GameObject BuildPanel()
    {
      // Root stays active so the launcher button remains usable, the panel itself is hidden by default.
      var root = new GameObject(PanelPrefabName, typeof(RectTransform));
      var rootRect = (RectTransform)root.transform;
      Stretch(rootRect);

      var rootLayout = root.AddComponent<LayoutElement>();
      rootLayout.flexibleWidth = 1f;
      rootLayout.flexibleHeight = 1f;
      rootLayout.minWidth = 100f;
      rootLayout.minHeight = 100f;

      // UdonSharpUndo is required: plain AddComponent skips the proxy/UdonBehaviour wiring,
      // which leaves the component without a valid program asset.
      var uiBehaviour = UdonSharpUndo.AddComponent<BilibiliSearchUI>(root);

      // Panel background, this is the object that ShowPanel toggles.
      var panel = NewRect("Panel", rootRect);
      Stretch(panel);
      AddImage(panel.gameObject, new Color(0.05f, 0.05f, 0.07f, 0.94f));

      // Content
      var content = NewRect("Content", panel);
      content.anchorMin = new Vector2(0f, 0f);
      content.anchorMax = new Vector2(1f, 1f);
      content.offsetMin = new Vector2(120f, 100f);
      // Small top inset: the toolbar and the search row are kept compact so the result list
      // gets as much room as possible.
      content.offsetMax = new Vector2(-120f, -56f);
      var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
      layout.childAlignment = TextAnchor.UpperCenter;
      layout.spacing = 8f;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;

      // Top bar. The two tabs sit here, left of the version button, instead of in a row of their
      // own: vertical room is what the result list needs, and the panel title was dropped for the
      // same reason. The url page comes first because it is the default one.
      var topBar = NewLayoutRow("TopBar", content, 36f);
      var tabUrlButton = NewButton("TabUrlButton", topBar, "TabUrlButtonText", "module.bilibilisearch.tab.url", uiBehaviour, nameof(BilibiliSearchUI.ShowUrlTab), 18, ColorType.Primary);
      SetFixedWidth((RectTransform)tabUrlButton.transform, 220f);
      var tabSearchButton = NewButton("TabSearchButton", topBar, "TabSearchButtonText", "module.bilibilisearch.tab.search", uiBehaviour, nameof(BilibiliSearchUI.ShowSearchTab), 18, ColorType.Primary);
      SetFixedWidth((RectTransform)tabSearchButton.transform, 220f);

      // Version button, right of the tabs. The label is a literal: BiliText returns an unknown key
      // unchanged, and the version string is deliberately not translated.
      var versionButton = NewButton("VersionButton", topBar, "VersionButtonText", "v" + Version, uiBehaviour, nameof(BilibiliSearchUI.OpenVersionPanel), 18);
      SetFixedWidth((RectTransform)versionButton.transform, 104f);

      var status = NewText("StatusText", topBar, 20, TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.78f));
      AddFlexibleWidth(status.rectTransform);
      var pageText = NewText("PageText", topBar, 20, TextAnchor.MiddleRight, new Color(0.75f, 0.75f, 0.78f));
      SetFixedWidth(pageText.rectTransform, 180f);

      // Page one: keyword search. Both pages are columns of their own with flexible height, so the
      // visible one gets all the leftover room of the panel.
      var searchTab = NewColumn("SearchTab", content);
      var searchRow = NewLayoutRow("SearchRow", searchTab, 48f);
      var searchInput = NewVRCUrlInputField("SearchInput", searchRow, "SearchPlaceholder", BiliText.Get("module.bilibilisearch.placeholder"), SearchUrl, uiBehaviour, nameof(BilibiliSearchUI.ResetSearchInput));
      AddFlexibleWidth((RectTransform)searchInput.transform);

      var searchButton = NewButton("SearchButton", searchRow, "SearchButtonText", "button.search", uiBehaviour, nameof(BilibiliSearchUI.SearchByInput), 20, ColorType.Primary);
      SetFixedWidth((RectTransform)searchButton.transform, 110f);
      var previousButton = NewButton("PreviousButton", searchRow, "PreviousButtonText", "button.previousPage", uiBehaviour, nameof(BilibiliSearchUI.PreviousPage));
      SetFixedWidth((RectTransform)previousButton.transform, 110f);

      var nextButton = NewButton("NextButton", searchRow, "NextButtonText", "button.nextPage", uiBehaviour, nameof(BilibiliSearchUI.NextPage));
      SetFixedWidth((RectTransform)nextButton.transform, 110f);

      // Same colour as the search button: the two corners of the row are the panel's main actions.
      var closeButton = NewButton("CloseButton", searchRow, "CloseButtonText", "button.close", uiBehaviour, nameof(BilibiliSearchUI.ClosePanel), 20, ColorType.Primary);
      SetFixedWidth((RectTransform)closeButton.transform, 96f);

      // Results. flexibleHeight makes this row absorb all leftover space of the content
      // column, otherwise the layout would spread the surplus over every row and blow the
      // search bar up to several times its intended height.
      var results = NewScrollView("ResultsScroll", searchTab, uiBehaviour);
      var resultsLayout = results.gameObject.AddComponent<LayoutElement>();
      resultsLayout.minHeight = 300f;
      resultsLayout.flexibleHeight = 1f;
      // The list is activated by the panel one frame after its page became visible, so that the
      // result list inside sizes its row pool from a viewport that already has a height. It is
      // still authored as part of the page, only its active flag starts off.
      results.gameObject.SetActive(false);

      // Page two: the url page, the one the panel opens with. The box is pre-filled with the
      // resolver prefix and reset on every click (see BilibiliSearchUI.ResetUrlInput), the player
      // only appends the bilibili link. Close sits next to play: when a video is already running
      // the new track only goes to the queue and the panel stays open, so that case needs a way
      // back to the main page. The two hints below explain both pages.
      var urlTab = NewColumn("UrlTab", content);
      var urlRow = NewLayoutRow("UrlRow", urlTab, 48f);
      var urlInput = NewVRCUrlInputField("UrlInput", urlRow, "UrlPlaceholder", BiliText.Get("module.bilibilisearch.urlPlaceholder"), PlayUrl, uiBehaviour, nameof(BilibiliSearchUI.ResetUrlInput));
      AddFlexibleWidth((RectTransform)urlInput.transform);

      var playUrlButton = NewButton("PlayUrlButton", urlRow, "PlayUrlButtonText", "module.bilibilisearch.urlPlay", uiBehaviour, nameof(BilibiliSearchUI.PlayUrlInput), 20, ColorType.Primary);
      SetFixedWidth((RectTransform)playUrlButton.transform, 140f);

      // Same width as play: the two labels are the same length, and a pair of buttons of different
      // lengths next to each other looks like a mistake.
      var closeUrlButton = NewButton("CloseUrlButton", urlRow, "CloseUrlButtonText", "button.close", uiBehaviour, nameof(BilibiliSearchUI.ClosePanel), 20, ColorType.Primary);
      SetFixedWidth((RectTransform)closeUrlButton.transform, 140f);

      // The two hints take exactly the height their text needs and sit under the input row, with one
      // blank line above the first one and one between the two groups; the spacer below them absorbs
      // the rest of the page. Every group is a heading line plus its explanation, and the headings
      // use the colour of the buttons so they read as labels.
      urlTab.GetComponent<VerticalLayoutGroup>().spacing = 0f;

      var topGap = NewRect("HintTopGap", urlTab);
      var topGapLayout = topGap.gameObject.AddComponent<LayoutElement>();
      topGapLayout.minHeight = HintLineGap;
      topGapLayout.preferredHeight = HintLineGap;
      topGapLayout.flexibleHeight = 0f;

      var urlLabel = NewText("UrlLabel", urlTab, 26, TextAnchor.UpperLeft, PrimaryColorDefault);
      urlLabel.text = BiliText.Get("module.bilibilisearch.urlLabel");
      SetColorRole(urlLabel.gameObject, ColorType.Primary);

      var urlHint = NewText("UrlHint", urlTab, 22, TextAnchor.UpperLeft, new Color(0.72f, 0.72f, 0.76f));
      urlHint.text = BiliText.Get("module.bilibilisearch.urlHint");

      var hintGap = NewRect("HintGap", urlTab);
      var hintGapLayout = hintGap.gameObject.AddComponent<LayoutElement>();
      hintGapLayout.minHeight = HintLineGap;
      hintGapLayout.preferredHeight = HintLineGap;
      hintGapLayout.flexibleHeight = 0f;

      var searchLabel = NewText("SearchLabel", urlTab, 26, TextAnchor.UpperLeft, PrimaryColorDefault);
      searchLabel.text = BiliText.Get("module.bilibilisearch.searchLabel");
      SetColorRole(searchLabel.gameObject, ColorType.Primary);

      var searchHint = NewText("SearchHint", urlTab, 22, TextAnchor.UpperLeft, new Color(0.72f, 0.72f, 0.76f));
      searchHint.text = BiliText.Get("module.bilibilisearch.searchHint");

      var hintSpacer = NewRect("HintSpacer", urlTab);
      var hintSpacerLayout = hintSpacer.gameObject.AddComponent<LayoutElement>();
      hintSpacerLayout.minHeight = 0f;
      hintSpacerLayout.preferredHeight = 0f;
      hintSpacerLayout.flexibleHeight = 1f;

      // The panel opens on the url page, so the keyword search starts hidden.
      searchTab.gameObject.SetActive(false);
      pageText.gameObject.SetActive(false);

      var linkDialog = NewRect("LinkDialog", rootRect);
      Stretch(linkDialog);
      AddImage(linkDialog.gameObject, new Color(.04f, .04f, .06f, .98f));
      var hint = NewText("LinkHint", linkDialog, 26, TextAnchor.MiddleLeft, Color.white);
      PinTopLeft(hint.rectTransform, 120, -100, 1450, 100);
      hint.text = BiliText.Get("module.bilibilisearch.hint.copy");
      var copyField = NewInputField("CopyField", linkDialog, 2048, "CopyPlaceholder", BiliText.Get("module.bilibilisearch.copyField"));
      PinTopLeft((RectTransform)copyField.transform, 120, -220, 1450, 64);
      copyField.readOnly = true;
      var confirmInput = NewVRCUrlInputField("ConfirmInput", linkDialog, "ConfirmPlaceholder", BiliText.Get("module.bilibilisearch.confirmPlaceholder"), "");
      PinTopLeft((RectTransform)confirmInput.transform, 120, -310, 1450, 64);
      confirmInput.SetUrl(VRCUrl.Empty);
      var confirm = NewButton("ConfirmLink", linkDialog, "ConfirmLinkText", "module.bilibilisearch.confirm", uiBehaviour, nameof(BilibiliSearchUI.ConfirmLink), 20, ColorType.Primary);
      PinTopLeft((RectTransform)confirm.transform, 1040, -430, 320, 64);
      var cancel = NewButton("CancelLink", linkDialog, "CancelLinkText", "module.bilibilisearch.cancel", uiBehaviour, nameof(BilibiliSearchUI.CloseLink));
      PinTopLeft((RectTransform)cancel.transform, 120, -430, 260, 64);
      linkDialog.SetAsLastSibling();
      linkDialog.gameObject.SetActive(false);

      var versionPanel = BuildVersionPanel(rootRect, uiBehaviour);

      // Loading indicator
      var loading = NewText("Loading", rootRect, 30, TextAnchor.MiddleCenter, Color.white);
      loading.text = "...";
      var loadingRect = loading.rectTransform;
      loadingRect.anchorMin = new Vector2(0.5f, 0.5f);
      loadingRect.anchorMax = new Vector2(0.5f, 0.5f);
      loadingRect.pivot = new Vector2(0.5f, 0.5f);
      loadingRect.anchoredPosition = Vector2.zero;
      loadingRect.sizeDelta = new Vector2(300f, 80f);
      loading.gameObject.SetActive(false);

      // Wire the panel fields.
      var serialized = new SerializedObject(uiBehaviour);
      serialized.FindProperty("_panelRoot").objectReferenceValue = panel.gameObject;
      serialized.FindProperty("_searchInput").objectReferenceValue = searchInput;
      serialized.FindProperty("_pageText").objectReferenceValue = pageText;
      serialized.FindProperty("_statusText").objectReferenceValue = status;
      serialized.FindProperty("_resultsScroll").objectReferenceValue = results;
      serialized.FindProperty("_copyField").objectReferenceValue = copyField;
      serialized.FindProperty("_linkDialog").objectReferenceValue = linkDialog.gameObject;
      serialized.FindProperty("_confirmInput").objectReferenceValue = confirmInput;
      serialized.FindProperty("_linkHint").objectReferenceValue = hint;
      serialized.FindProperty("_confirmButton").objectReferenceValue = confirm.gameObject;
      serialized.FindProperty("_loadingIndicator").objectReferenceValue = loading.gameObject;
      serialized.FindProperty("_versionPanel").objectReferenceValue = versionPanel.gameObject;
      // Tabs: the keyword search page and the url page.
      serialized.FindProperty("_searchTab").objectReferenceValue = searchTab.gameObject;
      serialized.FindProperty("_urlTab").objectReferenceValue = urlTab.gameObject;
      serialized.FindProperty("_urlInput").objectReferenceValue = urlInput;
      serialized.FindProperty("_urlHint").objectReferenceValue = urlHint;
      serialized.FindProperty("_searchHint").objectReferenceValue = searchHint;
      // _defaultPlayUrl is a VRCUrl, see the module prefab below: its text lives in a nested field.
      SerializedProperty defaultPlayUrl = serialized.FindProperty("_defaultPlayUrl");
      if (defaultPlayUrl != null)
      {
        SerializedProperty urlProperty = defaultPlayUrl.FindPropertyRelative("url");
        if (urlProperty != null) urlProperty.stringValue = PlayUrl;
      }
      // YamaPlayer's language switch pushes the language font onto every text below the
      // UIController; the panel puts this font back afterwards. See RestorePanelFont.
      serialized.FindProperty("_panelFont").objectReferenceValue = ResolvePanelFont();
      serialized.ApplyModifiedPropertiesWithoutUndo();

      linkDialog.SetAsLastSibling();
      // The panel starts hidden, the bilibili icon in the main page icon column opens it.
      panel.gameObject.SetActive(false);

      return root;
    }

    #endregion

    #region UI helpers

    private static RectTransform NewRect(string name, Transform parent)
    {
      var go = new GameObject(name, typeof(RectTransform));
      var rect = (RectTransform)go.transform;
      rect.SetParent(parent, false);
      return rect;
    }

    private static RectTransform NewLayoutRow(string name, Transform parent, float height)
    {
      var rect = NewRect(name, parent);
      var horizontal = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
      horizontal.childAlignment = TextAnchor.MiddleLeft;
      horizontal.spacing = 12f;
      horizontal.childControlWidth = true;
      horizontal.childControlHeight = true;
      horizontal.childForceExpandWidth = false;
      horizontal.childForceExpandHeight = true;
      var layout = rect.gameObject.AddComponent<LayoutElement>();
      layout.minHeight = height;
      layout.preferredHeight = height;
      // This has to be 0, not left at the default -1. Because the row force-expands its own
      // children in height, its HorizontalLayoutGroup reports flexibleHeight 1 (LayoutGroup
      // is an ILayoutElement too), and LayoutUtility.GetFlexibleHeight only prefers the
      // LayoutElement when its value is not negative. Left at -1 the row counts as flexible
      // for the parent, so it swallowed a share of the leftover space and rendered several
      // times taller than 'height'.
      layout.flexibleHeight = 0f;
      return rect;
    }

    /// <summary>Height of one blank line of the url page's hint text.</summary>
    private const float HintLineGap = 26f;

    /// <summary>
    /// A column that stacks its children and soaks up the leftover height of its parent, which is
    /// what a tab page needs: the toolbar and the input row keep their height, the list or the hint
    /// below them gets the rest.
    /// </summary>
    private static RectTransform NewColumn(string name, Transform parent)
    {
      var rect = NewRect(name, parent);
      var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
      layout.childAlignment = TextAnchor.UpperCenter;
      layout.spacing = 8f;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;
      var element = rect.gameObject.AddComponent<LayoutElement>();
      element.minHeight = 200f;
      element.flexibleHeight = 1f;
      return rect;
    }

    private static void Stretch(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
      rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static Image AddImage(GameObject target, Color color)
    {
      var image = target.GetComponent<Image>();
      if (image == null) image = target.AddComponent<Image>();
      image.color = color;
      image.raycastTarget = true;
      return image;
    }

    private static Text NewText(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
    {
      var rect = NewRect(name, parent);
      var text = rect.gameObject.AddComponent<Text>();
      text.font = ResolvePanelFont();
      text.fontSize = fontSize;
      text.alignment = alignment;
      text.color = color;
      text.horizontalOverflow = HorizontalWrapMode.Wrap;
      text.verticalOverflow = VerticalWrapMode.Truncate;
      text.raycastTarget = false;
      text.supportRichText = false;
      return text;
    }

    private static void SetFixedWidth(RectTransform rect, float width)
    {
      var layout = rect.gameObject.GetComponent<LayoutElement>();
      if (layout == null) layout = rect.gameObject.AddComponent<LayoutElement>();
      layout.minWidth = width;
      layout.preferredWidth = width;
      layout.flexibleWidth = 0f;
    }

    private static void AddFlexibleWidth(RectTransform rect)
    {
      var layout = rect.gameObject.GetComponent<LayoutElement>();
      if (layout == null) layout = rect.gameObject.AddComponent<LayoutElement>();
      layout.flexibleWidth = 1f;
      layout.minWidth = 120f;
    }

    private static InputField NewInputField(string name, Transform parent, int characterLimit, string placeholderName, string placeholderText)
    {
      RectTransform viewport;
      Text text;
      Text placeholder;
      var input = NewInputFieldBase(name, parent, placeholderName, out viewport, out text, out placeholder);
      input.characterLimit = characterLimit;
      input.lineType = InputField.LineType.SingleLine;
      input.contentType = InputField.ContentType.Standard;
      placeholder.text = placeholderText;
      return input;
    }

    /// <summary>Shared InputField background / text / placeholder hierarchy.</summary>
    private static InputField NewInputFieldBase(string name, Transform parent, string placeholderName, out RectTransform viewport, out Text text, out Text placeholder)
    {
      var rect = NewRect(name, parent);
      var background = AddPill(rect.gameObject, SecondaryColorDefault, ColorType.Secondary);

      viewport = NewRect("Viewport", rect);
      Stretch(viewport);
      viewport.offsetMin = new Vector2(20f, 4f);
      viewport.offsetMax = new Vector2(-20f, -4f);

      text = NewText("Text", viewport, 22, TextAnchor.MiddleLeft, Color.white);
      Stretch(text.rectTransform);
      text.supportRichText = false;

      // Named per input: the panel resolves labels by name, and "Placeholder" alone would
      // always find the first one in the hierarchy.
      placeholder = NewText(placeholderName, viewport, 22, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.4f));
      Stretch(placeholder.rectTransform);
      placeholder.fontStyle = FontStyle.Italic;

      var input = rect.gameObject.AddComponent<InputField>();
      input.targetGraphic = background;
      input.textComponent = text;
      input.placeholder = placeholder;
      input.caretColor = Color.white;
      input.selectionColor = new Color(0.94f, 0.38f, 0.57f, 0.5f);
      input.customCaretColor = true;

      return input;
    }

    /// <summary>
    /// The url input VRChat provides for player authored urls. It mirrors the InputField
    /// layout (textComponent / placeholder / targetGraphic) but is a separate component with
    /// its own nested enums, and it cannot live on the same object as a plain InputField.
    /// </summary>
    private static VRCUrlInputField NewVRCUrlInputField(string name, Transform parent, string placeholderName, string placeholderText, string prefillText, UdonSharpBehaviour clickTarget = null, string clickEvent = null)
    {
      var rect = NewRect(name, parent);
      var background = AddPill(rect.gameObject, SecondaryColorDefault, ColorType.Secondary);

      var viewport = NewRect("Viewport", rect);
      Stretch(viewport);
      viewport.offsetMin = new Vector2(20f, 4f);
      viewport.offsetMax = new Vector2(-20f, -4f);

      var text = NewText("Text", viewport, 22, TextAnchor.MiddleLeft, Color.white);
      Stretch(text.rectTransform);
      text.supportRichText = false;

      var placeholder = NewText(placeholderName, viewport, 22, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.4f));
      Stretch(placeholder.rectTransform);
      placeholder.fontStyle = FontStyle.Italic;
      placeholder.text = placeholderText;

      var urlField = rect.gameObject.AddComponent<VRCUrlInputField>();
      if (urlField == null)
      {
        Debug.LogError("[BilibiliSearch] VRCUrlInputField could not be added to the search box.");
        return null;
      }

      urlField.targetGraphic = background;
      urlField.textComponent = text;
      urlField.placeholder = placeholder;
      urlField.caretColor = Color.white;
      urlField.selectionColor = new Color(0.94f, 0.38f, 0.57f, 0.5f);
      urlField.customCaretColor = true;
      urlField.characterLimit = 2048;

      // Pre-filled so the player only appends the keyword. VRCUrl can only be built from a
      // string outside of Udon, which is why the value is authored here and serialized.
      // The runtime clears it again (VRCUrlInputField.Awake), so the panel reapplies
      // _defaultSearchUrl whenever it is opened.
      if (string.IsNullOrEmpty(prefillText))
      {
        urlField.SetUrl(VRCUrl.Empty);
      }
      else
      {
        urlField.SetUrl(new VRCUrl(prefillText));
        // SetUrl goes through UpdateLabel, which clips the text against a rect that has no
        // layout yet while the prefab is being authored. Writing the label again keeps the
        // prefab preview readable.
        text.text = prefillText;
      }

      // EventTrigger instead of Button.onClick: the panel has to react before the input field
      // activates itself, so that the url box the VRChat keyboard opens with already holds the
      // default. This is the same way YamaPlayer animates its own url input.
      if (clickTarget != null && !string.IsNullOrEmpty(clickEvent))
      {
        var trigger = rect.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerDown;
        UnityEventTools.AddStringPersistentListener(entry.callback,
          UdonSharpEditorUtility.GetBackingUdonBehaviour(clickTarget).SendCustomEvent, clickEvent);
        trigger.triggers.Add(entry);
      }

      return urlField;
    }

    /// <summary>
    /// The bilibili icon in the main page icon column. It is a plain button, not a toggle,
    /// so it does not join the page ToggleGroup that drives the other icons.
    /// </summary>
    private static GameObject BuildLauncher(BilibiliSearchUI uiBehaviour)
    {
      var root = new GameObject("LauncherButton", typeof(RectTransform));
      var rect = (RectTransform)root.transform;
      // Matches the url input icon: a 40x40 child of the vertical icon column.
      rect.anchorMin = new Vector2(0f, 0f);
      rect.anchorMax = new Vector2(0f, 0f);
      rect.pivot = new Vector2(0f, 0.5f);
      rect.sizeDelta = new Vector2(40f, 40f);

      var image = AddImage(root, Color.white);
      image.sprite = ResolveBilibiliIcon(DefaultOutputFolder);
      image.type = Image.Type.Simple;
      image.preserveAspect = true;

      var button = root.AddComponent<Button>();
      button.targetGraphic = image;
      button.transition = Selectable.Transition.ColorTint;

      if (uiBehaviour != null)
      {
        UnityEventTools.AddStringPersistentListener(button.onClick,
          UdonSharpEditorUtility.GetBackingUdonBehaviour(uiBehaviour).SendCustomEvent,
          nameof(BilibiliSearchUI.TogglePanel));
      }

      return root;
    }

    private static Button NewButton(string name, Transform parent, string labelName, string labelKey, UdonSharpBehaviour target, string eventName = null, int fontSize = 20, ColorType colorRole = ColorType.Secondary)
    {
      var rect = NewRect(name, parent);
      var image = AddPill(rect.gameObject, colorRole == ColorType.Primary ? PrimaryColorDefault : SecondaryColorDefault, colorRole);

      var button = rect.gameObject.AddComponent<Button>();
      button.targetGraphic = image;
      button.transition = Selectable.Transition.ColorTint;
      if (target != null) UnityEventTools.AddStringPersistentListener(button.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(target).SendCustomEvent, eventName);

      var label = NewText(labelName, rect, fontSize, TextAnchor.MiddleCenter, Color.white);
      Stretch(label.rectTransform);
      label.text = BiliText.Get(labelKey);

      return button;
    }

    #endregion

    #region Results scroll

    private static BilibiliResultList NewScrollView(string name, Transform parent, BilibiliSearchUI uiBehaviour)
    {
      var rect = NewRect(name, parent);
      var scrollRect = rect.gameObject.AddComponent<ScrollRect>();
      scrollRect.horizontal = false;
      scrollRect.vertical = true;
      scrollRect.movementType = ScrollRect.MovementType.Elastic;
      scrollRect.elasticity = 0.1f;
      scrollRect.inertia = true;
      scrollRect.decelerationRate = 0.135f;
      scrollRect.scrollSensitivity = 20f;

      // Viewport
      var viewport = NewRect("Viewport", rect);
      Stretch(viewport);
      AddImage(viewport.gameObject, new Color(1f, 1f, 1f, 0.03f));
      var mask = viewport.gameObject.AddComponent<Mask>();
      mask.showMaskGraphic = false;
      scrollRect.viewport = viewport;

      // Content
      var content = NewRect("Content", viewport);
      content.anchorMin = new Vector2(0f, 1f);
      content.anchorMax = new Vector2(1f, 1f);
      content.pivot = new Vector2(0f, 1f);
      content.anchoredPosition = Vector2.zero;
      content.sizeDelta = new Vector2(0f, 0f);
      scrollRect.content = content;

      // Template cell
      var template = BuildResultCell(content, uiBehaviour);
      template.gameObject.SetActive(false);

      // Scrollbar
      var scrollbar = BuildScrollbar(rect, scrollRect);

      var loopScroll = UdonSharpUndo.AddComponent<BilibiliResultList>(rect.gameObject);
      var serialized = new SerializedObject(loopScroll);
      serialized.FindProperty("_template").objectReferenceValue = template.gameObject;
      serialized.ApplyModifiedPropertiesWithoutUndo();

      UnityEventTools.AddStringPersistentListener(scrollRect.onValueChanged,
        UdonSharpEditorUtility.GetBackingUdonBehaviour(loopScroll).SendCustomEvent, nameof(BilibiliResultList.OnScroll));

      return loopScroll;
    }

    private static RectTransform BuildScrollbar(RectTransform scrollParent, ScrollRect scrollRect)
    {
      var scrollbarRect = NewRect("Scrollbar", scrollParent);
      scrollbarRect.anchorMin = new Vector2(1f, 0f);
      scrollbarRect.anchorMax = new Vector2(1f, 1f);
      scrollbarRect.pivot = new Vector2(1f, 0.5f);
      scrollbarRect.anchoredPosition = Vector2.zero;
      scrollbarRect.sizeDelta = new Vector2(16f, 0f);

      var background = AddImage(scrollbarRect.gameObject, new Color(1f, 1f, 1f, 0.08f));

      var slidingArea = NewRect("Sliding Area", scrollbarRect);
      slidingArea.anchorMin = Vector2.zero;
      slidingArea.anchorMax = Vector2.one;
      slidingArea.offsetMin = new Vector2(3f, 3f);
      slidingArea.offsetMax = new Vector2(-3f, -3f);

      var handle = NewRect("Handle", slidingArea);
      handle.anchorMin = Vector2.zero;
      handle.anchorMax = Vector2.one;
      handle.offsetMin = Vector2.zero;
      handle.offsetMax = Vector2.zero;
      var handleImage = AddImage(handle.gameObject, new Color(1f, 1f, 1f, 0.5f));

      var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
      scrollbar.direction = Scrollbar.Direction.BottomToTop;
      scrollbar.targetGraphic = handleImage;
      scrollbar.handleRect = handle;
      scrollbar.transition = Selectable.Transition.ColorTint;

      scrollRect.verticalScrollbar = scrollbar;
      scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
      scrollRect.verticalScrollbarSpacing = -3f;

      return scrollbarRect;
    }

    /// <summary>
    /// Overlay with the module version, author and changelog, laid out after YamaPlayer's own
    /// version panel: the project name centred on the divider, the author avatar and one icon row
    /// per account in the left half, the divider itself and the changelog in the right half.
    /// Everything is anchored to the top centre of the overlay, so the layout stays centred on
    /// whatever size the page container has.
    /// The account values, the project name, the version string, the changelog heading and the
    /// changelog entries are the module's own data and are deliberately not translated; only the
    /// back button is.
    /// </summary>
    private static GameObject BuildVersionPanel(RectTransform rootRect, BilibiliSearchUI uiBehaviour)
    {
      var overlay = NewRect("VersionPanel", rootRect);
      Stretch(overlay);
      AddImage(overlay.gameObject, new Color(.04f, .04f, .06f, .98f));

      // Back to the search panel. Deliberately short: the overlay is opened from the search panel,
      // so the label only has to say "back". The runtime puts the arrow in front of it.
      var back = NewButton("VersionBackButton", overlay, "VersionBackButtonText", "module.bilibilisearch.version.back", uiBehaviour, nameof(BilibiliSearchUI.CloseVersionPanel), 20, ColorType.Primary);
      PinTopLeft((RectTransform)back.transform, 60f, -44f, 260f, 52f);

      // Project name and version, centred on the divider: the divider starts right below it. A
      // literal like the panel title, the version overlay is module data and never takes part in
      // the translation.
      var name = NewText("VersionName", overlay, 42, TextAnchor.MiddleCenter, Color.white);
      PinTopCenter(name.rectTransform, 0f, -50f, 800f, 64f);
      name.text = "BiliBili Search v" + Version;
      SetColorRole(name.gameObject, ColorType.Primary);

      // Vertical divider between the left half (avatar and accounts) and the changelog. It starts
      // right below the project name and reaches down to the bottom of the changelog.
      var divider = NewRect("VersionDivider", overlay);
      PinTopCenter(divider, 0f, -118f, 2f, 580f);
      var dividerImage = AddImage(divider.gameObject, SecondaryColorDefault);
      dividerImage.raycastTarget = false;
      SetColorRole(divider.gameObject, ColorType.Secondary);

      // Left half: author avatar, centred over the account rows below it and filling the upper
      // half of the divider. Author.png is a round cut out with transparent corners, so the
      // square rect of the image needs no mask, and its 4:3 shape is the rect's shape.
      var avatar = NewRect("VersionAvatar", overlay);
      PinTopCenter(avatar, VersionLeftAxis, VersionAvatarTop, VersionAvatarWidth, VersionAvatarHeight);
      var avatarImage = AddImage(avatar.gameObject, Color.white);
      avatarImage.sprite = ResolveIcon(AuthorFileName, _assetFolder);
      avatarImage.preserveAspect = true;
      avatarImage.raycastTarget = false;

      // One row per account, icon plus value. They are built first and placed afterwards: the
      // widest row decides the width of the block, which is what keeps the icons in one column
      // while the block as a whole is centred on the avatar's axis. The project itself has no row,
      // its name and version are already the heading of the overlay. The last two rows credit the
      // AI assistants, they use the monochrome marks the tool draws.
      RectTransform[] rows = new RectTransform[5];
      float blockWidth = 0f;
      float contentWidth;

      rows[0] = BuildVersionRow(overlay, "VRChat", ResolveIcon(SocialIconVRChat, YamaPlayerImageFolder), "ホシノちゃん", out contentWidth);
      if (contentWidth > blockWidth) blockWidth = contentWidth;
      rows[1] = BuildVersionRow(overlay, "Twitter", ResolveIcon(SocialIconTwitter, YamaPlayerImageFolder), "vrchat_hoshino", out contentWidth);
      if (contentWidth > blockWidth) blockWidth = contentWidth;
      rows[2] = BuildVersionRow(overlay, "Github", ResolveIcon(SocialIconGithub, YamaPlayerImageFolder), "Nuist666", out contentWidth);
      if (contentWidth > blockWidth) blockWidth = contentWidth;
      rows[3] = BuildVersionRow(overlay, "DeepSeek", ResolveIcon(AiIconDeepSeek, _assetFolder), "DeepSeek-assisted development", out contentWidth);
      if (contentWidth > blockWidth) blockWidth = contentWidth;
      rows[4] = BuildVersionRow(overlay, "Codex", ResolveIcon(AiIconCodex, _assetFolder), "Codex-assisted development", out contentWidth);
      if (contentWidth > blockWidth) blockWidth = contentWidth;

      // Every row is padded by the same inset on both sides of its content, so centring the row
      // also centres the content of its widest row. The first row starts one blank line below the
      // avatar, which is one full row pitch.
      float rowWidth = blockWidth + VersionRowInset * 2f;
      float rowTop = VersionAvatarTop - VersionAvatarHeight - VersionRowHeight - VersionRowSpacing;

      for (int i = 0; i < rows.Length; i++)
      {
        RectTransform row = rows[i];
        row.sizeDelta = new Vector2(rowWidth, VersionRowHeight);
        row.anchoredPosition = new Vector2(VersionLeftAxis, rowTop - i * (VersionRowHeight + VersionRowSpacing));
      }

      // Right half: the changelog. Its heading is a fixed Japanese string, like the panel title.
      var changelogTitle = NewText("VersionChangelogTitle", overlay, 30, TextAnchor.MiddleLeft, PrimaryColorDefault);
      PinTopCenter(changelogTitle.rectTransform, 70f, -140f, 700f, 44f, 0f);
      changelogTitle.text = "更新履歴";
      SetColorRole(changelogTitle.gameObject, ColorType.Primary);

      BuildVersionChangelog(overlay);

      overlay.SetAsLastSibling();
      overlay.gameObject.SetActive(false);
      return overlay.gameObject;
    }

    /// <summary>
    /// The changelog of the version overlay, as a scroll view: the entry text grows downwards and
    /// the view stays scrollable, so more entries can simply be appended to the text. The content
    /// is kept as tall as the text through a ContentSizeFitter, the same way the result list of
    /// the panel scrolls.
    /// </summary>
    private static void BuildVersionChangelog(RectTransform overlay)
    {
      var scroll = NewRect("VersionChangelogScroll", overlay);
      PinTopCenter(scroll, 70f, -196f, 700f, 500f, 0f);

      var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
      scrollRect.horizontal = false;
      scrollRect.vertical = true;
      scrollRect.movementType = ScrollRect.MovementType.Elastic;
      scrollRect.elasticity = 0.1f;
      scrollRect.inertia = true;
      scrollRect.decelerationRate = 0.135f;
      scrollRect.scrollSensitivity = 20f;

      var viewport = NewRect("Viewport", scroll);
      Stretch(viewport);
      // The scrollbar overlays the right edge of the scroll rect, so the viewport is inset: the
      // masked area and the bar stay out of each other's way and no line of text is clipped.
      viewport.offsetMax = new Vector2(-20f, 0f);
      AddImage(viewport.gameObject, new Color(1f, 1f, 1f, 0.03f));
      var mask = viewport.gameObject.AddComponent<Mask>();
      mask.showMaskGraphic = false;
      scrollRect.viewport = viewport;

      var content = NewRect("Content", viewport);
      content.anchorMin = new Vector2(0f, 1f);
      content.anchorMax = new Vector2(1f, 1f);
      content.pivot = new Vector2(0f, 1f);
      content.anchoredPosition = Vector2.zero;
      content.sizeDelta = Vector2.zero;
      var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
      fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
      fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
      scrollRect.content = content;

      // The text sits on the content itself: it reports its own preferred height, which is what
      // the fitter above turns into the height of the scrollable content.
      var changelog = NewText("VersionChangelogValue", content, 22, TextAnchor.UpperLeft, new Color(0.72f, 0.72f, 0.76f));
      Stretch(changelog.rectTransform);
      changelog.horizontalOverflow = HorizontalWrapMode.Wrap;
      changelog.verticalOverflow = VerticalWrapMode.Overflow;
      changelog.text = Changelog;

      BuildScrollbar(scroll, scrollRect);
    }

    /// <summary>
    /// One account row of the version overlay: the icon of the account plus its value. The rect of
    /// the row is stamped by the caller once every row has been measured, so all rows end up the
    /// same width and their icons stay in one column.
    /// </summary>
    /// <param name="contentWidth">
    /// Width of the widest thing in the row, icon and value together. The caller uses the largest
    /// of these to size and centre the block.
    /// </param>
    private static RectTransform BuildVersionRow(RectTransform overlay, string account, Sprite icon, string value, out float contentWidth)
    {
      const float iconSize = 28f;
      const float iconGap = 20f;
      // The row is padded by VersionRowInset on both sides of its content, which is what makes the
      // content of the widest row sit exactly centred once the row itself is centred.
      const float valueInset = VersionRowInset + iconSize + iconGap;

      var row = NewRect("Version" + account + "Row", overlay);
      row.anchorMin = new Vector2(0.5f, 1f);
      row.anchorMax = new Vector2(0.5f, 1f);
      row.pivot = new Vector2(0.5f, 1f);
      // Placeholders, both are stamped by the caller once the whole block is known.
      row.anchoredPosition = new Vector2(VersionLeftAxis, 0f);
      row.sizeDelta = new Vector2(valueInset, VersionRowHeight);

      if (icon != null)
      {
        var iconRect = NewRect("Icon", row);
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(VersionRowInset, 0f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        var iconImage = AddImage(iconRect.gameObject, Color.white);
        iconImage.sprite = icon;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
      }

      var valueText = NewText("Version" + account + "Value", row, 22, TextAnchor.MiddleLeft, Color.white);
      PlaceRowText(valueText, valueInset, 400f);
      valueText.text = value;

      // The value is laid out with overflow, so this is the width the text really needs. A text
      // that cannot be measured (no font yet) falls back to an estimate, which only shifts the
      // block by a couple of units.
      float valueWidth = valueText.preferredWidth;
      if (valueWidth <= 0f || float.IsNaN(valueWidth)) valueWidth = value.Length * 12f;

      contentWidth = iconSize + iconGap + valueWidth;
      return row;
    }

    /// <summary>Left aligned cell inside a version row, vertically centred.</summary>
    private static void PlaceRowText(Text text, float x, float width)
    {
      var rect = text.rectTransform;
      rect.anchorMin = new Vector2(0f, 0.5f);
      rect.anchorMax = new Vector2(0f, 0.5f);
      rect.pivot = new Vector2(0f, 0.5f);
      rect.anchoredPosition = new Vector2(x, 0f);
      rect.sizeDelta = new Vector2(width, 32f);
      // Both texts report the width their text needs, the row is wide enough for all values.
      text.horizontalOverflow = HorizontalWrapMode.Overflow;
    }

    private static RectTransform BuildResultCell(Transform content, BilibiliSearchUI uiBehaviour)
    {
      var cell = NewRect("Cell", content);
      cell.anchorMin = new Vector2(0f, 1f);
      cell.anchorMax = new Vector2(1f, 1f);
      cell.pivot = new Vector2(0.5f, 1f);
      cell.anchoredPosition = Vector2.zero;
      // The extra height below the action buttons is the blank line before the separator.
      cell.sizeDelta = new Vector2(0f, CellHeight);

      // The text block is a vertical group on purpose: it is what removes the blank line after
      // the title, because the meta line is placed right below the title's real height instead
      // of at a fixed offset that would leave a hole whenever the title is a single line.
      // Cover images are intentionally not shown: VRCImageDownloader lives in VRC.SDK3.Image
      // which cannot be referenced from this module assembly.
      var info = NewRect("Info", cell);
      info.anchorMin = new Vector2(0f, 1f);
      info.anchorMax = new Vector2(1f, 1f);
      info.pivot = new Vector2(0.5f, 1f);
      info.anchoredPosition = new Vector2(0f, -6f);
      info.sizeDelta = new Vector2(-20f, InfoHeight);
      var infoLayout = info.gameObject.AddComponent<VerticalLayoutGroup>();
      infoLayout.childAlignment = TextAnchor.UpperLeft;
      infoLayout.spacing = 2f;
      infoLayout.childControlWidth = true;
      infoLayout.childControlHeight = true;
      infoLayout.childForceExpandWidth = true;
      infoLayout.childForceExpandHeight = false;

      // Title: no fixed height, it gets exactly the height its text needs (up to two lines fit
      // in the block), which keeps the meta line glued to it.
      var title = NewText("Title", info, 28, TextAnchor.UpperLeft, Color.white);
      title.verticalOverflow = VerticalWrapMode.Truncate;

      // Uploader + BV id packed left to right on one line.
      var meta = NewRect("Meta", info);
      var metaLayout = meta.gameObject.AddComponent<HorizontalLayoutGroup>();
      metaLayout.childAlignment = TextAnchor.MiddleLeft;
      metaLayout.spacing = 14f;
      metaLayout.childControlWidth = true;
      metaLayout.childControlHeight = true;
      metaLayout.childForceExpandWidth = false;
      metaLayout.childForceExpandHeight = false;
      var metaElement = meta.gameObject.AddComponent<LayoutElement>();
      metaElement.minHeight = MetaHeight;
      metaElement.preferredHeight = MetaHeight;
      metaElement.flexibleHeight = 0f;

      // Both texts report their own preferred width, so the id simply follows the name
      // instead of sitting at a fixed column.
      var channel = NewText("Channel", meta, 22, TextAnchor.MiddleLeft, new Color(0.8f, 0.65f, 0.75f));
      channel.horizontalOverflow = HorizontalWrapMode.Overflow;
      var id = NewText("Id", meta, 20, TextAnchor.MiddleLeft, new Color(0.6f, 0.6f, 0.65f));
      id.horizontalOverflow = HorizontalWrapMode.Overflow;
      id.supportRichText = false;

      // Summary: flexible height with a preferred height of 0, so it takes exactly the space
      // the title did not use instead of pushing the block past the action buttons.
      var description = NewText("Description", info, 20, TextAnchor.UpperLeft, new Color(0.72f, 0.72f, 0.76f));
      description.verticalOverflow = VerticalWrapMode.Truncate;
      var descriptionElement = description.gameObject.AddComponent<LayoutElement>();
      descriptionElement.minHeight = 0f;
      descriptionElement.preferredHeight = 0f;
      descriptionElement.flexibleHeight = 1f;

      // Actions, lifted off the bottom so a blank line is left above the separator.
      var actions = NewRect("Actions", cell);
      actions.anchorMin = new Vector2(0f, 0f);
      actions.anchorMax = new Vector2(1f, 0f);
      actions.pivot = new Vector2(0.5f, 0f);
      actions.anchoredPosition = new Vector2(0f, 36f);
      actions.sizeDelta = new Vector2(-20f, 48f);

      // Separator between two entries, so neighbouring results stay distinguishable.
      var separator = NewRect("Separator", cell);
      separator.anchorMin = new Vector2(0f, 0f);
      separator.anchorMax = new Vector2(1f, 0f);
      separator.pivot = new Vector2(0.5f, 0f);
      separator.anchoredPosition = Vector2.zero;
      separator.sizeDelta = new Vector2(-40f, 3f);
      var separatorImage = AddImage(separator.gameObject, SecondaryColorDefault);
      separatorImage.raycastTarget = false;
      SetColorRole(separator.gameObject, ColorType.Secondary);

      AddResultActionButton(actions, "CopyButton", "CopyButtonText", "button.copyUrl", uiBehaviour, nameof(BilibiliSearchResultAction.CopyLink), 0f);
      AddResultActionButton(actions, "PlayButton", "PlayButtonText", "button.playVideo", uiBehaviour, nameof(BilibiliSearchResultAction.Play), 0.5f);
      AddResultActionButton(actions, "QueueButton", "QueueButtonText", "button.addQueue", uiBehaviour, nameof(BilibiliSearchResultAction.AddToQueue), 1f);

      return cell;
    }

    private static void AddResultActionButton(Transform parent, string name, string labelName, string labelKey, BilibiliSearchUI uiBehaviour, string eventName, float xAnchor)
    {
      var button = NewButton(name, parent, labelName, labelKey, null);
      var rect = (RectTransform)button.transform;
      rect.anchorMin = new Vector2(xAnchor, 0f);
      rect.anchorMax = new Vector2(xAnchor, 1f);
      rect.pivot = new Vector2(xAnchor, 0.5f);
      rect.anchoredPosition = new Vector2(xAnchor == 0.5f ? 0f : (xAnchor == 0f ? 24f : -24f), 0f);
      rect.sizeDelta = new Vector2(300f, 0f);

      var action = UdonSharpUndo.AddComponent<BilibiliSearchResultAction>(button.gameObject);
      // The queue button keeps its own interactable state in sync with the panel's cooldown.
      var actionState = new SerializedObject(action);
      actionState.FindProperty("_button").objectReferenceValue = button;
      actionState.FindProperty("_isQueueAction").boolValue = name == "QueueButton";
      actionState.ApplyModifiedPropertiesWithoutUndo();
      UnityEventTools.AddStringPersistentListener(button.onClick,
        UdonSharpEditorUtility.GetBackingUdonBehaviour(action).SendCustomEvent, eventName);
    }

    private static void PinTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
      rect.anchorMin = new Vector2(0f, 1f);
      rect.anchorMax = new Vector2(0f, 1f);
      rect.pivot = new Vector2(0f, 1f);
      rect.anchoredPosition = new Vector2(x, y);
      rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>
    /// Pins a rect below the top edge of its parent, with x measured from the horizontal centre
    /// of the parent. The version overlay is built from these so it stays centred on any page
    /// size, instead of assuming the 1600x900 the panel is authored against. A pivotX of 0 hangs
    /// the rect to the right of the centre, which is how the changelog column is placed.
    /// </summary>
    private static void PinTopCenter(RectTransform rect, float x, float y, float width, float height, float pivotX = 0.5f)
    {
      rect.anchorMin = new Vector2(0.5f, 1f);
      rect.anchorMax = new Vector2(0.5f, 1f);
      rect.pivot = new Vector2(pivotX, 1f);
      rect.anchoredPosition = new Vector2(x, y);
      rect.sizeDelta = new Vector2(width, height);
    }

    #endregion
  }
}
