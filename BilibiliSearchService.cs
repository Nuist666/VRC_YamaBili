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
  /// Builds the request urls, downloads the json and parses it into a <see cref="BilibiliSearchResult"/>.
  /// </summary>
  /// <remarks>
  /// Udon does not expose "new VRCUrl(string)", a VRCUrl can only come from a serialized
  /// field or from a VRCUrlInputField. So the request url is assembled as a plain string and
  /// handed to the panel's VRCUrlInputField, which is then passed to VRCStringDownloader.
  /// </remarks>
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliSearchService : YamaPlayerBehaviour
  {
    [Header("Bilibili Search - Endpoints")]
    [Tooltip("Root of the bilibili player service. Query parameters are appended at runtime.")]
    [SerializeField] private VRCUrl _baseUrl;
    [Tooltip("Maximum amount of results shown per page.")]
    [SerializeField, Range(1, 50)] private int _maxResults = 20;

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

    /// <summary>Base url text used to build request urls at runtime.</summary>
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
    /// request url always comes from the player's VRCUrlInputField.
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

    public void Search(string keyword, int page)
    {
      if (_loading || Time.time < _nextRequestTime)
      {
        PrintLog("A search request is already running.");
        return;
      }

      if (!Utilities.IsValid(_uiBehaviour))
      {
        PrintError("Search panel is not wired up.");
        return;
      }

      // The player authored url is the only VRCUrl source available to Udon.
      VRCUrl requestUrl = _uiBehaviour.RequestUrl;
      if (VRCUrl.IsNullOrEmpty(requestUrl) || BiliUrlUtility.Page(requestUrl.Get()) == 0)
      {
        PrintError("Paste a request url into the search box first.");
        return;
      }

      _pendingPage = BiliUrlUtility.Page(requestUrl.Get());
      _pendingKeyword = BiliUrlUtility.Keyword(requestUrl.Get());
      _requestUrl = requestUrl.Get();
      _nextRequestTime = Time.time + 5.1f;
      _loading = true;

      if (Utilities.IsValid(_uiBehaviour)) _uiBehaviour.OnSearchStarted(_pendingKeyword, _pendingPage);

      VRCStringDownloader.LoadUrl(requestUrl, (IUdonEventReceiver)this);
      PrintLog($"Searching bilibili videos: {requestUrl.Get()}");
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
      if (!_loading || result.Url.Get() != _requestUrl) return;
      _loading = false;
      ParseResults(result.Result, _pendingKeyword, _pendingPage);
      if (!Utilities.IsValid(_result)) return;
      if (string.IsNullOrEmpty(_result.Error)) _lastSuccessfulUrl = _requestUrl;
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

      // Entries whose id is not a BV id are dropped instead of being listed: the response can
      // contain av-numbered videos, and a row without a BV id cannot be copied, played or
      // queued (VRChat needs the player to author the url, which is built from the id).
      int count = 0;
      for (int i = 0; i < total; i++)
      {
        if (count >= _maxResults) break;
        DataDictionary item = ReadItem(list, i);
        if (item == null) continue;
        if (!BiliUrlUtility.IsBv(ReadString(item, "id"))) continue;
        count++;
      }

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

        ids[written] = id;
        titles[written] = ReadString(item, "title");
        channels[written] = ReadString(item, "channelTitle");
        descriptions[written] = ReadString(item, "description");
        covers[written] = ReadString(item, "image");
        written++;
      }

      _result.Count = written;
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

    private string ReadString(DataDictionary item, string key)
    {
      DataToken value;
      if (!item.TryGetValue(key, out value)) return string.Empty;
      if (value.TokenType == TokenType.String) return value.String;
      return string.Empty;
    }
  }
}
