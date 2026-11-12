using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AutoClicker.App.ViewModels;
using AutoClicker.App.Views;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Services;

/// <summary>Bound controls on the ordinary native window; opt-in smoke input is always fake.</summary>
internal static class DesktopRetentionAudit
{
    public static async Task RunAsync(MainWindow window, MainWindowViewModel main, LocalizationService localization, string path)
    {
        main.Profiles.NewProfileCommand.Execute(null);
        main.ShowProfilesCommand.Execute(null);
        await main.Profiles.FlushAsync();
        var selected = main.Profiles.SelectedProfile!;
        var order = main.Profiles.Items.Select(item => item.Id).ToArray();
        var tab = window.GetVisualDescendants().OfType<TabControl>().Single();
        main.ShowSettingsCommand.Execute(null);
        await Task.Delay(100); window.UpdateLayout();
        var settings = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var selectors = settings.GetVisualDescendants().OfType<ComboBox>().ToArray();
        var language = selectors.Single(control => control.Items.OfType<LanguageOption>().Any());
        var theme = selectors.Single(control => control.Items.OfType<LocalizedEnumOption<Appearance>>().Any());
        var list = window.FindControl<ListBox>("ProfilesList")!;
        main.ShowProfilesCommand.Execute(null);
        await Task.Delay(100); window.UpdateLayout();
        using var process = Process.GetCurrentProcess();
        var samples = new List<object>();
        var lastTime = Stopwatch.GetTimestamp(); var lastCpu = process.TotalProcessorTime;
        void Capture(string stage)
        {
            process.Refresh();
            var now = Stopwatch.GetTimestamp(); var cpu = process.TotalProcessorTime;
            var elapsed = Stopwatch.GetElapsedTime(lastTime, now).TotalSeconds;
            var cpuOneCore = (cpu - lastCpu).TotalSeconds / Math.Max(elapsed, 0.001) * 100;
            samples.Add(new { stage, elapsedSeconds = elapsed, workingSetMiB = process.WorkingSet64 / 1048576d,
                privateBytesMiB = process.PrivateMemorySize64 / 1048576d, managedHeapMiB = GC.GetTotalMemory(false) / 1048576d,
                heapAtLastGcMiB = GC.GetGCMemoryInfo().HeapSizeBytes / 1048576d,
                threads = process.Threads.Count, gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
                cpuOneCorePercent = cpuOneCore, cpuMachinePercent = cpuOneCore / Environment.ProcessorCount,
                settingsViews = window.GetVisualDescendants().OfType<SettingsView>().Count(),
                editorViews = window.GetVisualDescendants().OfType<ProfileEditorView>().Count() });
            lastTime = now; lastCpu = cpu;
        }
        Capture("fresh");
        await Task.Delay(TimeSpan.FromSeconds(60)); Capture("idle60");
        for (var round = 1; round <= 2; round++)
        {
        for (var i = 0; i < 20; i++) { main.ShowSettingsCommand.Execute(null); await YieldLayout(); main.ShowProfilesCommand.Execute(null); await YieldLayout(); }
        for (var i = 0; i < 50; i++) { tab.SelectedIndex = i % 4; await YieldLayout(); }
        for (var i = 0; i < 50; i++) { list.SelectedItem = main.Profiles.Items[i % main.Profiles.Items.Count]; await YieldLayout(); }
        main.ShowSettingsCommand.Execute(null); await YieldLayout();
        for (var i = 0; i < 20; i++)
        {
            var value = i % 2 == 0 ? ApplicationLanguage.English : ApplicationLanguage.Turkish;
            language.SelectedItem = main.Settings.LanguageOptions.Single(option => option.Value == value);
            await YieldLayout();
            if (localization.Language != value) throw new InvalidOperationException("Native UI language binding round-trip failed.");
        }
        for (var i = 0; i < 20; i++) { theme.SelectedItem = main.Settings.AppearanceOptions[i % main.Settings.AppearanceOptions.Length]; await YieldLayout(); }
        var dragSource = main.Profiles.Items[0];
        for (var i = 0; i < 4; i++)
        {
            var target = i % 2 == 0 ? main.Profiles.Items[^1] : main.Profiles.Items[0];
            if (!main.Profiles.BeginProfileDrag(dragSource)) throw new InvalidOperationException("Drag session could not start.");
            main.Profiles.SetDropTarget(target.Id, i % 2 == 0); await YieldLayout();
            main.Profiles.DropProfile(target.Id, i % 2 == 0); await YieldLayout();
        }
        main.Profiles.BeginProfileDrag(dragSource); main.Profiles.SetDropTarget(selected.Id); main.Profiles.CancelProfileDrag();
        main.OpenProfile(selected); tab.SelectedIndex = 0;
        if (!order.SequenceEqual(main.Profiles.Items.Select(item => item.Id))) throw new InvalidOperationException("UI stress changed profile order.");
        await main.Profiles.FlushAsync(); Capture($"afterActions{round}");
        await Task.Delay(TimeSpan.FromSeconds(60)); Capture($"settled60round{round}");
        await Task.Delay(TimeSpan.FromSeconds(30)); Capture($"settled90round{round}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { mode = "native-window-fake-mouse", processorCount = Environment.ProcessorCount,
            rounds = 2, settingsCyclesPerRound = 20, tabChangesPerRound = 50, profileChangesPerRound = 50, languageChangesPerRound = 20, themeChangesPerRound = 20,
            dragDropsPerRound = 4, cancelledDragsPerRound = 1,
            engineState = main.Profiles.EngineTelemetry.State.ToString(), selectedProfilePreserved = ReferenceEquals(selected, main.Profiles.SelectedProfile),
            orderPreserved = order.SequenceEqual(main.Profiles.Items.Select(item => item.Id)), samples }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Task YieldLayout() => Task.Delay(20);
}
