using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// Attached to a button inside a result cell. Tells the panel which result the button belongs to,
  /// then forwards the click to the matching action.
  /// </summary>
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliSearchResultAction : UdonSharpBehaviour
  {
    [SerializeField] private BilibiliSearchUI _uiBehaviour;
    [SerializeField] private Button _button;
    [Tooltip("Only the queue button has to keep its interactable state in sync with the panel.")]
    [SerializeField] private bool _isQueueAction;
    private int _index = -1;

    public void SetUp(BilibiliSearchUI uiBehaviour, int index)
    {
      _uiBehaviour = uiBehaviour;
      _index = index;
      RefreshInteractable();
    }

    /// <summary>
    /// The queue button is disabled for a few seconds after it was used, so the same video
    /// cannot be queued twice by accident. Cells are pooled by LoopScroll, so the state has
    /// to be refreshed instead of being set once.
    /// </summary>
    private void Update()
    {
      if (!_isQueueAction) return;
      RefreshInteractable();
    }

    private void RefreshInteractable()
    {
      if (!_isQueueAction || !Utilities.IsValid(_button) || !Utilities.IsValid(_uiBehaviour)) return;
      bool ready = _uiBehaviour.CanQueue(_index);
      if (_button.interactable != ready) _button.interactable = ready;
    }

    private void Trigger(string eventName)
    {
      if (!Utilities.IsValid(_uiBehaviour)) return;
      _uiBehaviour.ActionIndex = _index;
      _uiBehaviour.SendCustomEvent(eventName);
    }

    public void CopyLink() => Trigger(nameof(BilibiliSearchUI.CopyLink));

    public void Play() => Trigger(nameof(BilibiliSearchUI.Play));

    public void AddToQueue() => Trigger(nameof(BilibiliSearchUI.AddToQueue));
  }
}
