using UdonSharp;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// Holds the parsed data of a single search result page.
  /// Kept as a separate UdonSharp behaviour because Udon does not support plain classes.
  /// </summary>
  [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
  public class BilibiliSearchResult : UdonSharpBehaviour
  {
    public int Count;
    public int Page = 1;
    public string Keyword = string.Empty;
    public string[] Ids = new string[0];
    public string[] Titles = new string[0];
    public string[] Channels = new string[0];
    public string[] Descriptions = new string[0];
    public string[] Covers = new string[0];
    public int[] RecordIds = new int[0];
    public int PreviousRecordId;
    public int NextRecordId;
    public string Error = string.Empty;

    public void Clear()
    {
      Count = 0;
      RecordIds = new int[0];
      PreviousRecordId = 0;
      NextRecordId = 0;
      Error = string.Empty;
      Ids = new string[0];
      Titles = new string[0];
      Channels = new string[0];
      Descriptions = new string[0];
      Covers = new string[0];
    }
  }
}
