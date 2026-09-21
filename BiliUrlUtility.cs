using System.Text;

namespace Yamadev.YamaStream.Modules.BilibiliSearch
{
  /// <summary>
  /// Reads the request url the player typed into the search box.
  /// </summary>
  /// <remarks>
  /// Nothing here knows the backend. Every world points the module at its own bilibili player
  /// server (see BACKEND.md), so the url is read as a plain query string instead of being matched
  /// against a fixed host. A valid request url carries exactly two parameters, a page and a non
  /// empty keyword:
  /// <code>https://bili.example.com/player/?page=1&amp;keyword=%E9%9F%B3%E6%A5%BD</code>
  /// </remarks>
  public static class BiliUrlUtility
  {
    private const string PageParameter = "page=";
    private const string KeywordParameter = "keyword=";

    /// <summary>A bilibili video id: "BV" followed by ten more characters.</summary>
    public static bool IsBv(string value)
    {
      if (string.IsNullOrEmpty(value) || value.Length != 12 || !value.StartsWith("BV")) return false;
      for (int i = 2; i < value.Length; i++)
      {
        char c = value[i];
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
      }
      return true;
    }

    /// <summary>
    /// Bilibili video id inside a url, empty when there is none. Used to give a track played from
    /// the panel's url box a readable name.
    /// </summary>
    public static string VideoId(string url)
    {
      if (string.IsNullOrEmpty(url)) return string.Empty;

      for (int i = 0; i + 12 <= url.Length; i++)
      {
        if (url[i] != 'B' || url[i + 1] != 'V') continue;
        string candidate = url.Substring(i, 12);
        if (IsBv(candidate)) return candidate;
      }
      return string.Empty;
    }

    /// <summary>Part of the url behind the '?', empty when it has none.</summary>
    private static string Query(string url)
    {
      if (string.IsNullOrEmpty(url)) return string.Empty;
      int separator = url.IndexOf('?');
      if (separator < 0 || separator + 1 >= url.Length) return string.Empty;
      return url.Substring(separator + 1);
    }

    /// <summary>Page of a request url, 0 when it is not a valid request url.</summary>
    public static int Page(string url)
    {
      string query = Query(url);
      if (query.Length == 0 || query.IndexOf('#') >= 0) return 0;

      string[] parts = query.Split('&');
      if (parts.Length != 2) return 0;

      int page = 0;
      bool keyword = false;
      for (int i = 0; i < parts.Length; i++)
      {
        if (parts[i].StartsWith(PageParameter))
        {
          // One to four digits, so the page is always inside the range checked below.
          if (parts[i].Length < PageParameter.Length + 1 || parts[i].Length > PageParameter.Length + 4) return 0;
          for (int n = PageParameter.Length; n < parts[i].Length; n++)
          {
            char c = parts[i][n];
            if (c < '0' || c > '9') return 0;
            page = page * 10 + c - '0';
          }
        }
        else if (parts[i].StartsWith(KeywordParameter)) keyword = !string.IsNullOrWhiteSpace(parts[i].Substring(KeywordParameter.Length));
        else return 0;
      }

      return keyword && page > 0 && page <= 9999 ? page : 0;
    }

    /// <summary>Same request url with another page, empty when the url is not valid.</summary>
    public static string ChangePage(string url, int page)
    {
      if (Page(url) == 0 || page < 1 || page > 9999) return string.Empty;

      int separator = url.IndexOf('?');
      string[] parts = url.Substring(separator + 1).Split('&');
      if (parts[0].StartsWith(PageParameter)) parts[0] = PageParameter + page;
      else parts[1] = PageParameter + page;
      return url.Substring(0, separator + 1) + parts[0] + "&" + parts[1];
    }

    /// <summary>Keyword of a request url, url decoded. Empty when the url is not valid.</summary>
    public static string Keyword(string url)
    {
      if (Page(url) == 0) return string.Empty;

      string[] parts = Query(url).Split('&');
      string raw = parts[0].StartsWith(KeywordParameter) ? parts[0].Substring(KeywordParameter.Length) : parts[1].Substring(KeywordParameter.Length);
      byte[] bytes = new byte[raw.Length];
      int count = 0;
      string decoded = "";
      for (int i = 0; i < raw.Length; i++)
      {
        if (raw[i] == '%' && i + 2 < raw.Length)
        {
          int a = Hex(raw[i + 1]), b = Hex(raw[i + 2]);
          if (a >= 0 && b >= 0) { bytes[count++] = (byte)(a * 16 + b); i += 2; continue; }
        }
        if (count > 0) { decoded += Encoding.UTF8.GetString(bytes, 0, count); count = 0; }
        decoded += raw[i] == '+' ? " " : raw[i].ToString();
      }
      if (count > 0) decoded += Encoding.UTF8.GetString(bytes, 0, count);
      return decoded;
    }

    private static int Hex(char c)
    {
      if (c >= '0' && c <= '9') return c - '0';
      if (c >= 'a' && c <= 'f') return c - 'a' + 10;
      if (c >= 'A' && c <= 'F') return c - 'A' + 10;
      return -1;
    }
  }
}
