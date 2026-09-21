using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// The scrolling result list of the bilibili panel: it pools a row per visible line and reuses
  /// them while the player scrolls.
  /// </summary>
  /// <remarks>
  /// This is the same loop scroll YamaPlayer uses for its own lists, but shipped inside the module
  /// on purpose. YamaPlayer's component measures its row pool once, when the list first runs - which
  /// happens while the page it lives on is being activated and the viewport has no height yet - and
  /// it moves pooled rows without resetting their geometry, so a reused row keeps the offset of the
  /// row it was cloned from. Both show up as a list that draws a single row, or rows that are
  /// shifted sideways. A module cannot patch core code, so it carries its own copy instead: the pool
  /// grows to the height the viewport has now, and every row is stamped with the geometry authored
  /// on the template.
  /// </remarks>
  [RequireComponent(typeof(ScrollRect))]
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliResultList : UdonSharpBehaviour
  {
    [SerializeField] private GameObject _template;
    private ScrollRect _scrollRect;
    private bool _initialized;
    private Vector2 _position;
    private int _lineCount;
    private float _lineHeight;
    private int _length;
    private int[] _lastIndexes;
    private int[] _indexes;
    private UdonSharpBehaviour _callbackUdon;
    private string _callbackEventName;

    // Authored geometry of a row, captured from the template before anything moves it. Every
    // pooled row is stamped with these values, so a clone can never inherit a modified position,
    // size or anchor set from the object it was cloned from.
    private Vector2 _rowAnchorMin;
    private Vector2 _rowAnchorMax;
    private Vector2 _rowPivot;
    private Vector2 _rowAnchoredPosition;
    private Vector2 _rowSizeDelta;

    private const float SCROLL_THRESHOLD_SQR = 0.003f;

    void Start() => Initialize();

    public int LineCount => _lineCount;

    public int Length => _length;

    public void SetUp(int length, UdonSharpBehaviour callbackUdon, string callbackEventName)
    {
      if (!_initialized) Initialize();
      if (!_initialized) return;
      // The pool may have been created while the viewport was not laid out yet (see
      // EnsureCellCount), so make sure it covers the current viewport height as well.
      EnsureCellCount();
      _length = length;
      _callbackUdon = callbackUdon;
      _callbackEventName = callbackEventName;
      ResetValues();
      Render();
    }

    private void ResetValues()
    {
      if (!_initialized) return;

      _indexes = new int[_lineCount].Populate(-1);
      for (int i = 0; i < _lineCount; i++)
      {
        _scrollRect.content.GetChild(i).gameObject.SetActive(false);
      }
    }

    private void Render()
    {
      AdjustHeight();
      UpdateIndexes();
      UpdatePosition();
      InvokeCallback();
    }

    public void ScrollToTop()
    {
      Initialize();
      if (!_initialized) return;
      EnsureCellCount();
      _scrollRect.content.anchoredPosition = new Vector2(_scrollRect.content.anchoredPosition.x, 0);
      Render();
    }

    public int[] LastIndexes => _lastIndexes;
    public int[] Indexes => _indexes;

    private void Initialize()
    {
      if (_initialized) return;
      _scrollRect = GetComponent<ScrollRect>();

      if (!Utilities.IsValid(_template) && Utilities.IsValid(_scrollRect) && Utilities.IsValid(_scrollRect.content))
      {
        if (_scrollRect.content.childCount <= 0) return;
        _template = _scrollRect.content.GetChild(0).gameObject;
      }

      if (!Utilities.IsValid(_template)) return;

      _template.SetActive(false);
      RectTransform templateRect = _template.GetComponent<RectTransform>();
      _lineHeight = templateRect.rect.height;

      if (_lineHeight <= 0) return;

      _rowAnchorMin = templateRect.anchorMin;
      _rowAnchorMax = templateRect.anchorMax;
      _rowPivot = templateRect.pivot;
      _rowAnchoredPosition = templateRect.anchoredPosition;
      _rowSizeDelta = templateRect.sizeDelta;

      // The template itself doubles as the first pooled row.
      _lineCount = 1;
      ApplyRowGeometry(templateRect, 0);
      _lastIndexes = new int[1].Populate(-1);
      _indexes = new int[1].Populate(-1);
      EnsureCellCount();
      _initialized = true;
    }

    /// <summary>Writes the authored row geometry, plus the row's own vertical slot.</summary>
    private void ApplyRowGeometry(RectTransform rect, int index)
    {
      rect.anchorMin = _rowAnchorMin;
      rect.anchorMax = _rowAnchorMax;
      rect.pivot = _rowPivot;
      rect.sizeDelta = _rowSizeDelta;
      rect.anchoredPosition = new Vector2(_rowAnchoredPosition.x, _rowAnchoredPosition.y - index * _lineHeight);
    }

    /// <summary>
    /// Grows the row pool until it covers the whole viewport, including the row that is only
    /// partially visible at the bottom edge.
    /// </summary>
    private void EnsureCellCount()
    {
      float viewportHeight = GetViewportHeight();
      if (viewportHeight <= 0f) return;

      int needed = Mathf.CeilToInt(viewportHeight / _lineHeight) + 1;
      if (needed <= _lineCount) return;

      for (int i = _lineCount; i < needed; i++)
      {
        GameObject obj = Instantiate(_template);
        obj.transform.SetParent(_scrollRect.content, false);
        obj.transform.SetSiblingIndex(_template.transform.GetSiblingIndex() + i);
        ApplyRowGeometry(obj.GetComponent<RectTransform>(), i);
        obj.SetActive(false);
      }

      _lineCount = needed;
      _lastIndexes = new int[_lineCount].Populate(-1);
      _indexes = new int[_lineCount].Populate(-1);
    }

    private float GetViewportHeight()
    {
      if (!Utilities.IsValid(_scrollRect)) return 0f;
      RectTransform viewport = Utilities.IsValid(_scrollRect.viewport)
        ? _scrollRect.viewport
        : _scrollRect.GetComponent<RectTransform>();
      if (!Utilities.IsValid(viewport)) return 0f;
      return viewport.rect.height;
    }

    private void AdjustHeight()
    {
      if (!_initialized) Initialize();
      Vector2 size = _scrollRect.content.sizeDelta;
      size.y = _length * _lineHeight;

      for (int i = _lineCount; i < _scrollRect.content.childCount; i++)
      {
        RectTransform child = _scrollRect.content.GetChild(i).GetComponent<RectTransform>();
        child.anchoredPosition = new Vector2(child.anchoredPosition.x, -size.y);
        if (child.gameObject.activeSelf) size.y += child.rect.height;
      }
      _scrollRect.content.sizeDelta = size;
    }

    private void UpdateIndexes()
    {
      int offset = Mathf.FloorToInt(_scrollRect.content.anchoredPosition.y / _lineHeight);
      int[] indexes = new int[_lineCount];
      for (int i = 0; i < _lineCount; i++)
      {
        int target = offset / _lineCount * _lineCount + i;
        indexes[i] = target < offset ? target + _lineCount : target;
        if (indexes[i] < 0 || indexes[i] > _length - 1) indexes[i] = -1;
      }
      _lastIndexes = _indexes;
      _indexes = indexes;
    }

    private void UpdatePosition()
    {
      for (int i = 0; i < _lineCount; i++)
      {
        Transform child = _scrollRect.content.GetChild(i);
        if (!Utilities.IsValid(child)) continue;

        int index = _indexes[i];
        bool visible = index != -1;
        if (child.gameObject.activeSelf != visible) child.gameObject.SetActive(visible);

        // Always stamped, never carried over: a pooled row that was cloned while its source sat
        // somewhere else used to keep that offset and showed up drawn to the side.
        RectTransform rect = child.GetComponent<RectTransform>();
        if (Utilities.IsValid(rect)) ApplyRowGeometry(rect, index < 0 ? 0 : index);
      }
    }

    public void OnScroll()
    {
      if (!_initialized) Initialize();
      if ((_position - _scrollRect.content.anchoredPosition).sqrMagnitude <= SCROLL_THRESHOLD_SQR) return;
      _position = _scrollRect.content.anchoredPosition;
      UpdateIndexes();
      UpdatePosition();
      InvokeCallback();
    }

    private void InvokeCallback()
    {
      if (Utilities.IsValid(_callbackUdon) && !string.IsNullOrEmpty(_callbackEventName))
      {
        _callbackUdon.SendCustomEvent(_callbackEventName);
      }
    }
  }
}
