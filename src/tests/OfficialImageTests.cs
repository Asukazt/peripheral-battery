using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PeriDock;

internal static class OfficialImageTests
{
    internal static readonly string[] Models = ["Logitech G733", "HyperX Cloud III Wireless", "MCHOSE A7 V2 Ultra",
        "ATK X1 Ultimate", "AULA F75", "VXE R1 Pro Max", "VGN F1 Moba"];

    internal static void Run(Action<string, Action> check)
    {
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        check("Cloud III black and black-red labels match the verified HP SKU photos", () =>
        {
            var product = OfficialProductImages.KnownProducts.Single(p => p.Models.Contains("HyperX Cloud III Wireless"));
            Require(product.Colors.Single(c => c.Label == "黑色").Image.AbsolutePath.Contains("dkam3j7ovn"), "Black/black-red photos were swapped");
        });
        check("every new official image alias is unique and retains its documented source", () =>
        {
            var keys = new HashSet<string>();
            foreach (var product in OfficialProductImages.KnownProducts)
            {
                Require(product.Page.Scheme == "https" && product.Page.IsDefaultPort && product.Page.UserInfo.Length == 0, "Invalid source page");
                Require(product.Colors.Length is >= 1 and <= 8, "Unbounded official variants");
                foreach (var model in product.Models)
                {
                    Require(keys.Add(OfficialProductImages.ModelKey(model)), "Duplicate official alias: " + model);
                    Require(OfficialProductImages.PageFor(model) == product.Page, "Wrong source page: " + model);
                }
                Require(product.Colors.All(c => c.Label.Length is > 0 and <= 60 && c.Image.Scheme == "https" && c.Image.IsDefaultPort && c.Image.UserInfo.Length == 0), "Invalid official image variant");
            }
        });
        check("six new brands map exact models to official product pages", () =>
        {
            foreach (var name in Models)
            {
                var page = OfficialProductImages.PageFor(name);
                Require(page?.Scheme == "https", "Missing official model: " + name);
                Require(page == OfficialProductImages.PageFor(name + " (BT)"), "Transport suffix changed model: " + name);
                Require(OfficialProductImages.PageFor(name + " unknown edition") is null, "Fuzzy model match: " + name);
            }
            foreach (var name in new[] { "Logitech HID++ 接收器", "MCHOSE M7 / L7 接收器", "ATK / VXE NK 接收器", "VXE R1 Pro / R1 Pro Max", "VGN Dragonfly 4K" })
                Require(OfficialProductImages.PageFor(name) is null, "Ambiguous receiver got a specific product image: " + name);
        });
        check("six-brand official colors download only pinned assets and restore offline", () =>
        {
            var fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png"));
            foreach (var name in Models)
            {
                var root = Path.Combine(Path.GetTempPath(), "PeriDock-new-images-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var handler = new ImageHttpHandler(request =>
                    {
                        Require(request.RequestUri?.Scheme == "https", "Non-HTTPS asset");
                        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture) };
                        response.Content.Headers.ContentType = new("image/png");
                        return response;
                    });
                    using var client = new HttpClient(handler);
                    var images = new OfficialProductImages(root, new Diagnostics(root), client);
                    var options = images.GetOptionsAsync(name).GetAwaiter().GetResult();
                    var colors = options.Where(v => v.Id is not ("auto" or "generic")).ToArray();
                    Require(colors.Length >= (name.StartsWith("Logitech") || name.StartsWith("HyperX") ? 2 : 1), "Missing official appearance: " + name);
                    Require(colors.All(c => File.Exists(c.FilePath)), "Missing downloaded file: " + name);
                    var automatic = options.Single(o => o.Id == "auto").FilePath;
                    Require(automatic is not null && File.Exists(automatic + ".source.json"), "Missing source provenance: " + name);
                    Require(handler.Seen.All(u => u != OfficialProductImages.PageFor(name)), "Unexpected storefront fetch");
                    using var offlineHandler = new ImageHttpHandler(_ => throw new HttpRequestException("offline"));
                    using var offline = new HttpClient(offlineHandler);
                    var restored = new OfficialProductImages(root, new Diagnostics(root), offline).GetOptionsAsync(name).GetAwaiter().GetResult();
                    Require(colors.All(c => restored.Any(o => o.Id == c.Id && o.FilePath == c.FilePath)), "Lost offline color: " + name);
                    Require(offlineHandler.Seen.Count == 0, "Cached colors requested network: " + name);
                    var metadata = Path.Combine(root, "data", "device-images", OfficialProductImages.FileKey(name) + ".options.json");
                    var entries = JsonSerializer.Deserialize<OfficialImageVariant[]>(File.ReadAllText(metadata))!;
                    var malicious = entries[0] with { Image = new Uri("https://evil.example/track.png"), Label = "foreign" };
                    File.WriteAllText(metadata, JsonSerializer.Serialize(new[] { malicious }));
                    using var blockedHandler = new ImageHttpHandler(_ => throw new Exception("Untrusted metadata fetched"));
                    using var blocked = new HttpClient(blockedHandler);
                    var safe = new OfficialProductImages(root, new Diagnostics(root), blocked).GetOptionsAsync(name).GetAwaiter().GetResult();
                    Require(safe.All(o => o.Label != "foreign"), "Untrusted cached variant accepted");
                    Require(blockedHandler.Seen.Count == 0, "Untrusted cached variant requested network");
                }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            }
        });
        check("new-brand image redirects cannot leave the exact verified asset list", () =>
        {
            foreach (var name in Models)
            {
                var root = Path.Combine(Path.GetTempPath(), "PeriDock-image-redirect-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var handler = new ImageHttpHandler(request =>
                    {
                        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                        response.Headers.Location = new Uri(request.RequestUri!, "/unverified.png");
                        return response;
                    });
                    using var client = new HttpClient(handler);
                    Require(new OfficialProductImages(root, new Diagnostics(root), client).GetAsync(name).GetAwaiter().GetResult() is null,
                        "Unverified redirect accepted: " + name);
                    Require(handler.Seen.Count == 1, "Unverified redirect requested: " + name);
                }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            }
        });
    }
}
