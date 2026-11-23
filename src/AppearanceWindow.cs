#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace PeriDock;

internal sealed class AppearanceWindow : Window
{
	private readonly WrapPanel options = new WrapPanel
	{
		Margin = new Thickness(12.0)
	};

	private readonly TextBlock status = Visuals.Text("正在获取可用颜色…", 13.0, Visuals.Muted);

	private readonly Dictionary<string, Border> cards = new Dictionary<string, Border>();

	private readonly Func<string, Task<ProductImageOption>> import;

	private readonly Func<ProductImageOption, bool> choose;

	private readonly BatteryState state;

	private readonly List<ProductImageOption> values = new List<ProductImageOption>();

	private string selected;

	private ProductImageOption? imported;

	private bool closed;

	internal AppearanceWindow(BatteryState state, string selected, Func<Task<IReadOnlyList<ProductImageOption>>> load, Func<ProductImageOption, bool> choose, Func<string, Task<ProductImageOption>> import)
	{
		Func<Task<IReadOnlyList<ProductImageOption>>> load2 = load;
		AppearanceWindow appearanceWindow = this;
		this.state = state;
		this.selected = selected;
		this.choose = choose;
		this.import = import;
		base.Title = "设备颜色与外观";
		base.Width = 600.0;
		base.Height = 540.0;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		DockPanel dockPanel = (DockPanel)(Visuals.WindowFrame(this).Child = new DockPanel());
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0, 16.0, 20.0, 12.0)
		};
		stackPanel.Children.Add(Visuals.Text("选择颜色/外观", 20.0, Visuals.Ink, FontWeights.SemiBold));
		TextBlock textBlock = Visuals.Text(state.Name, 14.0, Visuals.Muted);
		textBlock.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		stackPanel.Children.Add(textBlock);
		Button button = Visuals.Command("folder-open", "导入自定义图片…", async delegate
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "选择设备自定义图片",
				Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.ico",
				CheckFileExists = true,
				Multiselect = false,
				RestoreDirectory = true
			};
			if (openFileDialog.ShowDialog(appearanceWindow).GetValueOrDefault())
			{
				await appearanceWindow.ImportFile(openFileDialog.FileName);
			}
		});
		button.HorizontalAlignment = HorizontalAlignment.Left;
		button.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);
		button.ToolTip = "PNG / JPG / BMP / ICO，最大 10 MB；只保存在本机";
		stackPanel.Children.Add(button);
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		DockPanel dockPanel2 = new DockPanel
		{
			Margin = new Thickness(16.0, 8.0, 16.0, 12.0)
		};
		Button element = Visuals.Command("x", "关闭", base.Close);
		DockPanel.SetDock(element, Dock.Right);
		dockPanel2.Children.Add(element);
		status.MaxWidth = 420.0;
		dockPanel2.Children.Add(status);
		DockPanel.SetDock(dockPanel2, Dock.Bottom);
		dockPanel.Children.Add(dockPanel2);
		dockPanel.Children.Add(new ScrollViewer
		{
			Content = options,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		});
		base.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				appearanceWindow.Close();
			}
		};
		base.Closed += delegate
		{
			appearanceWindow.closed = true;
		};
		base.Loaded += async delegate
		{
			try
			{
				IReadOnlyList<ProductImageOption> collection = await load2();
				if (!appearanceWindow.closed)
				{
					appearanceWindow.values.Clear();
					appearanceWindow.values.AddRange(collection);
					if ((object)appearanceWindow.imported != null)
					{
						appearanceWindow.values.RemoveAll((ProductImageOption v) => v.Id == "custom");
						appearanceWindow.values.Insert(0, appearanceWindow.imported);
					}
					appearanceWindow.RenderOptions();
					if ((object)appearanceWindow.imported == null)
					{
						appearanceWindow.status.Text = "图片仅改变软件显示；自定义图片只保存在本机。";
					}
				}
			}
			catch (OperationCanceledException)
			{
				if (!appearanceWindow.closed)
				{
					appearanceWindow.status.Text = "获取已取消。";
				}
			}
			catch
			{
				if (!appearanceWindow.closed)
				{
					appearanceWindow.status.Text = "暂时无法获取图片，请稍后重试。";
				}
			}
		};
	}

	internal async Task ImportFile(string file)
	{
		try
		{
			ProductImageOption productImageOption = await import(file);
			if (!closed)
			{
				imported = productImageOption;
				values.RemoveAll((ProductImageOption v) => v.Id == "custom");
				values.Insert(0, productImageOption);
				Select(productImageOption);
				RenderOptions();
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			if (!closed)
			{
				status.Text = "导入失败：请选择有效图片（PNG/JPG/BMP/ICO，≤10 MB）并检查目录权限。";
			}
		}
	}

	private void Select(ProductImageOption value)
	{
		if (!choose(value))
		{
			status.Text = "保存失败，请检查目录是否可写。";
			return;
		}
		selected = value.Id;
		foreach (KeyValuePair<string, Border> card in cards)
		{
			card.Value.BorderBrush = ((card.Key == selected) ? Visuals.Green : Visuals.Line);
		}
		status.Text = "已选择：" + value.Label + "，已保存。";
	}

	private void RenderOptions()
	{
		cards.Clear();
		options.Children.Clear();
		foreach (ProductImageOption value in values)
		{
			BatteryState batteryState = new BatteryState(state.Id, state.Name, state.ShortName, state.Kind)
			{
				ProductImagePath = value.FilePath,
				GenericImage = value.Id == "generic"
			};
			StackPanel stackPanel = new StackPanel();
			stackPanel.Children.Add(Visuals.Product(batteryState, 140.0, 104.0));
			TextBlock textBlock = Visuals.Text(value.Label);
			textBlock.HorizontalAlignment = HorizontalAlignment.Center;
			textBlock.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
			stackPanel.Children.Add(textBlock);
			Button button = new Button
			{
				Content = stackPanel,
				Background = Visuals.Surface,
				BorderThickness = new Thickness(0.0),
				Padding = new Thickness(6.0),
				Cursor = Cursors.Hand,
				Foreground = Visuals.Ink
			};
			Border border = new Border
			{
				Child = button,
				BorderThickness = new Thickness(2.0),
				BorderBrush = ((value.Id == selected) ? Visuals.Green : Visuals.Line),
				Margin = new Thickness(4.0),
				CornerRadius = new CornerRadius(4.0)
			};
			cards[value.Id] = border;
			options.Children.Add(border);
			AutomationProperties.SetName(button, "选择" + value.Label);
			button.Click += delegate
			{
				Select(value);
			};
		}
	}
}
