using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace PeriDock;

internal sealed record ProductImageOption(string Id, string Label, string? FilePath);
internal sealed record OfficialImageVariant(string Id, string Label, Uri Page, Uri Image);

internal sealed class OfficialProductImages(string root, Diagnostics log, HttpClient? transport = null)
{
    static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false });
    static readonly Dictionary<string, string> Pages = LoadPages();
    readonly Dictionary<string, (DateTimeOffset Until, Task<string?> Work)> requests = [];
    readonly Dictionary<string, (DateTimeOffset Until, Task<IReadOnlyList<ProductImageOption>> Work)> optionRequests = [];
    readonly SemaphoreSlim slots = new(2);
    static Dictionary<string, string> LoadPages()
    {
        using var stream = typeof(OfficialProductImages).Assembly.GetManifestResourceStream("PeriDock.official-products.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!
            .ToDictionary(p => DeviceCatalog.ModelKey(p.Key), p => p.Value);
    }
    internal static string ModelKey(string name)
        => DeviceCatalog.ModelKey(Regex.Replace(name, @"\s*\((?:BT|Bluetooth|USB|2\.4G)\)\s*$", "", RegexOptions.IgnoreCase));
    internal static Uri? PageFor(string name)
        => Pages.TryGetValue(ModelKey(name), out var page) ? new Uri(page) : null;
    internal static string FileKey(string name) => DeviceCatalog.Key(ModelKey(name));
    internal static string OverridePath(string root, string name)
        => Path.Combine(root, "data", "device-images", "overrides", FileKey(name) + ".png");
    static bool Allowed(Uri uri, Uri page, bool image)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        return page.Host switch
        {
            "www.razer.com" => image ? uri.Host is "assets2.razerzone.com" or "assets3.razerzone.com" or "assets.razerzone.com" or "assets.razer.com" : uri.Host == page.Host,
            "rog.asus.com" => image ? uri.Host is "dlcdnwebimgs.asus.com" or "dlcdnimgs.asus.com" : uri.Host == page.Host,
            _ => false
        };
    }
    static string PageModel(Uri page)
    {
        var parts = page.AbsolutePath.TrimEnd('/').Split('/');
        var slug = parts[^1];
        if (slug is "buy" or "gallery" || slug.StartsWith("RZ", StringComparison.OrdinalIgnoreCase)) slug = parts[^2];
        return DeviceCatalog.ModelKey(slug.EndsWith("-model") ? slug[..^6] : slug);
    }
    static Dictionary<string, string> Attributes(string tag)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in Regex.Matches(tag, "([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')"))
            values[a.Groups[1].Value] = WebUtility.HtmlDecode(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value);
        return values;
    }
    internal static Uri? ImageFromPage(Uri page, string html)
    {
        if (!Allowed(page, page, false)) return null;
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var attributes = Attributes(tag.Value);
            if (attributes.TryGetValue("property", out var property) && attributes.TryGetValue("content", out var content))
                meta.TryAdd(property, content);
        }
        if (!meta.TryGetValue("og:title", out var title) ||
            !DeviceCatalog.ModelKey(title).Contains(PageModel(page), StringComparison.Ordinal)) return null;
        return meta.TryGetValue("og:image", out var value) && Uri.TryCreate(page, value, out var uri) && Allowed(uri, page, true) ? uri : null;
    }
    static string ColorLabel(string value) => value.Trim().ToLowerInvariant() switch
    {
        "black" => "黑色", "white" => "白色", "moonlight white" => "月光白", "quartz" => "粉晶",
        "mercury" => "水银白", "green" => "绿色", "phantom green" => "幻影绿", _ => value[..Math.Min(value.Length, 60)]
    };
    internal static IReadOnlyList<OfficialImageVariant> VariantsFromPage(Uri page, string html)
    {
        var variants = new List<OfficialImageVariant>();
        if (!Allowed(page, page, false)) return variants;
        void Add(string label, string url)
        {
            if (label.Length > 0 && Uri.TryCreate(page, url, out var uri) && Allowed(uri, page, true) &&
                !uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && variants.All(v => v.Label != label))
                variants.Add(new(DeviceCatalog.Key(uri.AbsoluteUri), label, page, uri));
        }
        if (page.Host == "www.razer.com")
        {
            var script = Regex.Match(html, "<script[^>]+id=[\"']ng-state[\"'][^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!script.Success) return variants;
            using var json = JsonDocument.Parse(script.Groups[1].Value);
            void Visit(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    if (node.TryGetProperty("baseOptions", out var groups) && groups.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var group in groups.EnumerateArray())
                        {
                            if (!group.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) continue;
                            foreach (var option in options.EnumerateArray())
                            {
                                if (!option.TryGetProperty("name", out var name) ||
                                    DeviceCatalog.ModelKey(name.GetString()!.Split(" - ")[0]) != PageModel(page) ||
                                    !option.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array ||
                                    !option.TryGetProperty("variantOptionQualifiers", out var qualifiers) || qualifiers.ValueKind != JsonValueKind.Array) continue;
                                var color = qualifiers.EnumerateArray().FirstOrDefault(q => q.TryGetProperty("qualifier", out var key) && key.GetString() == "color");
                                var image = images.EnumerateArray().FirstOrDefault(i => i.TryGetProperty("format", out var format) && format.GetString() == "product");
                                if (color.ValueKind == JsonValueKind.Object && image.ValueKind == JsonValueKind.Object &&
                                    color.TryGetProperty("value", out var label) && image.TryGetProperty("url", out var url))
                                    Add(ColorLabel(label.GetString() ?? ""), url.GetString() ?? "");
                            }
                        }
                    }
                    else foreach (var property in node.EnumerateObject()) Visit(property.Value);
                }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var value in node.EnumerateArray()) Visit(value);
            }
            Visit(json.RootElement);
        }
        else
        {
            foreach (Match tag in Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var attributes = Attributes(tag.Value);
                if (!attributes.TryGetValue("alt", out var alt) || !DeviceCatalog.ModelKey(alt).Contains(PageModel(page), StringComparison.Ordinal) ||
                    !attributes.TryGetValue("src", out var src)) continue;
                var lower = alt.ToLowerInvariant();
                if (lower.Contains("black") && lower.Contains("white")) continue;
                var color = new[] { "moonlight white", "white", "black", "pink", "red" }.FirstOrDefault(lower.Contains);
                Add(color is null ? "官方外观" : ColorLabel(color), src);
            }
        }
        return variants.Take(8).ToArray();
    }
    // Calls originate on the UI dispatcher; shared work deduplicates USB/receiver/Bluetooth images for one model.
    internal Task<string?> GetAsync(string name, CancellationToken cancellation = default)
    {
        var key = FileKey(name);
        if (requests.TryGetValue(key, out var request) && request.Until > DateTimeOffset.UtcNow) return request.Work;
        var task = LoadAsync(name, cancellation);
        requests[key] = (DateTimeOffset.UtcNow.AddHours(24), task);
        return task;
    }
    async Task<string?> LoadAsync(string name, CancellationToken cancellation)
    {
        var folder = Path.Combine(root, "data", "device-images");
        var file = Path.Combine(folder, FileKey(name) + ".png");
        var retry = Path.Combine(folder, FileKey(name) + ".retry");
        await slots.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            foreach (var candidate in new[] { OverridePath(root, name), file })
                if (File.Exists(candidate) && await Task.Run(() => ValidImage(candidate), cancellation).ConfigureAwait(false)) return candidate;
            var page = PageFor(name);
            if (page is null || File.Exists(retry) && DateTime.UtcNow - File.GetLastWriteTimeUtc(retry) < TimeSpan.FromHours(24)) return null;
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(retry, "", cancellation).ConfigureAwait(false);
            string html;
            var optionsPage = new Uri(page.AbsoluteUri.TrimEnd('/') + (page.Host == "www.razer.com" ? "/buy" : "/gallery/"));
            try { html = Encoding.UTF8.GetString(await FetchAsync(optionsPage, page, false, cancellation).ConfigureAwait(false)); }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            { html = Encoding.UTF8.GetString(await FetchAsync(page, page, false, cancellation).ConfigureAwait(false)); }
            var variants = VariantsFromPage(page, html);
            if (variants.Count > 0) await SaveVariants(name, variants, cancellation).ConfigureAwait(false);
            var image = variants.FirstOrDefault()?.Image ?? ImageFromPage(page, html) ?? throw new InvalidDataException("官网页面未提供匹配型号的图片");
            var bytes = await FetchAsync(image, page, true, cancellation).ConfigureAwait(false);
            var png = await Task.Run(() => NormalizeImage(bytes), cancellation).ConfigureAwait(false);
            await File.WriteAllBytesAsync(file + ".tmp", png, cancellation).ConfigureAwait(false);
            File.Move(file + ".tmp", file, true);
            await File.WriteAllTextAsync(file + ".source.json", JsonSerializer.Serialize(new {
                page = page.AbsoluteUri, image = image.AbsoluteUri, fetchedAt = DateTimeOffset.UtcNow
            }), cancellation).ConfigureAwait(false);
            File.Delete(retry);
            log.Write("info", "images", "download", page.AbsoluteUri + "; 官网产品图已缓存");
            return file;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            log.Write("info", "images", "fallback", "官网产品图不可用，继续使用通用图标；" + ex.GetType().Name);
            return null;
        }
        finally { slots.Release(); }
    }
    string OptionsFile(string name) => Path.Combine(root, "data", "device-images", FileKey(name) + ".options.json");
    Task SaveVariants(string name, IReadOnlyList<OfficialImageVariant> variants, CancellationToken cancellation)
        => File.WriteAllTextAsync(OptionsFile(name), JsonSerializer.Serialize(variants), cancellation);
    internal Task<IReadOnlyList<ProductImageOption>> GetOptionsAsync(string name, CancellationToken cancellation = default)
    {
        var key = FileKey(name);
        if (optionRequests.TryGetValue(key, out var request) && request.Until > DateTimeOffset.UtcNow) return request.Work;
        var task = LoadOptionsAsync(name, GetAsync(name, cancellation), cancellation);
        optionRequests[key] = (DateTimeOffset.UtcNow.AddHours(24), task);
        return task;
    }
    async Task<IReadOnlyList<ProductImageOption>> LoadOptionsAsync(string name, Task<string?> automatic, CancellationToken cancellation)
    {
        var choices = new List<ProductImageOption> { new("auto", "自动", await automatic.ConfigureAwait(false)) };
        var local = OverridePath(root, name);
        if (File.Exists(local) && await Task.Run(() => ValidImage(local), cancellation).ConfigureAwait(false)) choices.Add(new("local", "本地图片", local));
        try
        {
            var page = PageFor(name);
            if (page is not null)
            {
                var metadata = OptionsFile(name);
                IReadOnlyList<OfficialImageVariant> variants = [];
                if (File.Exists(metadata) && new FileInfo(metadata).Length < 1024 * 1024)
                {
                    try { variants = JsonSerializer.Deserialize<OfficialImageVariant[]>(await File.ReadAllTextAsync(metadata, cancellation).ConfigureAwait(false)) ?? []; }
                    catch (JsonException) { }
                }
                if (variants.Count == 0 || DateTime.UtcNow - File.GetLastWriteTimeUtc(metadata) > TimeSpan.FromDays(7))
                {
                    try
                    {
                        var url = new Uri(page.AbsoluteUri.TrimEnd('/') + (page.Host == "www.razer.com" ? "/buy" : "/gallery/"));
                        var html = Encoding.UTF8.GetString(await FetchAsync(url, page, false, cancellation).ConfigureAwait(false));
                        var latest = VariantsFromPage(page, html);
                        if (latest.Count > 0 || variants.Count == 0)
                        {
                            variants = latest; Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
                            await SaveVariants(name, variants, cancellation).ConfigureAwait(false);
                        }
                    }
                    catch (Exception) when (variants.Count > 0 && !cancellation.IsCancellationRequested) { }
                }
                foreach (var variant in variants.Take(8))
                {
                    try
                    {
                        if (!Regex.IsMatch(variant.Id, "^[0-9a-f]{24}$") || !Allowed(variant.Page, page, false) ||
                            PageModel(variant.Page) != PageModel(page) || !Allowed(variant.Image, page, true)) continue;
                        var file = Path.Combine(root, "data", "device-images", FileKey(name) + "-" + variant.Id + ".png");
                        if (!File.Exists(file) || !await Task.Run(() => ValidImage(file), cancellation).ConfigureAwait(false))
                        {
                            var bytes = await FetchAsync(variant.Image, page, true, cancellation).ConfigureAwait(false);
                            var png = await Task.Run(() => NormalizeImage(bytes), cancellation).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(file + ".tmp", png, cancellation).ConfigureAwait(false);
                            File.Move(file + ".tmp", file, true);
                        }
                        choices.Add(new(variant.Id, variant.Label, file));
                    }
                    catch (Exception) when (!cancellation.IsCancellationRequested) { }
                }
            }
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        { log.Write("info", "images", "colors", "官网颜色暂不可用；" + ex.GetType().Name); }
        choices.Add(new("generic", "通用图标", null));
        return choices;
    }
    async Task<byte[]> FetchAsync(Uri uri, Uri page, bool image, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var limit = image ? 5 * 1024 * 1024 : 3 * 1024 * 1024;
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            if (!Allowed(uri, page, image) || !image && PageModel(uri) != PageModel(page)) throw new InvalidDataException("非官网或不同型号地址");
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.UserAgent.ParseAdd("PeripheralBattery/0.2 (official-product-images)");
            using var response = await (transport ?? Client).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            // Also check the final URI when a caller supplies an HttpClient with automatic redirects enabled.
            if (response.RequestMessage?.RequestUri is { } final &&
                (!Allowed(final, page, image) || !image && PageModel(final) != PageModel(page)))
                throw new InvalidDataException("非官网重定向");
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = new Uri(uri, location); continue;
            }
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType;
            if ((image ? type is not ("image/png" or "image/jpeg") : type != "text/html") || response.Content.Headers.ContentLength > limit)
                throw new InvalidDataException("图片或页面类型、大小不符合要求");
            using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > limit) throw new InvalidDataException("下载超出大小限制");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        throw new InvalidDataException("官网重定向次数过多");
    }
    internal static byte[] NormalizeImage(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 25000000)
            throw new InvalidDataException("图片尺寸超出限制");
        source.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = source;
        if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = Math.Min(frame.PixelWidth, 512);
        else bitmap.DecodePixelHeight = Math.Min(frame.PixelHeight, 512);
        bitmap.EndInit(); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    static bool ValidImage(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 5 * 1024 * 1024) return false;
            NormalizeImage(File.ReadAllBytes(file)); return true;
        }
        catch { return false; }
    }
}
