using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MusicPage.Core;

public static class DropboxApi
{
    public sealed record DbxFile(
        string Id,
        string Name,
        string PathLower,
        long Size,
        DateTime ClientModified,
        string LinkUrl
    );

    public static async Task<string> GetAccessTokenAsync(HttpClient http, string appKey, string appSecret, string refreshToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropbox.com/oauth2/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{appKey}:{appSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dropbox token failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public static async IAsyncEnumerable<DbxFile> ListMusicWithLinksAsync(
        HttpClient http, string accessToken, string folderPath, Action<string>? log = null)
    {
        static bool IsMusic(string name)
        {
            var n = name.ToLowerInvariant();
            return n.EndsWith(".mp3") || n.EndsWith(".flac") || n.EndsWith(".wav") || n.EndsWith(".aiff") || n.EndsWith(".m4a") || n.EndsWith(".ogg");
        }

        async Task<(string? cursor, List<JsonElement> items)> ListPageAsync(string? cursor)
        {
            HttpRequestMessage req;
            if (cursor is null)
            {
                req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/list_folder")
                {
                    Content = JsonContent.Create(new { path = folderPath, recursive = true, include_non_downloadable_files = false })
                };
            }
            else
            {
                req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/list_folder/continue")
                {
                    Content = JsonContent.Create(new { cursor })
                };
            }
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var res = await http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Dropbox list_folder failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{json}");

            using var doc = JsonDocument.Parse(json);
            var hasMore = doc.RootElement.GetProperty("has_more").GetBoolean();
            var nextCursor = doc.RootElement.TryGetProperty("cursor", out var c) ? c.GetString() : null;

            // 🔧 Clone each entry so it’s safe after 'doc' is disposed
            var items = new List<JsonElement>();
            foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
                items.Add(e.Clone());

            return (hasMore ? nextCursor : null, items);
        }


        async Task<string?> GetExistingSharedLinkAsync(string pathLower)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/sharing/list_shared_links")
            {
                Content = JsonContent.Create(new { path = pathLower, direct_only = true })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var res = await http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(json);
            foreach (var l in doc.RootElement.GetProperty("links").EnumerateArray())
                return l.GetProperty("url").GetString();
            return null;
        }

        async Task<string?> CreateSharedLinkAsync(string pathLower)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/sharing/create_shared_link_with_settings")
            {
                Content = JsonContent.Create(new { path = pathLower, settings = new { requested_visibility = "public" } })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var res = await http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("url").GetString();
        }

        static string ToPreview(string sharedUrl)
        {
            var u = sharedUrl.Replace("?dl=0", "").Replace("?dl=1", "");
            return u + "?dl=0";
        }

        string? cursor = null;
        do
        {
            var (next, items) = await ListPageAsync(cursor);
            cursor = next;

            foreach (var it in items)
            {
                var tag = it.GetProperty(".tag").GetString();
                if (tag != "file") continue;

                var name = it.GetProperty("name").GetString() ?? "";
                if (!IsMusic(name)) continue;

                var id = it.GetProperty("id").GetString() ?? "";
                var pathLower = it.GetProperty("path_lower").GetString() ?? "";
                var size = it.GetProperty("size").GetInt64();
                var clientModified = it.GetProperty("client_modified").GetDateTime();

                var link = await GetExistingSharedLinkAsync(pathLower) ?? await CreateSharedLinkAsync(pathLower);
                if (string.IsNullOrWhiteSpace(link)) { log?.Invoke($"No link for {name}"); continue; }

                yield return new DbxFile(
                    Id: id,
                    Name: name,
                    PathLower: pathLower,
                    Size: size,
                    ClientModified: clientModified,
                    LinkUrl: ToPreview(link)
                );
            }
        } while (cursor is not null);
    }
}
