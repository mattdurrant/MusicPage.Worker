namespace MusicPage.Worker;

using MusicPage.Core;
using System.Text;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };

            var appKey = Env("DROPBOX_APP_KEY");
            var appSecret = Env("DROPBOX_APP_SECRET");
            var refreshTok = Env("DROPBOX_REFRESH_TOKEN");
            var folderPath = Environment.GetEnvironmentVariable("DROPBOX_FOLDER") ?? "/Public/Music";
            var outputDir = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";

            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"Music: listing {folderPath} from Dropbox…");
            var accessToken = await DropboxApi.GetAccessTokenAsync(http, appKey, appSecret, refreshTok);

            var files = new List<DropboxApi.DbxFile>();
            await foreach (var f in DropboxApi.ListMusicWithLinksAsync(http, accessToken, folderPath, s => Console.WriteLine("   " + s)))
                files.Add(f);

            var rows = files
                .OrderByDescending(f => f.ClientModified)
                .Select(f => new MusicRenderer.Row(
                    Name: f.Name,
                    Url: f.LinkUrl,
                    SizeBytes: f.Size,
                    ModifiedUtc: DateTime.SpecifyKind(f.ClientModified, DateTimeKind.Utc)))
                .ToList();

            var html = MusicRenderer.Render(rows, "Music", "A rolling list of tracks I’ve made, hosted on Dropbox.");

            var outDir = Path.Combine(outputDir);
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "index.html");
            await File.WriteAllTextAsync(outPath, html, Encoding.UTF8);

            Console.WriteLine($"Music: wrote {outPath} ({rows.Count} items).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex.Message);
            return 1;
        }
    }

    private static string Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"Missing environment variable: {name}");
        return v;
    }
}
