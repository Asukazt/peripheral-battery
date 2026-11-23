#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PeriDock;

internal sealed class OfficialProductImages
{
	[CompilerGenerated]
	private string _003Croot_003EP;

	[CompilerGenerated]
	private Diagnostics _003Clog_003EP;

	[CompilerGenerated]
	private HttpClient? _003Ctransport_003EP;

	private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
	{
		AllowAutoRedirect = false
	});

	private static readonly Dictionary<string, string> Pages = LoadPages();

	internal static readonly OfficialImageProduct[] KnownProducts = LoadProducts();

	private readonly Dictionary<string, (DateTimeOffset Until, Task<string?> Work)> requests;

	private readonly Dictionary<string, (DateTimeOffset Until, Task<IReadOnlyList<ProductImageOption>> Work)> optionRequests;

	private readonly SemaphoreSlim slots;

	public OfficialProductImages(string root, Diagnostics log, HttpClient? transport = null)
	{
		_003Croot_003EP = root;
		_003Clog_003EP = log;
		_003Ctransport_003EP = transport;
		requests = new Dictionary<string, (DateTimeOffset, Task<string>)>();
		optionRequests = new Dictionary<string, (DateTimeOffset, Task<IReadOnlyList<ProductImageOption>>)>();
		slots = new SemaphoreSlim(2);
	}

	private static Dictionary<string, string> LoadPages()
	{
		using Stream utf8Json = typeof(OfficialProductImages).Assembly.GetManifestResourceStream("PeriDock.official-products.json");
		return JsonSerializer.Deserialize<Dictionary<string, string>>(utf8Json).ToDictionary((KeyValuePair<string, string> p) => DeviceCatalog.ModelKey(p.Key), (KeyValuePair<string, string> p) => p.Value);
	}

	private static OfficialImageProduct[] LoadProducts()
	{
		using Stream utf8Json = typeof(OfficialProductImages).Assembly.GetManifestResourceStream("PeriDock.official-image-sources.json");
		return JsonSerializer.Deserialize<OfficialImageProduct[]>(utf8Json);
	}

	private static IReadOnlyList<OfficialImageVariant> CatalogVariants(string name)
	{
		var product = KnownProducts.FirstOrDefault((OfficialImageProduct p) => p.Models.Any((string m) => ModelKey(m) == ModelKey(name)));
		if (product?.Colors is null) return Array.Empty<OfficialImageVariant>();
		return product.Colors.Take(8)
			.Select(c => new OfficialImageVariant(DeviceCatalog.Key(c.Image.AbsoluteUri), c.Label, PageFor(name), c.Image))
			.ToArray();
	}

	internal static string ModelKey(string name)
	{
		return DeviceCatalog.ModelKey(Regex.Replace(name, "\\s*\\((?:BT|Bluetooth|USB|2\\.4G)\\)\\s*$", "", RegexOptions.IgnoreCase));
	}

	internal static Uri? PageFor(string name)
	{
		string name2 = name;
		if (!Pages.TryGetValue(ModelKey(name2), out string value))
		{
			return KnownProducts.FirstOrDefault((OfficialImageProduct p) => p.Models.Any((string m) => ModelKey(m) == ModelKey(name2)))?.Page;
		}
		return new Uri(value);
	}

	internal static string FileKey(string name)
	{
		return DeviceCatalog.Key(ModelKey(name));
	}

	internal static string OverridePath(string root, string name)
	{
		return Path.Combine(root, "data", "device-images", "overrides", FileKey(name) + ".png");
	}

	internal static string CustomPath(string root, string name)
	{
		return Path.Combine(root, "data", "device-images", "custom", FileKey(name) + ".png");
	}

	internal async Task<ProductImageOption> ImportAsync(string name, string source, CancellationToken cancellation = default(CancellationToken))
	{
		bool flag;
		switch (Path.GetExtension(source).ToLowerInvariant())
		{
		case ".png":
		case ".jpg":
		case ".jpeg":
		case ".bmp":
		case ".ico":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidDataException("请选择 PNG、JPG、BMP 或 ICO 图片。");
		}
		byte[] bytes;
		using (FileStream input = File.OpenRead(source))
		{
			long length = input.Length;
			if ((length <= 0 || length > 10485760) ? true : false)
			{
				throw new InvalidDataException("图片大小应在 10 MB 以内。");
			}
			bytes = new byte[(int)input.Length];
			await input.ReadExactlyAsync(bytes, cancellation);
		}
		byte[] bytes2 = await Task.Run(() => NormalizeImage(bytes), cancellation);
		string file = CustomPath(_003Croot_003EP, name);
		Directory.CreateDirectory(Path.GetDirectoryName(file));
		string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			await File.WriteAllBytesAsync(temp, bytes2, cancellation);
			cancellation.ThrowIfCancellationRequested();
			File.Move(temp, file, overwrite: true);
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
		}
		optionRequests.Remove(FileKey(name));
		return new ProductImageOption("custom", "自定义图片", file);
	}

	private static bool Allowed(Uri uri, Uri page, bool image)
	{
		Uri page2 = page;
		Uri uri2 = uri;
		if (uri2.Scheme != "https" || !uri2.IsDefaultPort || uri2.UserInfo.Length > 0)
		{
			return false;
		}
		string host = page2.Host;
		bool result;
		if (!(host == "www.razer.com"))
		{
			if (host == "rog.asus.com")
			{
				if (image)
				{
					string host2 = uri2.Host;
					bool flag = ((host2 == "dlcdnwebimgs.asus.com" || host2 == "dlcdnimgs.asus.com") ? true : false);
					result = flag;
				}
				else
				{
					result = uri2.Host == page2.Host;
				}
				return result;
			}
			return KnownProducts.Any((OfficialImageProduct p) => p.Page == page2 && ((!image) ? (uri2 == page2) : p.Colors.Any((OfficialImageColor c) => c.Image == uri2)));
		}
		if (image)
		{
			bool flag;
			switch (uri2.Host)
			{
			case "assets2.razerzone.com":
			case "assets3.razerzone.com":
			case "assets.razerzone.com":
			case "assets.razer.com":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			result = flag;
		}
		else
		{
			result = uri2.Host == page2.Host;
		}
		return result;
	}

	private static string PageModel(Uri page)
	{
		string[] array = page.AbsolutePath.TrimEnd('/').Split('/');
		string text = array[^1];
		bool flag = ((text == "buy" || text == "gallery") ? true : false);
		if (flag || text.StartsWith("RZ", StringComparison.OrdinalIgnoreCase))
		{
			text = array[^2];
		}
		string name;
		if (!text.EndsWith("-model"))
		{
			name = text;
		}
		else
		{
			string text2 = text;
			name = text2.Substring(0, text2.Length - 6);
		}
		return DeviceCatalog.ModelKey(name);
	}

	private static Dictionary<string, string> Attributes(string tag)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match item in Regex.Matches(tag, "([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')"))
		{
			dictionary[item.Groups[1].Value] = WebUtility.HtmlDecode(item.Groups[2].Success ? item.Groups[2].Value : item.Groups[3].Value);
		}
		return dictionary;
	}

	internal static Uri? ImageFromPage(Uri page, string html)
	{
		if (!Allowed(page, page, image: false))
		{
			return null;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match item in Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase))
		{
			Dictionary<string, string> dictionary2 = Attributes(item.Value);
			if (dictionary2.TryGetValue("property", out var value) && dictionary2.TryGetValue("content", out var value2))
			{
				dictionary.TryAdd(value, value2);
			}
		}
		if (!dictionary.TryGetValue("og:title", out var value3) || !DeviceCatalog.ModelKey(value3).Contains(PageModel(page), StringComparison.Ordinal))
		{
			return null;
		}
		if (!dictionary.TryGetValue("og:image", out var value4) || !Uri.TryCreate(page, value4, out Uri result) || !Allowed(result, page, image: true))
		{
			return null;
		}
		return result;
	}

	private static string ColorLabel(string value)
	{
		return value.Trim().ToLowerInvariant() switch
		{
			"black" => "黑色", 
			"white" => "白色", 
			"moonlight white" => "月光白", 
			"quartz" => "粉晶", 
			"mercury" => "水银白", 
			"green" => "绿色", 
			"phantom green" => "幻影绿", 
			_ => value.Substring(0, Math.Min(value.Length, 60)), 
		};
	}

	internal static IReadOnlyList<OfficialImageVariant> VariantsFromPage(Uri page, string html)
	{
		Uri page2 = page;
		List<OfficialImageVariant> variants = new List<OfficialImageVariant>();
		if (!Allowed(page2, page2, image: false))
		{
			return variants;
		}
		if (page2.Host == "www.razer.com")
		{
			Match match = Regex.Match(html, "<script[^>]+id=[\"']ng-state[\"'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
			if (!match.Success)
			{
				return variants;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(match.Groups[1].Value);
			Visit(jsonDocument.RootElement);
		}
		else
		{
			foreach (Match item in Regex.Matches(html, "<img\\b[^>]*>", RegexOptions.IgnoreCase))
			{
				Dictionary<string, string> dictionary = Attributes(item.Value);
				if (dictionary.TryGetValue("alt", out var value) && DeviceCatalog.ModelKey(value).Contains(PageModel(page2), StringComparison.Ordinal) && dictionary.TryGetValue("src", out var value2))
				{
					string text = value.ToLowerInvariant();
					if (!text.Contains("black") || !text.Contains("white"))
					{
						string text2 = new string[5] { "moonlight white", "white", "black", "pink", "red" }.FirstOrDefault(text.Contains);
						Add((text2 == null) ? "官方外观" : ColorLabel(text2), value2);
					}
				}
			}
		}
		return variants.Take(8).ToArray();
		void Add(string label, string url)
		{
			string label2 = label;
			if (label2.Length > 0 && Uri.TryCreate(page2, url, out Uri result) && Allowed(result, page2, image: true) && !result.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && variants.All((OfficialImageVariant v) => v.Label != label2))
			{
				variants.Add(new OfficialImageVariant(DeviceCatalog.Key(result.AbsoluteUri), label2, page2, result));
			}
		}
		void Visit(JsonElement node)
		{
			if (node.ValueKind == JsonValueKind.Object)
			{
				if (node.TryGetProperty("baseOptions", out var value3) && value3.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item2 in value3.EnumerateArray())
					{
						if (item2.TryGetProperty("options", out var value4) && value4.ValueKind == JsonValueKind.Array)
						{
							foreach (JsonElement item3 in value4.EnumerateArray())
							{
								if (item3.TryGetProperty("name", out var value5) && !(DeviceCatalog.ModelKey(value5.GetString().Split(" - ")[0]) != PageModel(page2)) && item3.TryGetProperty("images", out var value6) && value6.ValueKind == JsonValueKind.Array && item3.TryGetProperty("variantOptionQualifiers", out var value7) && value7.ValueKind == JsonValueKind.Array)
								{
									JsonElement value11;
									JsonElement jsonElement = value7.EnumerateArray().FirstOrDefault((JsonElement q) => q.TryGetProperty("qualifier", out value11) && value11.GetString() == "color");
									JsonElement value10;
									JsonElement jsonElement2 = value6.EnumerateArray().FirstOrDefault((JsonElement i) => i.TryGetProperty("format", out value10) && value10.GetString() == "product");
									if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement2.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("value", out var value8) && jsonElement2.TryGetProperty("url", out var value9))
									{
										Add(ColorLabel(value8.GetString() ?? ""), value9.GetString() ?? "");
									}
								}
							}
						}
					}
					return;
				}
				{
					foreach (JsonProperty item4 in node.EnumerateObject())
					{
						Visit(item4.Value);
					}
					return;
				}
			}
			if (node.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item5 in node.EnumerateArray())
				{
					Visit(item5);
				}
			}
		}
	}

	internal Task<string?> GetAsync(string name, CancellationToken cancellation = default(CancellationToken))
	{
		string key = FileKey(name);
		if (requests.TryGetValue(key, out (DateTimeOffset, Task<string>) value) && value.Item1 > DateTimeOffset.UtcNow)
		{
			return value.Item2;
		}
		Task<string> task = LoadAsync(name, cancellation);
		requests[key] = (DateTimeOffset.UtcNow.AddHours(24.0), task);
		return task;
	}

	private async Task<string?> LoadAsync(string name, CancellationToken cancellation)
	{
		string folder = Path.Combine(_003Croot_003EP, "data", "device-images");
		string file = Path.Combine(folder, FileKey(name) + ".png");
		string retry = Path.Combine(folder, FileKey(name) + ".retry");
		await slots.WaitAsync(cancellation).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string[] array = new string[2]
			{
				OverridePath(_003Croot_003EP, name),
				file
			};
			foreach (string candidate in array)
			{
				bool flag = File.Exists(candidate);
				if (flag)
				{
					flag = await Task.Run(() => ValidImage(candidate), cancellation).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (flag)
				{
					return candidate;
				}
			}
			Uri page = PageFor(name);
			if ((object)page == null || (File.Exists(retry) && DateTime.UtcNow - File.GetLastWriteTimeUtc(retry) < TimeSpan.FromHours(24.0)))
			{
				return null;
			}
			Directory.CreateDirectory(folder);
			await File.WriteAllTextAsync(retry, "", cancellation).ConfigureAwait(continueOnCapturedContext: false);
			IReadOnlyList<OfficialImageVariant> variants = CatalogVariants(name);
			string html = "";
			if (variants.Count == 0)
			{
				Uri uri = new Uri(page.AbsoluteUri.TrimEnd('/') + ((page.Host == "www.razer.com") ? "/buy" : "/gallery/"));
				try
				{
					Encoding uTF = Encoding.UTF8;
					html = uTF.GetString(await FetchAsync(uri, page, image: false, cancellation).ConfigureAwait(continueOnCapturedContext: false));
				}
				catch (Exception) when (!cancellation.IsCancellationRequested)
				{
					Encoding uTF = Encoding.UTF8;
					html = uTF.GetString(await FetchAsync(page, page, image: false, cancellation).ConfigureAwait(continueOnCapturedContext: false));
				}
				variants = VariantsFromPage(page, html);
			}
			if (variants.Count > 0)
			{
				await SaveVariants(name, variants, cancellation).ConfigureAwait(continueOnCapturedContext: false);
			}
			Uri image = variants.FirstOrDefault()?.Image ?? ImageFromPage(page, html) ?? throw new InvalidDataException("官网页面未提供匹配型号的图片");
			byte[] bytes = await FetchAsync(image, page, image: true, cancellation).ConfigureAwait(continueOnCapturedContext: false);
			byte[] bytes2 = await Task.Run(() => NormalizeImage(bytes), cancellation).ConfigureAwait(continueOnCapturedContext: false);
			await File.WriteAllBytesAsync(file + ".tmp", bytes2, cancellation).ConfigureAwait(continueOnCapturedContext: false);
			File.Move(file + ".tmp", file, overwrite: true);
			await File.WriteAllTextAsync(file + ".source.json", JsonSerializer.Serialize(new
			{
				page = page.AbsoluteUri,
				image = image.AbsoluteUri,
				fetchedAt = DateTimeOffset.UtcNow
			}), cancellation).ConfigureAwait(continueOnCapturedContext: false);
			File.Delete(retry);
			_003Clog_003EP.Write("info", "images", "download", page.AbsoluteUri + "; 官网产品图已缓存");
			return file;
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception ex3)
		{
			_003Clog_003EP.Write("info", "images", "fallback", "官网产品图不可用，继续使用通用图标；" + ex3.GetType().Name);
			return null;
		}
		finally
		{
			slots.Release();
		}
	}

	private string OptionsFile(string name)
	{
		return Path.Combine(_003Croot_003EP, "data", "device-images", FileKey(name) + ".options.json");
	}

	private Task SaveVariants(string name, IReadOnlyList<OfficialImageVariant> variants, CancellationToken cancellation)
	{
		return File.WriteAllTextAsync(OptionsFile(name), JsonSerializer.Serialize(variants), cancellation);
	}

	internal Task<IReadOnlyList<ProductImageOption>> GetOptionsAsync(string name, CancellationToken cancellation = default(CancellationToken))
	{
		string key = FileKey(name);
		if (optionRequests.TryGetValue(key, out (DateTimeOffset, Task<IReadOnlyList<ProductImageOption>>) value) && value.Item1 > DateTimeOffset.UtcNow)
		{
			return value.Item2;
		}
		Task<IReadOnlyList<ProductImageOption>> task = LoadOptionsAsync(name, GetAsync(name, cancellation), cancellation);
		optionRequests[key] = (DateTimeOffset.UtcNow.AddHours(24.0), task);
		return task;
	}

	private async Task<IReadOnlyList<ProductImageOption>> LoadOptionsAsync(string name, Task<string?> automatic, CancellationToken cancellation)
	{
		List<ProductImageOption> list = new List<ProductImageOption>();
		List<ProductImageOption> list2 = list;
		list2.Add(new ProductImageOption("auto", "自动", await automatic.ConfigureAwait(continueOnCapturedContext: false)));
		List<ProductImageOption> choices = list;
		string local = OverridePath(_003Croot_003EP, name);
		bool flag = File.Exists(local);
		if (flag)
		{
			flag = await Task.Run(() => ValidImage(local), cancellation).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (flag)
		{
			choices.Add(new ProductImageOption("local", "本地图片", local));
		}
		string custom = CustomPath(_003Croot_003EP, name);
		flag = File.Exists(custom);
		if (flag)
		{
			flag = await Task.Run(() => ValidImage(custom), cancellation).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (flag)
		{
			choices.Add(new ProductImageOption("custom", "自定义图片", custom));
		}
		try
		{
			Uri page = PageFor(name);
			if ((object)page != null)
			{
				string metadata = OptionsFile(name);
				IReadOnlyList<OfficialImageVariant> variants = CatalogVariants(name);
				bool pinned = variants.Count > 0;
				if (!pinned && File.Exists(metadata) && new FileInfo(metadata).Length < 1048576)
				{
					try
					{
						variants = JsonSerializer.Deserialize<OfficialImageVariant[]>(await File.ReadAllTextAsync(metadata, cancellation).ConfigureAwait(continueOnCapturedContext: false)) ?? Array.Empty<OfficialImageVariant>();
					}
					catch (JsonException)
					{
					}
				}
				if (!pinned && (variants.Count == 0 || DateTime.UtcNow - File.GetLastWriteTimeUtc(metadata) > TimeSpan.FromDays(7.0)))
				{
					try
					{
						Uri uri = new Uri(page.AbsoluteUri.TrimEnd('/') + ((page.Host == "www.razer.com") ? "/buy" : "/gallery/"));
						Encoding uTF = Encoding.UTF8;
						string @string = uTF.GetString(await FetchAsync(uri, page, image: false, cancellation).ConfigureAwait(continueOnCapturedContext: false));
						IReadOnlyList<OfficialImageVariant> readOnlyList = VariantsFromPage(page, @string);
						if (readOnlyList.Count > 0 || variants.Count == 0)
						{
							variants = readOnlyList;
							Directory.CreateDirectory(Path.GetDirectoryName(metadata));
							await SaveVariants(name, variants, cancellation).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
					catch (Exception) when (variants.Count > 0 && !cancellation.IsCancellationRequested)
					{
					}
				}
				foreach (OfficialImageVariant variant in variants.Take(8))
				{
					try
					{
						if (!Regex.IsMatch(variant.Id, "^[0-9a-f]{24}$") || !Allowed(variant.Page, page, image: false) || PageModel(variant.Page) != PageModel(page) || !Allowed(variant.Image, page, image: true))
						{
							continue;
						}
						string file = Path.Combine(_003Croot_003EP, "data", "device-images", FileKey(name) + "-" + variant.Id + ".png");
						flag = !File.Exists(file);
						if (!flag)
						{
							flag = !(await Task.Run(() => ValidImage(file), cancellation).ConfigureAwait(continueOnCapturedContext: false));
						}
						if (flag)
						{
							byte[] bytes = await FetchAsync(variant.Image, page, image: true, cancellation).ConfigureAwait(continueOnCapturedContext: false);
							byte[] bytes2 = await Task.Run(() => NormalizeImage(bytes), cancellation).ConfigureAwait(continueOnCapturedContext: false);
							await File.WriteAllBytesAsync(file + ".tmp", bytes2, cancellation).ConfigureAwait(continueOnCapturedContext: false);
							File.Move(file + ".tmp", file, overwrite: true);
						}
						choices.Add(new ProductImageOption(variant.Id, variant.Label, file));
					}
					catch (Exception) when (!cancellation.IsCancellationRequested)
					{
					}
				}
			}
		}
		catch (Exception ex4) when (!cancellation.IsCancellationRequested)
		{
			_003Clog_003EP.Write("error", "images", "colors", "官网颜色暂不可用；" + ex4.GetType().Name, ex4);
		}
		if (choices.Count > 0 && choices[0].Id == "auto" && choices[0].FilePath is null)
		{
			var official = choices.FirstOrDefault(o => o.FilePath is not null &&
				o.Id is not ("auto" or "local" or "custom" or "generic"));
			if (official is not null) choices[0] = choices[0] with { FilePath = official.FilePath };
		}
		var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		choices = choices.Where(o => o.FilePath is null || paths.Add(o.FilePath)).ToList();
		choices.Add(new ProductImageOption("generic", "通用图标", null));
		return choices;
	}

	private async Task<byte[]> FetchAsync(Uri uri, Uri page, bool image, CancellationToken cancellation)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		timeout.CancelAfter(TimeSpan.FromSeconds(12.0));
		int limit = (image ? 5242880 : 3145728);
		for (int redirects = 0; redirects <= 3; redirects++)
		{
			if (!Allowed(uri, page, image) || (!image && PageModel(uri) != PageModel(page)))
			{
				throw new InvalidDataException("非官网或不同型号地址");
			}
			using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, uri);
			message.Headers.UserAgent.ParseAdd("PeripheralBattery/0.2 (official-product-images)");
			using HttpResponseMessage response = await (_003Ctransport_003EP ?? Client).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			Uri uri2 = response.RequestMessage?.RequestUri;
			if ((object)uri2 != null && (!Allowed(uri2, page, image) || (!image && PageModel(uri2) != PageModel(page))))
			{
				throw new InvalidDataException("非官网重定向");
			}
			int statusCode = (int)response.StatusCode;
			if (statusCode < 300 || statusCode >= 400)
			{
				goto IL_023b;
			}
			Uri location = response.Headers.Location;
			if ((object)location == null)
			{
				goto IL_023b;
			}
			uri = new Uri(uri, location);
			goto end_IL_0194;
			IL_023b:
			response.EnsureSuccessStatusCode();
			string text = response.Content.Headers.ContentType?.MediaType;
			bool flag2;
			if (image)
			{
				bool flag = ((text == "image/png" || text == "image/jpeg") ? true : false);
				flag2 = !flag;
			}
			else
			{
				flag2 = text != "text/html";
			}
			if (flag2 || response.Content.Headers.ContentLength > limit)
			{
				throw new InvalidDataException("图片或页面类型、大小不符合要求");
			}
			using (Stream source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false))
			{
				using MemoryStream output = new MemoryStream();
				byte[] buffer = new byte[16384];
				int num;
				while ((num = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(continueOnCapturedContext: false)) > 0)
				{
					if (output.Length + num > limit)
					{
						throw new InvalidDataException("下载超出大小限制");
					}
					output.Write(buffer, 0, num);
				}
				return output.ToArray();
			}
			end_IL_0194:;
		}
		throw new InvalidDataException("官网重定向次数过多");
	}

	internal static byte[] NormalizeImage(byte[] bytes)
	{
		using MemoryStream memoryStream = new MemoryStream(bytes);
		BitmapFrame bitmapFrame = BitmapDecoder.Create(memoryStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
		if (bitmapFrame.PixelWidth > 8192 || bitmapFrame.PixelHeight > 8192 || (long)bitmapFrame.PixelWidth * (long)bitmapFrame.PixelHeight > 25000000)
		{
			throw new InvalidDataException("图片尺寸超出限制");
		}
		memoryStream.Position = 0L;
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.StreamSource = memoryStream;
		if (bitmapFrame.PixelWidth >= bitmapFrame.PixelHeight)
		{
			bitmapImage.DecodePixelWidth = Math.Min(bitmapFrame.PixelWidth, 512);
		}
		else
		{
			bitmapImage.DecodePixelHeight = Math.Min(bitmapFrame.PixelHeight, 512);
		}
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapImage));
		using MemoryStream memoryStream2 = new MemoryStream();
		pngBitmapEncoder.Save(memoryStream2);
		return memoryStream2.ToArray();
	}

	private static bool ValidImage(string file)
	{
		try
		{
			if (new FileInfo(file).Length > 5242880)
			{
				return false;
			}
			NormalizeImage(File.ReadAllBytes(file));
			return true;
		}
		catch
		{
			return false;
		}
	}
}
