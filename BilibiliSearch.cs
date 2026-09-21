using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// YamaPlayer module that adds a bilibili video search panel to the player UI.
  /// </summary>
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliSearch : YamaPlayerModule
  {
    [SerializeField, HideInInspector] private BilibiliSearchService _service;
    [SerializeField, HideInInspector] private BilibiliSearchUI _uiBehaviour;
    [SerializeField, HideInInspector] private BilibiliSearchResult _result;

    public BilibiliSearchService Service => _service;
    public BilibiliSearchUI UIBehaviour => _uiBehaviour;
    public BilibiliSearchResult Result => _result;

    /// <summary>Exposed so the panel can fall back to it when no module reference is wired.</summary>
    [SerializeField] private Controller _targetController;
    public Controller Controller => Utilities.IsValid(_controller) ? _controller : _targetController;
    public void BindController(Controller target) { _targetController = target; _controller = target; }
    public void SetActiveUI(BilibiliSearchUI ui)
    {
      if (Utilities.IsValid(_service) && _service.IsLoading) return;
      _uiBehaviour = ui;
      if (Utilities.IsValid(_service)) _service.SetUIBehaviour(ui);
    }

    public override void Start()
    {
      // Modules are normally injected with the controller by YamaPlayerModuleBuildProcess at
      // build time. Resolve it here as well so the module also works when it was not injected
      // (for example while testing without a build).
      if (!Utilities.IsValid(_controller)) _controller = _targetController;
      if (!Utilities.IsValid(_controller)) _controller = GetComponentInParent<Controller>();

      base.Start();

      if (!Utilities.IsValid(_service)) _service = GetComponentInChildren<BilibiliSearchService>();
      if (!Utilities.IsValid(_result)) _result = GetComponentInChildren<BilibiliSearchResult>();
      if (!Utilities.IsValid(_uiBehaviour)) _uiBehaviour = GetComponentInChildren<BilibiliSearchUI>(true);

      if (Utilities.IsValid(_service))
      {
        _service.SetResult(_result);
        _service.SetUIBehaviour(_uiBehaviour);
      }
      if (Utilities.IsValid(_uiBehaviour)) _uiBehaviour.SetSearchTarget(this, _result);

      if (!Utilities.IsValid(_service) || !Utilities.IsValid(_result))
      {
        PrintError("BilibiliSearch prefab is incomplete. Re-run Tools/YamaPlayer/Bilibili Search Setup.");
      }
    }

    /// <summary>
    /// Entry point used by the panel. The panel only talks to the module, never to the service.
    /// </summary>
    public void Search(string keyword, int page)
    {
      if (!Utilities.IsValid(_service))
      {
        PrintError("BilibiliSearchService is missing on the BilibiliSearch module.");
        return;
      }
      _service.Search(keyword, page);
    }

    /// <summary>
    /// Plays an entry of a result page. The controller lives on the module, the panel
    /// (a YamaPlayerListener) has no access to it, so the action is routed through here.
    /// The URL is selected from the editor-baked srid pool and validated against the
    /// current result and the sender's displayed row before changing playback.
    /// </summary>
    /// <returns>0 when nothing happened, 1 when playback started, 2 when the track was
    /// queued because a video was already playing.</returns>
    public int PlayConfirmed(BilibiliSearchUI sender, int index, VRCUrl url)
    {
      if (!Utilities.IsValid(sender) || !Utilities.IsValid(_result) || !Utilities.IsValid(_service)) return 0;
      if (!Utilities.IsValid(Controller) || index < 0 || index >= _result.Count) return 0;
      if (!BiliUrlUtility.IsBv(_result.Ids[index]) || VRCUrl.IsNullOrEmpty(url)) return 0;
      if (!_service.MatchesResultUrl(index, url) || !sender.MatchesDisplayedResult(index, _result.Ids[index], _result.RecordIds[index])) return 0;
      if (!sender.CheckPlayPermission()) return 0;
      if (Controller.FindHandlerIndexForUrl(url) < 0) return 0;

      if (Controller.IsPlaying && !sender.CheckQueuePermission()) return 0;
      object[] track = TrackUtils.NewTrack(VideoPlayerType.AVProVideoPlayer, _result.Titles[index], url);
      Controller.TakeOwnership();

      // Never cut off a running video: while something plays, the track is queued instead.
      if (Controller.IsPlaying)
      {
        Controller.Queue.AddTrack(track);
        return 2;
      }

      Controller.PlayTrack(track);
      return 1;
    }

    /// <summary>
    /// Plays a url the player authored directly on the url page of the panel, or queues it while
    /// something else is playing.
    /// </summary>
    /// <remarks>
    /// This is the same as <see cref="PlayConfirmed"/> except that the url is not validated against
    /// a search result: the player completed it in the panel's own url box, so it is theirs to
    /// choose. The track title has no name to use, so the video id stands in for it.
    /// </remarks>
    /// <returns>0 when nothing happened, 1 when playback started, 2 when the track was queued.</returns>
    public int PlayTrack(BilibiliSearchUI sender, VRCUrl url, string title)
    {
      if (!Utilities.IsValid(sender) || VRCUrl.IsNullOrEmpty(url)) return 0;
      if (!Utilities.IsValid(Controller)) return 0;
      if (!sender.CheckPlayPermission()) return 0;
      if (Controller.FindHandlerIndexForUrl(url) < 0) return 0;

      if (Controller.IsPlaying && !sender.CheckQueuePermission()) return 0;
      object[] track = TrackUtils.NewTrack(VideoPlayerType.AVProVideoPlayer, title, url);
      Controller.TakeOwnership();

      // Never cut off a running video: while something plays, the track is queued instead.
      if (Controller.IsPlaying)
      {
        Controller.Queue.AddTrack(track);
        return 2;
      }

      Controller.PlayTrack(track);
      return 1;
    }

    /// <summary>Adds an entry of a result page to YamaPlayer's play queue.</summary>
    public bool QueueConfirmed(BilibiliSearchUI sender, int index, VRCUrl url)
    {
      if (!Utilities.IsValid(sender) || !Utilities.IsValid(_result) || !Utilities.IsValid(_service)) return false;
      if (!Utilities.IsValid(Controller) || index < 0 || index >= _result.Count) return false;
      if (!BiliUrlUtility.IsBv(_result.Ids[index]) || VRCUrl.IsNullOrEmpty(url)) return false;
      if (!_service.MatchesResultUrl(index, url) || !sender.MatchesDisplayedResult(index, _result.Ids[index], _result.RecordIds[index])) return false;
      if (!sender.CheckQueuePermission()) return false;
      object[] track = TrackUtils.NewTrack(VideoPlayerType.AVProVideoPlayer, _result.Titles[index], url);
      Controller.TakeOwnership();
      Controller.Queue.AddTrack(track);
      return true;
    }
  }
}
