namespace MusicPage.Core;

using System.Text;

public static class MusicRenderer
{
    public sealed record Row(string Name, string Url, long SizeBytes, DateTime ModifiedUtc);

    public static string Render(IEnumerable<Row> items, string title, string? introHtml = null)
    {
        var list = items.ToList();

        var sb = new StringBuilder();
        sb.Append(@"<!doctype html><html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>").Append(Html(title)).Append(@"</title>
<link rel=""stylesheet"" href=""https://www.mattdurrant.com/styles.css"">
<link rel=""stylesheet"" type=""text/css"" href=""https://www.mattdurrant.com/albums.css"">
<style>
.music-list { padding:0; list-style:none; }
.music-list li { padding:10px 0; border-bottom:1px solid #ddd; }
.meta { color:#666; font-size:0.95em; }
</style>
</head><body class=""albums-page"">");


        sb.Append(@"<header>
            <div class=""site-nav""><a href=""https://www.mattdurrant.com/"">← Home</a></div>
            <h1>").Append(Html(title)).Append("</h1>");

        if (!string.IsNullOrWhiteSpace(introHtml))
            sb.Append(@"<div class=""blurb"">").Append(introHtml).Append("</div>");
        sb.Append("</header><main>");

        sb.Append(@"<ul class=""music-list"">");
        foreach (var r in list)
        {
            var uk = ToUk(r.ModifiedUtc).ToString("yyyy-MM-dd HH:mm 'UK'");
            sb.Append("<li>");
            sb.Append($@"<div><a href=""{r.Url}"" target=""_blank"">{Html(r.Name)}</a></div>");
            sb.Append($@"<div class=""meta"">{FormatSize(r.SizeBytes)} — updated {uk}</div>");
            sb.Append("</li>");
        }
        sb.Append("</ul>");

        var updated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
        sb.Append(@"</main><div class=""footer"">Last updated: ").Append(updated).Append("</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string FormatSize(long bytes)
    {
        double b = bytes;
        if (b >= 1024 * 1024 * 1024) return $"{b / 1024 / 1024 / 1024:0.##} GB";
        if (b >= 1024 * 1024) return $"{b / 1024 / 1024:0.##} MB";
        if (b >= 1024) return $"{b / 1024:0.##} KB";
        return $"{b:0} B";
    }
    private static DateTime ToUk(DateTime utc)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")); }
        catch { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")); }
    }
}
