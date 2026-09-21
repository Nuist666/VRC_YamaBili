using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using Yamadev.YamaStream.UI;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// Draws the bilibili video search panel and drives the search requests.
  /// UI children are resolved by name so that the panel prefab can be rebuilt freely.
  /// </summary>
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliSearchUI : YamaPlayerListener
  {
    private const string UrlPrefix = "https://";
    private const string KeywordMarker = "keyword=";
    /// <summary>Seconds the queue button of a video stays disabled after it was used.</summary>
    private const float QueueCooldown = 10f;

    [Header("Bilibili Search - Components")]
    [SerializeField] private BilibiliSearch _search;
    [SerializeField] private BilibiliSearchResult _result;
    [SerializeField] private BilibiliSearchService _service;
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private GameObject _loadingIndicator;

    [Header("Bilibili Search - UI")]
    [Tooltip("Player pasted request url. VRChat only allows the player to author a VRCUrl.")]
    [SerializeField] private VRCUrlInputField _searchInput;
    [Tooltip("Filled into the url box on start so the player only has to append the keyword. " +
             "It is a serialized field because Udon cannot build a VRCUrl from a string.")]
    [SerializeField] private VRCUrl _defaultSearchUrl;
    [SerializeField] private Text _pageText;
    [SerializeField] private Text _statusText;
    [SerializeField] private BilibiliResultList _resultsScroll;
    [SerializeField] private InputField _copyField;
    [Tooltip("Index of the result the currently clicked cell action belongs to. Set at runtime.")]
    [SerializeField] private int _actionIndex = -1;

    [SerializeField] private GameObject _linkDialog;
    [Tooltip("Overlay with the module version, author and changelog, opened from the button " +
             "next to the title.")]
    [SerializeField] private GameObject _versionPanel;
    [SerializeField] private VRCUrlInputField _confirmInput;
    [SerializeField] private Text _linkHint;
    [Tooltip("Confirm button of the link dialog. It is irrelevant for the copy action, " +
             "where close is the only meaningful choice, so it is hidden for that action.")]
    [SerializeField] private GameObject _confirmButton;

    [Header("Bilibili Search - Tabs")]
    [Tooltip("Keyword search page: the request url box and the result list.")]
    [SerializeField] private GameObject _searchTab;
    [Tooltip("Url page: a request url box the player completes with a bilibili link, plus a " +
             "play button and a hint. Shown by the second tab.")]
    [SerializeField] private GameObject _urlTab;
    [Tooltip("Request url box of the url page. Pre-filled with the playback prefix, and reset to " +
             "it on every click.")]
    [SerializeField] private VRCUrlInputField _urlInput;
    [SerializeField] private Text _urlHint;
    [Tooltip("Explanation of the keyword search page, shown under the url hint.")]
    [SerializeField] private Text _searchHint;
    [Tooltip("Playback prefix of the url page, e.g. https://bili.example.com/player/?url=. " +
             "It is a serialized field because Udon cannot build a VRCUrl from a string.")]
    [SerializeField] private VRCUrl _defaultPlayUrl;

    private string _expectedLink = "";
    private string _loadedUrl = "";
    private int _linkAction;
    private int _playIndex;
    private float _nextPlayTime;
    /// <summary>Per result: the time the queue button becomes usable again.</summary>
    private float[] _queueReadyAt = new float[0];
    /// <summary>Per result: the play url the player already confirmed, reusable for the queue.</summary>
    private VRCUrl[] _confirmedUrls = new VRCUrl[0];
    private bool[] _confirmedUrlValid = new bool[0];
    private UIController _uiController;
    private Controller _controller;
    private bool _initialized;
    private bool _searching;
    private ScrollRect _scrollRect;
    /// <summary>
    /// Font the panel puts back after YamaPlayer switched the language font. Wired by the
    /// setup tool to the same font the panel is authored with.
    /// </summary>
    [Tooltip("Font restored on every text of the panel after the language changed.")]
    [SerializeField] private Font _panelFont;

    private int _resultsCount;
    private int _currentPage = 1;
    private string _keyword = string.Empty;
    private string[] _ids = new string[0];
    private string[] _titles = new string[0];
    private string[] _channels = new string[0];
    private string[] _descriptions = new string[0];
    private int[] _visibleIndexes = new int[0];
    private int _scrollLineCountCache;

    public int ResultsCount => _resultsCount;
    public int CurrentPage => _currentPage;
    public string Keyword => _keyword;

    /// <summary>
    /// The player authored request url. VRChat does not let a script create a VRCUrl from a
    /// string, so the value has to come from the input field the player typed into.
    /// </summary>
    public VRCUrl RequestUrl
    {
      get
      {
        if (!Utilities.IsValid(_searchInput)) return null;
        return _searchInput.GetUrl();
      }
    }

    public int ActionIndex
    {
      get => _actionIndex;
      set => _actionIndex = value;
    }

    private void Start()
    {
      _uiController = GetComponentInParent<UIController>();
      if (Utilities.IsValid(_uiController)) _uiController.AddListener(this);

      if (!Utilities.IsValid(_panelRoot)) _panelRoot = gameObject;

      ResolveReferences();
      if (Utilities.IsValid(_search)) _search.SetActiveUI(this);
      RestorePanelFont();
      ApplyDefaultUrl();
      UpdateTranslation();
      ClearStatus();
      ShowPanel(false);
    }

    #region Tabs

    /// <summary>Shows the keyword search page.</summary>
    public void ShowSearchTab() { ShowTab(true); }

    /// <summary>Shows the url page, with the playback prefix already in its box.</summary>
    public void ShowUrlTab() { ShowTab(false); }

    private void ShowTab(bool search)
    {
      if (Utilities.IsValid(_searchTab)) _searchTab.SetActive(search);
      if (Utilities.IsValid(_urlTab)) _urlTab.SetActive(!search);
      // The page counter belongs to the result list only.
      if (Utilities.IsValid(_pageText)) _pageText.gameObject.SetActive(search);

      if (!search) { ResetUrlInput(); return; }

      // The list itself stays inactive until the page it lives on had a frame to lay out. Its
      // The result list sizes the row pool from the viewport height once, the first time it runs, and a
      // pool measured before that height exists stays a single row tall - which is exactly what a
      // stock YamaPlayer does. Activating the list a frame late, and only then setting it up, makes
      // the pool come out right with the stock component as well.
      if (Utilities.IsValid(_resultsScroll) && !_resultsScroll.gameObject.activeSelf)
      {
        SendCustomEventDelayedFrames(nameof(ActivateResults), 1);
        return;
      }

      SendCustomEventDelayedFrames(nameof(RefreshResults), 2);
    }

    /// <summary>
    /// Shows the result list, then sets it up. Called one frame after its page became visible, see
    /// ShowTab.
    /// </summary>
    public void ActivateResults()
    {
      if (Utilities.IsValid(_resultsScroll)) _resultsScroll.gameObject.SetActive(true);
      SendCustomEventDelayedFrames(nameof(RefreshResults), 1);
    }

    /// <summary>
    /// Puts the playback prefix into the url page's box. Wired to a pointer down trigger on the
    /// box, so every click starts from the prefix again - the player only appends the bilibili
    /// link. Also called when the url page is opened, because the box is only activated then and
    /// VRCUrlInputField clears whatever the prefab was saved with on its first activation.
    /// </summary>
    public void ResetUrlInput()
    {
      if (!Utilities.IsValid(_urlInput)) return;
      if (VRCUrl.IsNullOrEmpty(_defaultPlayUrl)) return;
      _urlInput.SetUrl(_defaultPlayUrl);
    }

    /// <summary>
    /// Plays the url the player completed on the url page. No confirmation dialog is needed here:
    /// the url box of that page is authored by the player, which is the only thing VRChat asks for.
    /// </summary>
    /// <remarks>
    /// Whatever the player left in the box is handed to the backend as it is. The backend resolves
    /// both a bilibili link and a bare BV id, so the panel does not second guess the input any more
    /// - only an empty box is refused. The resolver prefix has to stay in front of it though, and
    /// only the player can keep it there: Udon cannot build a VRCUrl from a string, so a script
    /// cannot add the prefix to what was pasted. Clicking the box puts the prefix back in.
    /// </remarks>
    public void PlayUrlInput()
    {
      if (_searching || Time.time < _nextPlayTime) return;
      if (!Utilities.IsValid(_urlInput) || !Utilities.IsValid(_search) || !Utilities.IsValid(_service))
      {
        SetStatusKey("module.bilibilisearch.msg.needRebuild");
        return;
      }

      VRCUrl url = _urlInput.GetUrl();
      if (VRCUrl.IsNullOrEmpty(url)) { SetStatusKey("module.bilibilisearch.msg.emptyUrl"); return; }

      // The track has no name yet, so the BV id of the request stands in for it when there is one.
      string id = BiliUrlUtility.VideoId(url.Get());
      int outcome = _search.PlayTrack(this, url, string.IsNullOrEmpty(id) ? "BiliBili" : id);
      if (outcome == 0) { SetStatusKey("module.bilibilisearch.msg.urlPlayFailed"); return; }
      _nextPlayTime = Time.time + 5.1f;

      if (outcome == 2)
      {
        // A video is already playing, so the track went to the queue instead.
        SetStatusKey("module.bilibilisearch.msg.queuedWhilePlaying");
        return;
      }

      SetStatusKey("module.bilibilisearch.msg.playing");
      ShowPanel(false);
    }

    #endregion

    /// <summary>
    /// Pre-fills the request url so the player only has to append the search keyword.
    /// An url the player already typed (or that survived from a previous session) wins.
    /// </summary>
    private void ApplyDefaultUrl()
    {
      if (!Utilities.IsValid(_searchInput)) return;
      if (VRCUrl.IsNullOrEmpty(_defaultSearchUrl)) return;
      if (!VRCUrl.IsNullOrEmpty(_searchInput.GetUrl())) return;
      _searchInput.SetUrl(_defaultSearchUrl);
    }

    /// <summary>
    /// Puts the default request url back into the search box. Wired to a pointer down trigger
    /// on the box itself, so opening it again after a search starts from the default instead of
    /// the previous request url. SetUrl also moves the caret to the end of the text, which is
    /// where the keyword has to be typed.
    /// </summary>
    public void ResetSearchInput()
    {
      if (!Utilities.IsValid(_searchInput)) return;
      if (VRCUrl.IsNullOrEmpty(_defaultSearchUrl)) return;
      _searchInput.SetUrl(_defaultSearchUrl);
    }

    #region Setup

    private void ResolveReferences()
    {
      if (!Utilities.IsValid(_panelRoot)) _panelRoot = gameObject;

      if (!Utilities.IsValid(_searchInput)) _searchInput = FindUrlInputField("SearchInput");
      if (!Utilities.IsValid(_pageText)) _pageText = FindText("PageText");
      if (!Utilities.IsValid(_statusText)) _statusText = FindText("StatusText");
      if (!Utilities.IsValid(_loadingIndicator))
      {
        Transform loading = FindTransform("Loading");
        if (Utilities.IsValid(loading)) _loadingIndicator = loading.gameObject;
      }
      if (!Utilities.IsValid(_resultsScroll)) _resultsScroll = GetComponentInChildren<BilibiliResultList>(true);
      if (Utilities.IsValid(_search))
      {
        _service = _search.Service;
        _result = _search.Result;
        _controller = _search.Controller;
      }

      // Reached through the module so a panel that is not parented under the player still works.
      if (!Utilities.IsValid(_controller))
      {
        _controller = GetComponentInParent<Controller>();
        if (!Utilities.IsValid(_controller) && Utilities.IsValid(_search)) _controller = _search.Controller;
      }

      if (Utilities.IsValid(_resultsScroll))
      {
        // Force the result list to build its cells before the first SetUp call.
        _scrollLineCountCache = _resultsScroll.LineCount;
        _scrollRect = _resultsScroll.GetComponent<ScrollRect>();
      }

      _initialized = Utilities.IsValid(_search) && Utilities.IsValid(_service) && Utilities.IsValid(_result) && Utilities.IsValid(_resultsScroll) && Utilities.IsValid(_searchInput);
      if (!_initialized) PrintError("Bilibili search panel is not wired correctly. Re-run the panel setup tool.");
    }

    #endregion

    #region Child lookup

    private Transform FindTransform(string childName)
    {
      // Searched from the panel root, not from _panelRoot: the link dialog is a sibling of the
      // panel background, so it would be invisible to a lookup that starts at the background.
      Transform[] transforms = GetComponentsInChildren<Transform>(true);
      if (!Utilities.IsValid(transforms)) return null;

      int len = transforms.Length;
      for (int i = 0; i < len; i++)
      {
        Transform current = transforms[i];
        if (!Utilities.IsValid(current)) continue;
        if (current.gameObject.name == childName) return current;
      }
      return null;
    }

    // UdonSharp does not support generic method declarations on behaviours,
    // so every lookup is written out with its concrete type.
    private InputField FindInputField(string childName)
    {
      Transform found = FindTransform(childName);
      if (!Utilities.IsValid(found)) return null;
      return found.GetComponent<InputField>();
    }

    private VRCUrlInputField FindUrlInputField(string childName)
    {
      Transform found = FindTransform(childName);
      if (!Utilities.IsValid(found)) return null;
      return found.GetComponent<VRCUrlInputField>();
    }

    private Text FindText(string childName)
    {
      Transform found = FindTransform(childName);
      if (!Utilities.IsValid(found)) return null;
      return found.GetComponent<Text>();
    }

    #endregion

    #region Panel visibility

    public void ShowPanel(bool visible)
    {
      if (!Utilities.IsValid(_panelRoot)) return;
      _panelRoot.SetActive(visible);
      if (!visible)
      {
        // The version overlay belongs to the panel, it must not stay behind on its own.
        if (Utilities.IsValid(_versionPanel)) _versionPanel.SetActive(false);
        return;
      }

      // Awake of the url input runs on the first activation and clears the saved text.
      ApplyDefaultUrl();

      // The panel always opens on the url page: pasting a bilibili link is the quick action, the
      // keyword search is one click away. It also fills the url box, which only becomes active now.
      // The result list is deliberately NOT set up here: it lives on the hidden search page, and a
      // list that is initialized while its viewport has no height keeps a row pool of one
      // row for good. It is set up when the search page is opened instead (see ShowTab).
      ShowUrlTab();
    }

    /// <summary>Opens the version overlay, called by the button next to the title.</summary>
    public void OpenVersionPanel()
    {
      if (!Utilities.IsValid(_versionPanel)) return;
      _versionPanel.SetActive(true);
      _versionPanel.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Closes the version overlay and goes back to the search panel, so the back button always
    /// lands on the bilibili search screen.
    /// </summary>
    public void CloseVersionPanel()
    {
      if (Utilities.IsValid(_versionPanel)) _versionPanel.SetActive(false);
      ShowPanel(true);
    }

    public void OpenPanel() => ShowPanel(true);

    public void ClosePanel() => ShowPanel(false);

    /// <summary>Entry point of the launcher button on the main page.</summary>
    public void TogglePanel()
    {
      ShowPanel(Utilities.IsValid(_panelRoot) && !_panelRoot.activeSelf);
    }

    public void SetSearchTarget(BilibiliSearch target, BilibiliSearchResult result)
    {
      _search = target;
      _result = result;
      ResolveReferences();
    }

    #endregion

    #region Search

    /// <summary>
    /// Pulls the keyword out of the url the player pasted, e.g.
    /// https://bili.example.com/player/?page=1&amp;keyword=test -> "test".
    /// A bare keyword is accepted as well.
    /// </summary>
    private string ExtractKeyword() => BiliUrlUtility.Keyword(RequestUrl.Get());

    public void SearchByInput()
    {
      ResolveReferences();
      if (!_initialized || _searching) return;
      int page = BiliUrlUtility.Page(RequestUrl.Get());
      if (page == 0) { SetStatusKey("module.bilibilisearch.msg.invalidUrl"); return; }
      _search.SetActiveUI(this);
      _search.Search(ExtractKeyword(), page);
    }

    public void NextPage() { if (_resultsCount > 0) PreparePage(_currentPage + 1); }
    public void PreviousPage() { if (_currentPage > 1) PreparePage(_currentPage - 1); }
    public void RetrySearch() { SearchByInput(); }

    private void PreparePage(int page)
    {
      if (!_initialized || _searching) return;
      string url = BiliUrlUtility.ChangePage(_loadedUrl, page);
      if (url == "") return;
      ShowLink(url, 1);
    }

    public void OnSearchStarted(string keyword, int page)
    {
      _searching = true;
      if (Utilities.IsValid(_linkDialog)) _linkDialog.SetActive(false);
      SetStatusKey("module.bilibilisearch.msg.searching");
      if (Utilities.IsValid(_loadingIndicator)) _loadingIndicator.SetActive(true);
      UpdatePageText();
    }

    public void OnSearchCompleted(BilibiliSearchResult result)
    {
      _searching = false;
      if (Utilities.IsValid(_loadingIndicator)) _loadingIndicator.SetActive(false);

      if (!Utilities.IsValid(result))
      {
        SetStatusKey("module.bilibilisearch.msg.failed");
        return;
      }

      if (!string.IsNullOrEmpty(result.Error)) { OnSearchFailed(result); return; }
      _loadedUrl = _service.LastSuccessfulUrl;
      ApplyResult(result);
      if (Utilities.IsValid(_resultsScroll)) _resultsScroll.ScrollToTop();
    }

    public void OnSearchFailed(BilibiliSearchResult result)
    {
      _searching = false;
      if (Utilities.IsValid(_loadingIndicator)) _loadingIndicator.SetActive(false);
      // Keep the previous successful page on transport/JSON failures.

      string error = Utilities.IsValid(result) ? result.Error : string.Empty;
      if (!string.IsNullOrEmpty(error))
      {
        SetStatusError("module.bilibilisearch.msg.failed", error);
      }
      else
      {
        SetStatusKey("module.bilibilisearch.msg.failed");
      }
    }

    private void ApplyResult(BilibiliSearchResult result)
    {
      _keyword = result.Keyword;
      _currentPage = result.Page;
      _resultsCount = result.Count;
      _ids = result.Ids;
      _titles = result.Titles;
      _channels = result.Channels;
      _descriptions = result.Descriptions;
      ResetActionState(_resultsCount);

      UpdatePageText();
      RefreshResults();

      if (_resultsCount == 0)
      {
        SetStatusKey("module.bilibilisearch.msg.noResult");
      }
      else
      {
        SetStatusCount("module.bilibilisearch.msg.resultCount", _resultsCount);
      }
    }

    /// <summary>
    /// Drops the queue cooldown and the cached urls, because every index now points at a
    /// different video.
    /// </summary>
    private void ResetActionState(int count)
    {
      if (count < 0) count = 0;
      _queueReadyAt = new float[count];
      _confirmedUrls = new VRCUrl[count];
      _confirmedUrlValid = new bool[count];
    }

    private void ClearResults()
    {
      _resultsCount = 0;
      _ids = new string[0];
      _titles = new string[0];
      _channels = new string[0];
      _descriptions = new string[0];
      ResetActionState(0);
      RefreshResults();
    }

    private void UpdatePageText()
    {
      if (Utilities.IsValid(_pageText))
      {
        _pageText.text = string.Concat(GetTranslation("module.bilibilisearch.page"), " ", _currentPage.ToString());
      }
    }

    /// <summary>
    /// The status line is kept as a translation key plus the raw text around it, so switching
    /// the language re-renders it instead of leaving the message in the language it was
    /// written in.
    /// </summary>
    private string _statusKey = string.Empty;
    private string _statusPrefix = string.Empty;
    private string _statusSuffix = string.Empty;

    private void SetStatusKey(string key)
    {
      SetStatusParts(key, string.Empty, string.Empty);
    }

    /// <summary>Status with a leading number, e.g. "20 video(s)".</summary>
    private void SetStatusCount(string key, int count)
    {
      SetStatusParts(key, string.Concat(count.ToString(), " "), string.Empty);
    }

    /// <summary>Status with a trailing detail, e.g. the error of a failed request.</summary>
    private void SetStatusError(string key, string error)
    {
      SetStatusParts(key, string.Empty, string.IsNullOrEmpty(error) ? string.Empty : string.Concat(" ", error));
    }

    private void ClearStatus()
    {
      SetStatusParts(string.Empty, string.Empty, string.Empty);
    }

    private void SetStatusParts(string key, string prefix, string suffix)
    {
      _statusKey = key;
      _statusPrefix = prefix;
      _statusSuffix = suffix;
      ApplyStatus();
    }

    private void ApplyStatus()
    {
      if (!Utilities.IsValid(_statusText)) return;
      _statusText.text = string.Concat(_statusPrefix, GetTranslation(_statusKey), _statusSuffix);
    }

    #endregion

    #region Results view

    public void RefreshResults()
    {
      if (!_initialized || !Utilities.IsValid(_resultsScroll)) return;
      // While the search page is hidden the list is not set up: a list that runs without a
      // laid out viewport would keep a row pool of one row (see ShowTab).
      if (!_resultsScroll.gameObject.activeSelf) return;

      int lineCount = _resultsScroll.LineCount;
      _visibleIndexes = new int[lineCount];
      for (int i = 0; i < lineCount; i++) _visibleIndexes[i] = -2;

      _resultsScroll.SetUp(_resultsCount, this, nameof(UpdateResultsContent));
    }

    public void UpdateResultsContent()
    {
      if (!_initialized || !Utilities.IsValid(_resultsScroll)) return;

      int lineCount = _resultsScroll.LineCount;
      if (_visibleIndexes.Length != lineCount)
      {
        _visibleIndexes = new int[lineCount];
        for (int i = 0; i < lineCount; i++) _visibleIndexes[i] = -2;
      }

      int[] indexes = _resultsScroll.Indexes;
      for (int i = 0; i < lineCount; i++)
      {
        int index = indexes[i];
        if (_visibleIndexes[i] == index) continue;
        _visibleIndexes[i] = index;

        if (index < 0 || index >= _resultsCount) continue;

        Transform cell = GetCell(i);
        if (!Utilities.IsValid(cell)) continue;

        UpdateCell(cell, index);
      }
    }

    private Transform GetCell(int lineIndex)
    {
      if (!Utilities.IsValid(_scrollRect) || !Utilities.IsValid(_scrollRect.content)) return null;
      if (lineIndex < 0 || lineIndex >= _scrollRect.content.childCount) return null;
      return _scrollRect.content.GetChild(lineIndex);
    }

    private void UpdateCell(Transform cell, int index)
    {
      string title = _titles[index];
      Text titleText = FindCellText(cell, "Title");
      if (Utilities.IsValid(titleText)) titleText.text = string.IsNullOrEmpty(title) ? _ids[index] : title;

      Text channelText = FindCellText(cell, "Channel");
      if (Utilities.IsValid(channelText)) channelText.text = _channels[index];

      Text descriptionText = FindCellText(cell, "Description");
      if (Utilities.IsValid(descriptionText)) descriptionText.text = _descriptions[index];

      Text idText = FindCellText(cell, "Id");
      if (Utilities.IsValid(idText)) idText.text = _ids[index];

      // The button labels are authored into the prefab, and the name based pass in
      // UpdateTranslation only ever reaches the first cell (the template). Every pooled clone
      // therefore has to be translated while it is being populated, otherwise every row except
      // the first keeps the language the panel was generated in.
      SetCellLabel(cell, "CopyButtonText", "button.copyUrl");
      SetCellLabel(cell, "PlayButtonText", "button.playVideo");
      SetCellLabel(cell, "QueueButtonText", "button.addQueue");

      SetUpResultActions(cell, index);
    }

    private void SetCellLabel(Transform cell, string childName, string key)
    {
      Text text = FindCellText(cell, childName);
      if (Utilities.IsValid(text)) text.text = GetTranslation(key);
    }

    /// <summary>
    /// Tells every button of a cell which result it belongs to.
    /// </summary>
    private void SetUpResultActions(Transform cell, int index)
    {
      Transform actions = cell.Find("Actions");
      if (!Utilities.IsValid(actions)) return;

      BilibiliSearchResultAction[] handlers = actions.GetComponentsInChildren<BilibiliSearchResultAction>(true);
      if (!Utilities.IsValid(handlers)) return;

      int len = handlers.Length;
      for (int i = 0; i < len; i++)
      {
        BilibiliSearchResultAction handler = handlers[i];
        if (Utilities.IsValid(handler)) handler.SetUp(this, index);
      }
    }

    /// <summary>
    /// Looks a labelled text up anywhere below the cell: the cell nests its texts (title,
    /// then the uploader/BV line, then the summary), so a direct child lookup is not enough.
    /// </summary>
    private Text FindCellText(Transform cell, string childName)
    {
      if (!Utilities.IsValid(cell)) return null;
      Transform[] transforms = cell.GetComponentsInChildren<Transform>(true);
      if (!Utilities.IsValid(transforms)) return null;

      int len = transforms.Length;
      for (int i = 0; i < len; i++)
      {
        Transform current = transforms[i];
        if (!Utilities.IsValid(current)) continue;
        if (current.gameObject.name != childName) continue;
        return current.GetComponent<Text>();
      }
      return null;
    }

    #endregion

    #region Result actions

    public void CopyLink()
    {
      if (_actionIndex < 0 || _actionIndex >= _ids.Length || !Utilities.IsValid(_service)) return;
      if (!BiliUrlUtility.IsBv(_ids[_actionIndex])) { SetStatusKey("module.bilibilisearch.msg.invalidBv"); return; }
      ShowLink(_service.GetBilibiliVideoUrl(_ids[_actionIndex]), 0);
    }

    public void CopyText(string text) { ShowLink(text, 0); }

    private void ShowLink(string url, int action)
    {
      if (!Utilities.IsValid(_linkDialog) || !Utilities.IsValid(_copyField)) { SetStatusKey("module.bilibilisearch.msg.needRebuild"); return; }
      _expectedLink = url;
      _linkAction = action;
      _linkDialog.SetActive(true);
      _copyField.text = url;
      _confirmInput.SetUrl(VRCUrl.Empty);
      _confirmInput.gameObject.SetActive(action != 0);
      // Copying is finished by closing the dialog, so "confirm" would do the same thing.
      if (Utilities.IsValid(_confirmButton)) _confirmButton.SetActive(action != 0);
      if (action == 0) _linkHint.text = GetTranslation("module.bilibilisearch.hint.copy");
      else if (action == 1) _linkHint.text = GetTranslation("module.bilibilisearch.hint.page");
      else if (action == 3) _linkHint.text = GetTranslation("module.bilibilisearch.hint.queue");
      else _linkHint.text = GetTranslation("module.bilibilisearch.hint.play");
    }

    public void CloseLink() { if (Utilities.IsValid(_linkDialog)) _linkDialog.SetActive(false); }
    public void Play() { PreparePlay(_actionIndex); }

    public void PreparePlay(int index)
    {
      if (_searching || index < 0 || index >= _ids.Length || !Utilities.IsValid(_service)) return;
      if (!BiliUrlUtility.IsBv(_ids[index])) { SetStatusKey("module.bilibilisearch.msg.invalidBv"); return; }
      _playIndex = index;
      ShowLink(_service.BuildPlayUrl(_service.GetBilibiliVideoUrl(_ids[index])), 2);
    }

    public void ConfirmLink()
    {
      if (_linkAction == 0) { CloseLink(); return; }
      VRCUrl url = _confirmInput.GetUrl();
      if (url.Get() != _expectedLink) { _linkHint.text = GetTranslation("module.bilibilisearch.hint.mismatch"); return; }
      if (_linkAction == 1)
      {
        if (_searching || _service.IsLoading) return;
        _searchInput.SetUrl(url);
        CloseLink();
        SearchByInput();
      }
      else if (_linkAction == 3)
      {
        if (!QueueWith(url, _playIndex)) { _linkHint.text = GetTranslation("module.bilibilisearch.hint.playFailed"); return; }
        CloseLink();
      }
      else if (_linkAction == 2)
      {
        if (_searching || Time.time < _nextPlayTime) return;
        int outcome = _search.PlayConfirmed(this, _playIndex, url);
        if (outcome == 0) { _linkHint.text = GetTranslation("module.bilibilisearch.hint.playFailed"); return; }
        _nextPlayTime = Time.time + 5.1f;
        RememberUrl(_playIndex, url);

        if (outcome == 2)
        {
          // A video is already playing, so the track went to the queue instead. The panel
          // stays open so the player can see the status and keep browsing.
          _queueReadyAt[_playIndex] = Time.time + QueueCooldown;
          CloseLink();
          SetStatusKey("module.bilibilisearch.msg.queuedWhilePlaying");
          return;
        }

        // The play request went through, so both the confirmation dialog and the search
        // panel are closed and the player is back on the main page.
        SetStatusKey("module.bilibilisearch.msg.playing");
        CloseLink();
        ShowPanel(false);
      }
    }

    public bool CheckPlayPermission()
    {
      return Utilities.IsValid(_uiController) && _uiController.InvokeBeforeEvent("BeforeUserPlayTrack");
    }

    public bool CheckQueuePermission()
    {
      return Utilities.IsValid(_uiController) && _uiController.InvokeBeforeEvent("BeforeUserAddTrackToQueue");
    }

    #endregion

    #region Queue

    /// <summary>False while the queue button of that result has to stay disabled.</summary>
    public bool CanQueue(int index)
    {
      if (index < 0 || index >= _queueReadyAt.Length) return false;
      return Time.time >= _queueReadyAt[index];
    }

    /// <summary>
    /// Entry point of the queue button of a result cell.
    /// </summary>
    public void AddToQueue()
    {
      ResolveReferences();
      int index = _actionIndex;
      if (!_initialized || index < 0 || index >= _ids.Length) return;
      if (!BiliUrlUtility.IsBv(_ids[index])) { SetStatusKey("module.bilibilisearch.msg.invalidBv"); return; }
      if (!CanQueue(index)) return;

      // A url the player already confirmed for this result makes the button a single click.
      // Otherwise the player has to paste the play url, because VRChat only lets the player
      // author a VRCUrl and a script cannot build one from a string.
      if (index < _confirmedUrlValid.Length && _confirmedUrlValid[index] &&
          Utilities.IsValid(_confirmedUrls[index]) && QueueWith(_confirmedUrls[index], index)) return;

      PrepareQueue(index);
    }

    /// <summary>Opens the confirmation dialog for the queue action.</summary>
    private void PrepareQueue(int index)
    {
      if (_searching || index < 0 || index >= _ids.Length || !Utilities.IsValid(_service)) return;
      _playIndex = index;
      ShowLink(_service.BuildPlayUrl(_service.GetBilibiliVideoUrl(_ids[index])), 3);
    }

    private bool QueueWith(VRCUrl url, int index)
    {
      if (index < 0 || index >= _queueReadyAt.Length || !Utilities.IsValid(_search)) return false;
      if (!_search.QueueConfirmed(this, index, url)) return false;

      RememberUrl(index, url);
      _queueReadyAt[index] = Time.time + QueueCooldown;
      SetStatusKey("module.bilibilisearch.msg.queued");
      // Update the pooled cells so the disabled queue button shows up right away.
      ForceRefreshVisibleCells();
      return true;
    }

    private void RememberUrl(int index, VRCUrl url)
    {
      if (index < 0 || index >= _confirmedUrls.Length) return;
      _confirmedUrls[index] = url;
      _confirmedUrlValid[index] = true;
    }

    private void ForceRefreshVisibleCells()
    {
      if (!Utilities.IsValid(_visibleIndexes)) return;
      for (int i = 0; i < _visibleIndexes.Length; i++) _visibleIndexes[i] = -2;
      UpdateResultsContent();
    }

    #endregion

    #region Localization

    /// <summary>
    /// YamaPlayer's translation table only contains the module strings after a world build,
    /// so an unresolved key falls back to the built in strings in <see cref="BiliText"/>.
    /// </summary>
    private string GetTranslation(string key)
    {
      if (Utilities.IsValid(_uiController))
      {
        string value = _uiController.GetTranslation(key);
        if (!string.IsNullOrEmpty(value)) return value;
      }
      return BiliText.Get(key);
    }

    private void UpdateTranslation()
    {
      // The title is a fixed brand string and is deliberately not translated. It is authored
      // into the prefab by the setup tool and never written from here.
      SetLabel("SearchPlaceholder", "module.bilibilisearch.placeholder");
      SetLabel("CopyPlaceholder", "module.bilibilisearch.copyField");
      SetLabel("ConfirmPlaceholder", "module.bilibilisearch.confirmPlaceholder");

      SetLabel("SearchButtonText", "button.search");
      SetLabel("PreviousButtonText", "button.previousPage");
      SetLabel("NextButtonText", "button.nextPage");
      SetLabel("CloseButtonText", "button.close");
      // CopyButtonText / PlayButtonText / QueueButtonText live on pooled result cells, where a
      // lookup by name would only ever find the template. See SetCellLabel.
      SetLabel("ConfirmLinkText", "module.bilibilisearch.confirm");
      SetLabel("CancelLinkText", "module.bilibilisearch.cancel");

      // Tabs. The url page is the one that takes a bilibili link instead of a keyword.
      SetLabel("TabSearchButtonText", "module.bilibilisearch.tab.search");
      SetLabel("TabUrlButtonText", "module.bilibilisearch.tab.url");
      SetLabel("UrlPlaceholder", "module.bilibilisearch.urlPlaceholder");
      SetLabel("PlayUrlButtonText", "module.bilibilisearch.urlPlay");
      SetLabel("CloseUrlButtonText", "button.close");
      SetLabel("UrlLabel", "module.bilibilisearch.urlLabel");
      SetLabel("UrlHint", "module.bilibilisearch.urlHint");
      SetLabel("SearchLabel", "module.bilibilisearch.searchLabel");
      SetLabel("SearchHint", "module.bilibilisearch.searchHint");

      // Version overlay. The account values, the project name, the version string and the
      // changelog entries are the module's own data and stay as authored, and the changelog
      // heading is a fixed Japanese string that is authored into the prefab as well. The only
      // translated text left in the overlay is the back button.
      //
      // The back button carries an arrow in front of its label. It is written here rather than
      // authored into the prefab, otherwise a language change would replace the whole text and
      // drop the arrow. The arrow is not CJK, so every font of the panel has the glyph.
      Text backLabel = FindText("VersionBackButtonText");
      if (Utilities.IsValid(backLabel)) backLabel.text = "← " + GetTranslation("module.bilibilisearch.version.back");

      UpdatePageText();
    }

    private void SetLabel(string childName, string key)
    {
      Text text = FindText(childName);
      if (Utilities.IsValid(text)) text.text = GetTranslation(key);
    }

    public void AfterLanguageChanged()
    {
      UpdateTranslation();
      ApplyStatus();
      // The labels of the pooled result cells are written while a cell is populated, so the
      // rows that are on screen now have to be populated again.
      ForceRefreshVisibleCells();
      RestorePanelFont();
    }

    /// <summary>
    /// Puts the panel's own font back on every text of the panel.
    /// </summary>
    /// <remarks>
    /// UIController.UpdateFont assigns the font of the language set to *every* Text below the
    /// UIController, and the panel is injected into that hierarchy. Those fonts do not always
    /// carry the glyphs the panel needs: the Japanese font used for the English, Japanese,
    /// Spanish and Italian sets has no glyph for characters that only exist in Chinese - "哔"
    /// and "哩" among them - so the panel title, and any bilibili title or summary containing
    /// such a character, rendered as if it were empty. The panel's own font (the built in
    /// LegacyRuntime.ttf, the same one YamaPlayer uses for Chinese) does render them, so it is
    /// put back after every language change.
    /// </remarks>
    private void RestorePanelFont()
    {
      if (!Utilities.IsValid(_panelFont)) return;

      Text[] texts = GetComponentsInChildren<Text>(true);
      if (!Utilities.IsValid(texts)) return;

      int len = texts.Length;
      for (int i = 0; i < len; i++)
      {
        Text text = texts[i];
        if (Utilities.IsValid(text)) text.font = _panelFont;
      }
    }

    #endregion
  }
}
