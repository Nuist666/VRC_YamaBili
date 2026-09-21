using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// Selects request URLs, downloads the JSON and parses it into a <see cref="BilibiliSearchResult"/>.
  /// </summary>
  /// <remarks>
  /// Udon does not expose "new VRCUrl(string)", a VRCUrl can only come from a serialized
  /// field or from a VRCUrlInputField. Initial searches use player input; subsequent
  /// page and video actions select complete srid URLs baked by the editor.
  /// </remarks>
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliSearchService : YamaPlayerBehaviour
  {
    [Header("Bilibili Search - Endpoints")]
    [Tooltip("Root of the bilibili player service. Direct action URLs are baked from this endpoint in the editor.")]
    [SerializeField] private VRCUrl _baseUrl;
    [Tooltip("Maximum amount of results shown per page.")]
    [SerializeField, Range(1, 50)] private int _maxResults = 20;

    [Header("Direct actions - bake URLs before uploading")]
    [Min(1)] public int RecordUrlStart = 550000;
    public const int MaxRecordUrlCapacity = 2000000;
    [Range(1, MaxRecordUrlCapacity)] public int RecordUrlCapacity = 1200000;
    [HideInInspector] public VRCUrl[] RecordUrls = new VRCUrl[0];
    private VRCUrl _lastSuccessfulRequest;
    private VRCUrl _returnPageRequest;
    private int _returnPageNumber;
    private BilibiliSearchUI _uiBehaviour;
    private BilibiliSearchResult _result;
    private int _pendingPage = 1;
    private string _pendingKeyword = string.Empty;
    private bool _loading;
    private float _nextRequestTime;
    private string _requestUrl;
    private string _lastSuccessfulUrl;
    public string LastSuccessfulUrl => _lastSuccessfulUrl;

    private void Start()
    {
      if (!Utilities.IsValid(_result)) _result = GetComponentInChildren<BilibiliSearchResult>();
    }

    public BilibiliSearchUI UIBehaviour => _uiBehaviour;
    public BilibiliSearchResult Result => _result;
    public bool IsLoading => _loading;
    public int PendingPage => _pendingPage;
    public string PendingKeyword => _pendingKeyword;
    public VRCUrl BaseUrl => _baseUrl;
    public int MaxResults => _maxResults;

    /// <summary>Wired up by the BilibiliSearch module.</summary>
    public void SetUIBehaviour(BilibiliSearchUI uiBehaviour)
    {
      _uiBehaviour = uiBehaviour;
    }

    public void SetResult(BilibiliSearchResult result)
    {
      _result = result;
    }

    /// <summary>Base URL used to validate requests and author the editor URL pool.</summary>
    public string BaseUrlString
    {
      get
      {
        if (!Utilities.IsValid(_baseUrl)) return string.Empty;
        return _baseUrl.Get();
      }
    }

    public string GetBilibiliVideoUrl(string id)
    {
      if (string.IsNullOrEmpty(id)) return "https://www.bilibili.com/video/";
      return string.Concat("https://www.bilibili.com/video/", id);
    }

    /// <summary>
    /// Builds the search request url as a string. Only used for logging and for pre-filling
    /// guidance; VRChat does not let a script turn a string into a VRCUrl, so the actual
    /// initial search URL comes from the player's VRCUrlInputField.
    /// </summary>
    public string BuildSearchUrl(string keyword, int page)
    {
      string baseUrl = BaseUrlString;
      if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
      if (page < 1) page = 1;
      if (string.IsNullOrEmpty(keyword)) keyword = string.Empty;
      return string.Concat(baseUrl, "?page=", page.ToString(), "&keyword=", keyword);
    }

    /// <summary>Playback url for one bilibili video, used for logging.</summary>
    public string BuildPlayUrl(string videoUrl)
    {
      string baseUrl = BaseUrlString;
      if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(videoUrl)) return string.Empty;
      return string.Concat(baseUrl, "?url=", videoUrl);
    }

    /// <summary>Only selects serialized URLs; never creates a VRCUrl at runtime.</summary>
    public VRCUrl GetRecordUrl(int recordId)
    {
      if (recordId < 1 || RecordUrlStart < 1 || recordId < RecordUrlStart || RecordUrls == null) return VRCUrl.Empty;
      int index = recordId - RecordUrlStart;
      if (index >= RecordUrls.Length) return VRCUrl.Empty;
      VRCUrl url = RecordUrls[index];
      if (VRCUrl.IsNullOrEmpty(url) || string.IsNullOrEmpty(BaseUrlString)) return VRCUrl.Empty;
      // Detect stale pools after editing the base URL or the starting index.
      if (url.Get() != BaseUrlString + "?srid=" + recordId.ToString()) return VRCUrl.Empty;
      return url;
    }

    public VRCUrl GetResultUrl(int index)
    {
      if (!Utilities.IsValid(_result) || index < 0 || index >= _result.Count || index >= _result.RecordIds.Length) return VRCUrl.Empty;
      return GetRecordUrl(_result.RecordIds[index]);
    }

    public bool MatchesResultUrl(int index, VRCUrl url)
    {
      VRCUrl expected = GetResultUrl(index);
      return !VRCUrl.IsNullOrEmpty(expected) && !VRCUrl.IsNullOrEmpty(url) && expected.Get() == url.Get();
    }

    public void Search(string keyword, int page)
    {
      if (!Utilities.IsValid(_uiBehaviour)) return;
      VRCUrl url = _uiBehaviour.RequestUrl;
      if (VRCUrl.IsNullOrEmpty(url) || BiliUrlUtility.Page(url.Get()) == 0 || !url.Get().StartsWith(BaseUrlString + "?")) return;
      if (BeginSearch(url, BiliUrlUtility.Keyword(url.Get()), BiliUrlUtility.Page(url.Get())))
      {
        _returnPageRequest = VRCUrl.Empty;
        _returnPageNumber = 0;
      }
    }

    /// <returns>0 unavailable, 1 submitted, 2 busy/cooling down.</returns>
    public int SearchPage(int page)
    {
      if (_loading || Time.time < _nextRequestTime) return 2;
      if (!Utilities.IsValid(_result) || page < 1 || page > 9999) return 0;
      if (page != _result.Page - 1 && page != _result.Page + 1) return 0;
      int recordId = page < _result.Page ? _result.PreviousRecordId : _result.NextRecordId;
      VRCUrl url = GetRecordUrl(recordId);
      // An empty terminal page has no records from which to derive pagination.
      if (recordId == 0 && page == _returnPageNumber) url = _returnPageRequest;
      if (VRCUrl.IsNullOrEmpty(url)) return 0;
      VRCUrl previous = _lastSuccessfulRequest;
      int previousPage = _result.Page;
      if (!BeginSearch(url, _result.Keyword, page)) return 2;
      _returnPageRequest = previous;
      _returnPageNumber = previousPage;
      return 1;
    }

    private VRCUrl _activeRequest;

    private bool BeginSearch(VRCUrl url, string keyword, int page)
    {
      if (_loading || Time.time < _nextRequestTime || !Utilities.IsValid(_uiBehaviour)) return false;
      if (VRCUrl.IsNullOrEmpty(url)) return false;
      _pendingPage = page;
      _pendingKeyword = keyword;
      _activeRequest = url;
      _requestUrl = url.Get();
      _nextRequestTime = Time.time + 5.1f;
      _loading = true;
      _uiBehaviour.OnSearchStarted(keyword, page);
      VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
      PrintLog($"Searching bilibili videos: {url.Get()}");
      return true;
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
      if (!_loading || result.Url.Get() != _requestUrl) return;
      _loading = false;
      ParseResults(result.Result, _pendingKeyword, _pendingPage);
      if (!Utilities.IsValid(_result)) return;
      if (string.IsNullOrEmpty(_result.Error))
      {
        _lastSuccessfulUrl = _requestUrl;
        _lastSuccessfulRequest = _activeRequest;
      }
      if (Utilities.IsValid(_uiBehaviour)) _uiBehaviour.OnSearchCompleted(_result);
      PrintLog($"Search completed: {_result.Count} result(s) on page {_result.Page}.");
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
      if (!_loading || result.Url.Get() != _requestUrl) return;
      _loading = false;
      string error = result.Error;

      if (!Utilities.IsValid(_result))
      {
        _result = GetComponentInChildren<BilibiliSearchResult>();
      }
      if (Utilities.IsValid(_result))
      {
        _result.Error = error;
      }

      if (Utilities.IsValid(_uiBehaviour)) _uiBehaviour.OnSearchFailed(_result);
      PrintLog($"Failed to search bilibili videos: {error}");
    }

    private void ParseResults(string body, string keyword, int page)
    {
      if (!Utilities.IsValid(_result))
      {
        _result = GetComponentInChildren<BilibiliSearchResult>();
      }
      if (!Utilities.IsValid(_result)) return;

      DataList list = ExtractList(body);
      if (list == null)
      {
        _result.Error = string.IsNullOrEmpty(body)
          ? "No response body. The download callback did not receive the token."
          : "Response is not a json array.";
        return;
      }

      _result.Clear();
      _result.Page = page;
      _result.Keyword = keyword;
      int total = list.Count;
      if (total < 0) total = 0;

      // Retain only BV rows for copying and result identity checks. Pagination below
      // still scans the full original response before display filtering and truncation.
      int count = 0;
      for (int i = 0; i < total; i++)
      {
        if (count >= _maxResults) break;
        DataDictionary item = ReadItem(list, i);
        if (item == null) continue;
        if (!BiliUrlUtility.IsBv(ReadString(item, "id"))) continue;
        count++;
      }

      // Pagination belongs to the RAW response, before BV filtering or UI limits.
      // Refuse to guess if the response does not contain a contiguous record block.
      int lastRecord = 0;
      bool paginationValid = total > 0;
      for (int i = 0; i < total; i++)
      {
        int record = ReadRecordId(ReadItem(list, i));
        if (record < 1 || (i > 0 && record != lastRecord + 1)) paginationValid = false;
        lastRecord = record;
      }
      if (paginationValid && lastRecord <= 2147483645)
      {
        _result.PreviousRecordId = lastRecord + 1;
        _result.NextRecordId = lastRecord + 2;
      }

      int[] recordIds = new int[count];
      string[] ids = new string[count];
      string[] titles = new string[count];
      string[] channels = new string[count];
      string[] descriptions = new string[count];
      string[] covers = new string[count];

      int written = 0;
      for (int i = 0; i < total && written < count; i++)
      {
        DataDictionary item = ReadItem(list, i);
        if (item == null) continue;

        string id = ReadString(item, "id");
        if (!BiliUrlUtility.IsBv(id)) continue;

        recordIds[written] = ReadRecordId(item);
        ids[written] = id;
        titles[written] = ReadString(item, "title");
        channels[written] = ReadString(item, "channelTitle");
        descriptions[written] = ReadString(item, "description");
        covers[written] = ReadString(item, "image");
        written++;
      }

      _result.Count = written;
      _result.RecordIds = recordIds;
      _result.Ids = ids;
      _result.Titles = titles;
      _result.Channels = channels;
      _result.Descriptions = descriptions;
      _result.Covers = covers;
    }

    private DataDictionary ReadItem(DataList list, int index)
    {
      if (list == null) return null;
      DataToken itemToken = list[index];
      if (itemToken.TokenType != TokenType.DataDictionary) return null;
      return itemToken.DataDictionary;
    }

    /// <summary>
    /// Accepts both a bare json array and the common { "data": [...] } wrapper.
    /// </summary>
    private DataList ExtractList(string body)
    {
      if (string.IsNullOrEmpty(body)) return null;
      if (!VRCJson.TryDeserializeFromJson(body, out DataToken token)) return null;

      if (token.TokenType == TokenType.DataList) return token.DataList;

      if (token.TokenType == TokenType.DataDictionary)
      {
        DataDictionary root = token.DataDictionary;
        DataToken data;
        if (root.TryGetValue("data", out data) && data.TokenType == TokenType.DataList) return data.DataList;
        if (root.TryGetValue("result", out data) && data.TokenType == TokenType.DataList) return data.DataList;
        if (root.TryGetValue("list", out data) && data.TokenType == TokenType.DataList) return data.DataList;
      }

      return null;
    }

    private int ReadRecordId(DataDictionary item)
    {
      if (item == null) return 0;
      DataToken value;
      if (!item.TryGetValue("recordsid", out value)) return 0;
      if (value.TokenType == TokenType.String)
      {
        string raw = value.String;
        if (string.IsNullOrEmpty(raw) || raw.Length > 10) return 0;
        for (int i = 0; i < raw.Length; i++) if (raw[i] < '0' || raw[i] > '9') return 0;
        int parsed;
        return int.TryParse(raw, out parsed) && parsed > 0 ? parsed : 0;
      }
      if (value.TokenType == TokenType.Double)
      {
        double number = value.Double;
        if (number >= 1 && number <= 2147483647 && number == System.Math.Floor(number)) return (int)number;
      }
      return 0;
    }

    private string ReadString(DataDictionary item, string key)
    {
      DataToken value;
      if (!item.TryGetValue(key, out value)) return string.Empty;
      if (value.TokenType == TokenType.String) return value.String;
      return string.Empty;
    }
  }
}
