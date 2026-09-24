using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VRC.SDK3.Data;

namespace Yamadev.YamaStream.Modules.BilibiliSearch.Editor
{
  /// <summary>Editor-only diagnostics. Does not bake pools or modify scene objects.</summary>
  public class BilibiliSearchSridTester : EditorWindow
  {
    [SerializeField] private bool _settingsLoaded;
    [SerializeField] private string _baseUrl;
    [SerializeField] private int _start;
    [SerializeField] private int _capacity;
    [SerializeField] private int _latest;
    [SerializeField] private int _dailyGrowth;
    [SerializeField] private int _days;
    [SerializeField] private string _keyword = "";
    [SerializeField] private int _page = 1;
    private int _previous;
    private int _next;
    private int _observed;
    private int _minimumObserved;
    private string _responseBase;
    private string _requestUrl = "";
    private string _status = "No request sent";
    private string _summary = "";
    private string _body = "";
    private Vector2 _scroll;
    private Vector2 _summaryScroll;
    private Vector2 _bodyScroll;
    private UnityWebRequest _request;
    private double _nextRequestTime;

    [MenuItem("Tools/YamaPlayer/Bilibili Search/SRID Test Tool", priority = 102)]
    public static void Open()
    {
      GetWindow<BilibiliSearchSridTester>("Bilibili SRID Test").Show();
    }

    private void OnEnable()
    {
      minSize = new Vector2(540, 540);
      if (!_settingsLoaded) LoadSetupSettings();
      EditorApplication.update += Tick;
    }

    private void LoadSetupSettings()
    {
      _baseUrl = BilibiliSearchPanelSetup.BackendUrl;
      _start = BilibiliSearchDirectSetup.ConfiguredStart;
      _capacity = BilibiliSearchDirectSetup.ConfiguredCapacity;
      _latest = BilibiliSearchDirectSetup.ConfiguredLatest;
      _dailyGrowth = BilibiliSearchDirectSetup.ConfiguredDailyGrowth;
      _days = BilibiliSearchDirectSetup.ConfiguredDays;
      _settingsLoaded = true;
      ClearResults();
    }

    private void ClearResults()
    {
      _previous = _next = _observed = _minimumObserved = 0;
      _summary = _body = _requestUrl = "";
      _summaryScroll = _bodyScroll = Vector2.zero;
      _status = "No request sent";
    }

    private void OnDisable()
    {
      EditorApplication.update -= Tick;
      DisposeRequest();
    }

    private void DisposeRequest()
    {
      if (_request == null) return;
      _request.Abort();
      _request.Dispose();
      _request = null;
    }

    private void OnGUI()
    {
      float previousLabelWidth = EditorGUIUtility.labelWidth;
      try
      {
        EditorGUIUtility.labelWidth = 230f;
        DrawContents();
      }
      finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
    }

    private void DrawContents()
    {
      _scroll = EditorGUILayout.BeginScrollView(_scroll);
      // Constrain the content independently of long URLs / response text. Otherwise
      // IMGUI expands every control and centers button captions outside the viewport.
      EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(300f, position.width - 22f)));
      EditorGUILayout.LabelField("Keyword Search / SRID Coverage", EditorStyles.boldLabel);
      EditorGUILayout.HelpBox("Defaults are loaded from Setup. Edit values for this test, or reload Setup settings. Local edits do not change Setup.", MessageType.Info);
      using (new EditorGUI.DisabledScope(_request != null))
      {
        if (GUILayout.Button("Reload from Setup")) LoadSetupSettings();
        EditorGUI.BeginChangeCheck();
        _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
        _start = EditorGUILayout.IntField("First record ID", _start);
        _capacity = EditorGUILayout.IntField("URL count", _capacity);
        _latest = EditorGUILayout.IntField("Latest observed record ID", _latest);
        _dailyGrowth = EditorGUILayout.IntField("Estimated IDs per day", _dailyGrowth);
        _days = EditorGUILayout.IntField("Days to cover", _days);
        if (EditorGUI.EndChangeCheck()) ClearResults();
      }
      string root = (_baseUrl ?? "").Trim();
      int start = _start;
      int count = _capacity;
      int latest = _latest;
      int growth = _dailyGrowth;
      int days = _days;
      bool rangeValid = BilibiliSearchDirectSetup.IsRangeValid(start, count);
      EditorGUILayout.LabelField("Record URL pool", rangeValid ? start + " – " + ((long)start + count - 1) + " (" + count + " URLs)" : "Invalid range");
      EditorGUILayout.LabelField("Latest ID / Daily growth / Days", latest + " / " + growth + " / " + days);
      int planned = BilibiliSearchDirectSetup.PlanCapacity(start, latest, growth, days);
      EditorGUILayout.LabelField("Setup-based suggested count", planned > 0 ? planned.ToString() : "Cannot estimate: check inputs and capacity limit");
      if (rangeValid && growth > 0 && latest >= start && latest <= (long)start + count - 1)
        EditorGUILayout.LabelField("Setup-based remaining days", (((long)start + count - 1 - latest) / (double)growth).ToString("F1"));
      if (GUILayout.Button("Open Bilibili Search Setup"))
        EditorApplication.ExecuteMenuItem("Tools/YamaPlayer/Bilibili Search Setup");
      EditorGUILayout.HelpBox("Growth estimates do not prove that IDs exist. Searches create backend records. Pagination uses L+1 / L+2 only for contiguous raw records. UnityWebRequest results do not validate AVPro / yt-dlp playback.", MessageType.Info);

      bool validBackend = IsBackendValid(root);
      if (!validBackend) EditorGUILayout.HelpBox("Enter a valid HTTPS Base URL without a query or fragment.", MessageType.Warning);
      bool ready = validBackend && _request == null && EditorApplication.timeSinceStartup >= _nextRequestTime;
      using (new EditorGUI.DisabledScope(_request != null))
      {
        EditorGUI.BeginChangeCheck();
        _keyword = EditorGUILayout.TextField("Search keyword", _keyword);
        _page = EditorGUILayout.IntField("Search page (1-9999)", _page);
        if (EditorGUI.EndChangeCheck()) ClearResults();
      }
      using (new EditorGUI.DisabledScope(!ready || string.IsNullOrWhiteSpace(_keyword) || _page < 1 || _page > 9999))
        if (GUILayout.Button("Search and check SRID coverage"))
          StartRequest(root + "?page=" + _page.ToString(CultureInfo.InvariantCulture) + "&keyword=" + Uri.EscapeDataString(_keyword.Trim()), root);
      EditorGUILayout.BeginHorizontal();
      using (new EditorGUI.DisabledScope(!ready || _previous < 1 || root != _responseBase))
        if (GUILayout.Button("Test previous page SRID " + _previous)) StartRequest(RecordUrl(root, _previous), root);
      using (new EditorGUI.DisabledScope(!ready || _next < 1 || root != _responseBase))
        if (GUILayout.Button("Test next page SRID " + _next)) StartRequest(RecordUrl(root, _next), root);
      EditorGUILayout.EndHorizontal();
      using (new EditorGUI.DisabledScope(_request != null || _observed < 1 || root != _responseBase || root != BilibiliSearchPanelSetup.BackendUrl))
        if (GUILayout.Button("Write highest response ID to Setup (same backend only)")) BilibiliSearchDirectSetup.ConfiguredLatest = _observed;
      if (_request != null && GUILayout.Button("Cancel request"))
      {
        DisposeRequest();
        _status = "Cancelled";
      }
      if (_request == null && EditorApplication.timeSinceStartup < _nextRequestTime)
        EditorGUILayout.LabelField("Request cooldown", Math.Ceiling(_nextRequestTime - EditorApplication.timeSinceStartup) + " seconds");
      EditorGUILayout.Space();
      EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedLabel);
      EditorGUILayout.SelectableLabel(_requestUrl, EditorStyles.wordWrappedLabel, GUILayout.Height(48));
      EditorGUILayout.BeginVertical(EditorStyles.helpBox);
      EditorGUILayout.LabelField("SRID Range", EditorStyles.boldLabel);
      var rangeStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
      EditorGUILayout.SelectableLabel(_minimumObserved > 0 ? _minimumObserved + " - " + _observed : "-",
        rangeStyle, GUILayout.Height(32));
      EditorGUILayout.EndVertical();
      EditorGUILayout.LabelField("Search results and prediction", EditorStyles.boldLabel);
      DrawScrollableText(_summary, ref _summaryScroll, 230f);
      EditorGUILayout.LabelField("Raw response (up to 32,000 characters)");
      DrawScrollableText(_body, ref _bodyScroll, 280f);
      EditorGUILayout.EndVertical();
      EditorGUILayout.EndScrollView();
    }

    private static void DrawScrollableText(string text, ref Vector2 scroll, float height)
    {
      // Explicit rectangles keep text measurement out of the parent GUILayout pass.
      // Wrapping plus a vertical scrollbar makes every displayed line reachable.
      Rect viewport = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
      var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true, richText = false };
      float width = Mathf.Max(1f, viewport.width - 18f);
      float contentHeight = Mathf.Max(height, style.CalcHeight(new GUIContent(text), width) + 8f);
      Rect content = new Rect(0f, 0f, width, contentHeight);
      scroll = GUI.BeginScrollView(viewport, scroll, content, false, true);
      EditorGUI.SelectableLabel(content, text, style);
      GUI.EndScrollView();
    }

    internal static string FormatResponse(string body)
    {
      if (string.IsNullOrEmpty(body)) return string.Empty;
      if (VRCJson.TryDeserializeFromJson(body, out DataToken token) &&
          VRCJson.TrySerializeToJson(token, JsonExportType.Beautify, out DataToken formatted))
        return formatted.String;
      return body;
    }

    internal static bool IsBackendValid(string root)
    {
      return Uri.TryCreate(root, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    private static string RecordUrl(string root, int id) => root + "?srid=" + id.ToString(CultureInfo.InvariantCulture);

    internal static string Coverage(int id, int start, int count)
    {
      if (!BilibiliSearchDirectSetup.IsRangeValid(start, count)) return "Invalid pool settings";
      return id > 0 && id >= start && (long)id < (long)start + count ? "Inside pool" : "Outside pool (unavailable for direct runtime use)";
    }

    private void StartRequest(string url, string root)
    {
      _previous = _next = _observed = _minimumObserved = 0;
      _summary = _body = "";
      _summaryScroll = _bodyScroll = Vector2.zero;
      _requestUrl = url;
      _responseBase = root;
      _nextRequestTime = EditorApplication.timeSinceStartup + 5.1;
      try
      {
        _request = UnityWebRequest.Get(url);
        _request.timeout = 20;
        // Inspect redirects without downloading a potentially large media file.
        _request.redirectLimit = 0;
        _request.SendWebRequest();
        _status = "Requesting...";
      }
      catch (Exception e)
      {
        DisposeRequest();
        _status = "Request failed: " + e.Message;
      }
    }

    private void Tick()
    {
      if (_request != null && _request.isDone)
      {
        try
        {
          string body = _request.downloadHandler.text;
          string formatted = FormatResponse(body);
          _body = formatted.Length > 32000 ? formatted.Substring(0, 32000) + "\n... (display truncated; calculations use the full response)" : formatted;
          _status = "HTTP " + _request.responseCode + " | " + _request.result + " | " + _request.GetResponseHeader("Content-Type");
          if (_request.responseCode >= 300 && _request.responseCode < 400)
            _summary = "Redirect (not followed): " + _request.GetResponseHeader("Location") + "\nThis result does not confirm media playback.";
          else if (_request.result == UnityWebRequest.Result.Success)
            _summary = Analyze(body, _start, _capacity,
              out _previous, out _next, out _observed, out _minimumObserved, _dailyGrowth, _days);
          else _summary = "Request failed: " + _request.error;
        }
        catch (Exception e) { _status = "Response processing failed: " + e.Message; }
        finally { DisposeRequest(); }
        Repaint();
      }
      if (EditorApplication.timeSinceStartup < _nextRequestTime) Repaint();
    }

    internal static string Analyze(string body, int start, int count, out int previous, out int next, out int observed,
      int dailyGrowth = 0, int days = 0)
    {
      return Analyze(body, start, count, out previous, out next, out observed, out _, dailyGrowth, days);
    }

    internal static string Analyze(string body, int start, int count, out int previous, out int next, out int observed,
      out int minimumObserved, int dailyGrowth = 0, int days = 0)
    {
      previous = next = observed = minimumObserved = 0;
      if (!VRCJson.TryDeserializeFromJson(body, out DataToken token)) return "Invalid JSON; pagination cannot be calculated.";
      DataList list = null;
      if (token.TokenType == TokenType.DataList) list = token.DataList;
      else if (token.TokenType == TokenType.DataDictionary)
        foreach (string key in new[] { "data", "result", "list" })
          if (token.DataDictionary.TryGetValue(key, out DataToken data) && data.TokenType == TokenType.DataList)
          { list = data.DataList; break; }
      if (list == null) return "JSON contains no search result array. It may be video metadata; pagination cannot be calculated.";
      if (list.Count == 0) return "Empty results; no pagination IDs can be calculated.";
      bool contiguous = true;
      int last = 0;
      int valid = 0;
      int outside = 0;
      int minimum = int.MaxValue;
      bool poolValid = BilibiliSearchDirectSetup.IsRangeValid(start, count);
      for (int i = 0; i < list.Count; i++)
      {
        int id = ReadRecordId(list[i]);
        if (id < 1 || (i > 0 && (long)id != (long)last + 1)) contiguous = false;
        if (id > 0)
        {
          valid++;
          observed = Math.Max(observed, id);
          minimum = Math.Min(minimum, id);
          if (poolValid && (id < start || (long)id >= (long)start + count)) outside++;
        }
        last = id;
      }
      var report = new StringBuilder();
      minimumObserved = valid > 0 ? minimum : 0;
      report.AppendLine("Raw records: " + list.Count + "; valid SRIDs: " + valid + "; invalid/missing: " + (list.Count - valid));
      report.AppendLine("Returned SRID range: " + (valid > 0 ? minimum + " - " + observed : "No valid SRIDs"));
      if (!poolValid) report.AppendLine("Pool coverage: Unknown (invalid pool settings)");
      else
      {
        report.AppendLine("Inside pool: " + (valid - outside) + "; outside pool: " + outside);
        report.AppendLine("Pool coverage: " + (valid == 0 ? "Unknown (no valid SRIDs)" :
          outside > 0 ? "OUTSIDE - some returned IDs are unavailable" :
          valid != list.Count ? "INCOMPLETE - valid IDs are inside; other records cannot be checked" : "PASS - all returned IDs are inside"));
      }
      if (contiguous && last <= int.MaxValue - 2)
      {
        previous = last + 1;
        next = last + 2;
        report.AppendLine("Previous page: " + previous + " / " + Coverage(previous, start, count));
        report.AppendLine("Next page: " + next + " / " + Coverage(next, start, count));
      }
      else report.AppendLine("Invalid or noncontiguous IDs, or pagination overflow. Pagination is disabled.");
      report.AppendLine(Forecast(start, count, observed, next, dailyGrowth, days));
      return report.ToString();
    }

    internal static string Forecast(int start, int count, int observed, int next, int dailyGrowth, int days)
    {
      if (observed < 1) return "Prediction unavailable: no valid response IDs.";
      if (!BilibiliSearchDirectSetup.IsRangeValid(start, count) || dailyGrowth < 1 || days < 1)
        return "Prediction unavailable: check pool settings, daily growth and days to cover.";
      long last = (long)start + count - 1;
      long remaining = last - Math.Max(observed, next);
      var report = new StringBuilder();
      report.AppendLine("Response-based prediction (assumed growth: " + dailyGrowth + " IDs/day):");
      report.AppendLine("Upper-bound headroom: " + remaining + " IDs (including inferred pagination)");
      report.AppendLine("Estimated remaining days: " + (Math.Max(0L, remaining) / (double)dailyGrowth).ToString("F1", CultureInfo.InvariantCulture));
      int planned = BilibiliSearchDirectSetup.PlanCapacity(start, observed, dailyGrowth, days);
      if (planned > 0)
      {
        report.AppendLine("Suggested pool for " + days + " days (+20% reserve + pagination): " + start + " - " + ((long)start + planned - 1));
        report.AppendLine("Suggested URL count: " + planned + "; additional URLs needed: " + Math.Max(0, planned - count));
      }
      else report.AppendLine("No valid suggested pool: response ID is below the pool start, or the plan exceeds capacity / ID limits.");
      if (observed < start) report.AppendLine("Returned IDs are below the pool. Upper-bound headroom does not mean current results are covered.");
      report.Append("Estimate only: this response is not the global latest ID, and one search cannot measure daily growth.");
      return report.ToString();
    }

    private static int ReadRecordId(DataToken row)
    {
      if (row.TokenType != TokenType.DataDictionary || !row.DataDictionary.TryGetValue("recordsid", out DataToken value)) return 0;
      if (value.TokenType == TokenType.Double)
      {
        double number = value.Double;
        return number >= 1 && number <= int.MaxValue && number == Math.Floor(number) ? (int)number : 0;
      }
      if (value.TokenType != TokenType.String) return 0;
      string raw = value.String;
      if (string.IsNullOrEmpty(raw) || raw.Length > 10) return 0;
      foreach (char c in raw) if (c < '0' || c > '9') return 0;
      return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : 0;
    }
  }
}
