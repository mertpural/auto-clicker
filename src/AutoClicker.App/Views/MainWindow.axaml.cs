using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AutoClicker.App.ViewModels;
using AutoClicker.Core.Models;

namespace AutoClicker.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<string> ProfileDragFormat = DataFormat.CreateStringApplicationFormat("autoclicker-profile-id");
    private Control? _dragHandle;
    private ProfileEditorViewModel? _dragProfile;
    private Point _dragStart;
    private bool _dragStarted;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            // Blend the native title bar into the theme: content extends under it, traffic lights stay.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
            LayoutRoot.Margin = new Thickness(0, 30, 0, 0);
        }
        // Coming back from System Settings: re-check macOS permissions without requiring a manual Retry.
        Activated += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel { Profiles: { IsMacOS: true, ShowPermissionWarning: true } profiles })
            {
                await profiles.RetryInputPermissionCommand.ExecuteAsync(null);
            }
        };
        AddHandler(KeyDownEvent, CaptureHotkey, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, ProfileList_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Profiles_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel && args.AddedItems.Count != 0)
            viewModel.OpenProfile((ProfileEditorViewModel)args.AddedItems[0]!);
    }

    private void RenameProfile_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.Profiles.SelectedProfile is { } profile
            && viewModel.Profiles.BeginRename(profile))
        {
            viewModel.ShowProfilesCommand.Execute(null);
            FocusInlineRename(profile);
        }
    }

    private void FocusInlineRename(ProfileEditorViewModel profile)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ProfilesList.ContainerFromItem(profile) is not Control container) return;
            var editor = container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control => control.Name == "InlineProfileName");
            editor?.Focus();
            editor?.SelectAll();
        });
    }

    private void InlineProfileName_KeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not TextBox { DataContext: ProfileEditorViewModel profile }) return;
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            viewModel.Profiles.CommitRename(profile);
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            viewModel.Profiles.CancelRename(profile);
            ProfilesList.Focus();
        }
    }

    private void InlineProfileName_LostFocus(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is TextBox { DataContext: ProfileEditorViewModel { IsRenaming: true } profile })
            viewModel.Profiles.CommitRename(profile);
    }

    private void ProfileList_PointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Source is not Visual source || DataContext is not MainWindowViewModel viewModel) return;
        var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
        if (ancestors.OfType<Control>().FirstOrDefault(control => control.Name == "ProfileDragHandle") is { } handle)
        { ProfileDragHandle_PointerPressed(handle, args); return; }
        if (ancestors.OfType<Control>().Any(control => control.Name == "InlineProfileName")) return;
        if (ancestors.OfType<ListBoxItem>().FirstOrDefault() is not { DataContext: ProfileEditorViewModel profile } item) return;
        // Pressing anywhere on a row starts a reorder gesture; it only moves once the pointer passes the threshold.
        // Select immediately on press (as before), then arm a reorder gesture that starts after the drag threshold.
        if (args.GetCurrentPoint(null).Properties.IsRightButtonPressed || args.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            viewModel.OpenProfile(profile);
        if (args.GetCurrentPoint(null).Properties.IsLeftButtonPressed
            && item.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == "ProfileDragHandle") is { } rowHandle
            && BeginProfileDragPress(rowHandle, args))
            args.Handled = true; // keep the ListBox from taking the pointer capture
    }

    private void ProfileDragHandle_PointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is Control handle && BeginProfileDragPress(handle, args)) args.Handled = true;
    }

    private bool BeginProfileDragPress(Control handle, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(null).Properties.IsLeftButtonPressed
            || handle.DataContext is not ProfileEditorViewModel profile
            || !ProfileDragGesture.CanStart(profile)
            || DataContext is not MainWindowViewModel) return false;
        _dragHandle = handle;
        _dragProfile = profile;
        _dragStart = args.GetPosition(_dragHandle);
        _dragStarted = false;
        args.Pointer.Capture(_dragHandle);
        return true;
    }

    private async void ProfileDragHandle_PointerMoved(object? sender, PointerEventArgs args)
    {
        if (_dragStarted || _dragHandle is null || _dragProfile is not { IsRenaming: false } profile
            || !args.GetCurrentPoint(_dragHandle).Properties.IsLeftButtonPressed
            || DataContext is not MainWindowViewModel viewModel) return;
        var current = args.GetPosition(_dragHandle);
        if (!ProfileDragGesture.HasPassedThreshold(_dragStart, current)) return;
        if (!viewModel.Profiles.BeginProfileDrag(profile)) { ResetProfileDrag(); return; }
        _dragStarted = true;
        var transfer = new DataTransfer();
        var transferItem = new DataTransferItem();
        transferItem.Set(ProfileDragFormat, profile.Id.ToString("D"));
        transfer.Add(transferItem);
        args.Pointer.Capture(null);
        try
        {
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move);
        }
        finally
        {
            viewModel.Profiles.CancelProfileDrag();
            ResetProfileDrag();
        }
    }

    private void ProfileDragHandle_PointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        args.Handled = true;
        if (!_dragStarted) ResetProfileDrag();
    }

    private void ProfileDragHandle_PointerCaptureLost(object? sender, RoutedEventArgs args)
    {
        if (!_dragStarted) ResetProfileDrag();
    }

    private void ResetProfileDrag()
    {
        _dragHandle = null;
        _dragProfile = null;
        _dragStarted = false;
    }

    private void ProfileItem_DragOver(object? sender, DragEventArgs args)
    {
        if (sender is not Control { DataContext: ProfileEditorViewModel { IsRenaming: false } target }
            || DataContext is not MainWindowViewModel viewModel
            || args.DataTransfer.TryGetValue(ProfileDragFormat) is not string sourceId
            || !Guid.TryParse(sourceId, out var sourceProfileId)
            || viewModel.Profiles.Items.FirstOrDefault(profile => profile.Id == sourceProfileId) is not { IsRenaming: false }
            || sourceProfileId == target.Id)
        { args.DragEffects = DragDropEffects.None; return; }
        var point = args.GetPosition((Control)sender);
        args.DragEffects = DragDropEffects.Move;
        viewModel.Profiles.SetDropTarget(target.Id, point.Y >= ((Control)sender).Bounds.Height / 2);
    }

    private void ProfileItem_DragLeave(object? sender, DragEventArgs args)
    {
        if (sender is Control control && !new Rect(control.Bounds.Size).Contains(args.GetPosition(control))
            && DataContext is MainWindowViewModel viewModel) viewModel.Profiles.SetDropTarget(null);
    }

    private void ProfileItem_Drop(object? sender, DragEventArgs args)
    {
        if (sender is Control { DataContext: ProfileEditorViewModel { IsRenaming: false } target }
            && DataContext is MainWindowViewModel viewModel
            && args.DataTransfer.TryGetValue(ProfileDragFormat) is string sourceId
            && Guid.TryParse(sourceId, out var sourceProfileId))
        {
            var point = args.GetPosition((Control)sender);
            var after = point.Y >= ((Control)sender).Bounds.Height / 2;
            args.DragEffects = viewModel.Profiles.DropProfile(target.Id, after)
                ? DragDropEffects.Move : DragDropEffects.None;
        }
        else args.DragEffects = DragDropEffects.None;
        if (DataContext is MainWindowViewModel main) main.Profiles.CancelProfileDrag();
    }

    private void CaptureHotkey(object? sender, KeyEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var profiles = viewModel.Profiles;
        var selected = profiles.SelectedProfile;

        if (selected is { IsRecording: true } editor)
        {
            args.Handled = true;
            if (args.Key == Key.Escape) { editor.IsRecording = false; return; }
            if (args.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
            var modifiers = HotkeyModifiers.None;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Meta;
            var key = args.Key.ToString();
            if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key[1..];
            try { editor.CompleteRecording(new Hotkey(key, modifiers)); }
            catch (ArgumentException)
            {
                editor.RecordingMessage = profiles.Localize("Hotkey_RecordingInvalid");
            }
            return;
        }

        if (args.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var pressedModifiers = HotkeyModifiers.None;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) pressedModifiers |= HotkeyModifiers.Control;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) pressedModifiers |= HotkeyModifiers.Shift;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) pressedModifiers |= HotkeyModifiers.Alt;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) pressedModifiers |= HotkeyModifiers.Meta;
        var pressedKey = args.Key.ToString();
        if (pressedKey.Length == 2 && pressedKey[0] == 'D' && char.IsDigit(pressedKey[1])) pressedKey = pressedKey[1..];

        Hotkey? currentHotkey = null;
        try { currentHotkey = new Hotkey(pressedKey, pressedModifiers); } catch { }
        if (currentHotkey is null) return;

        if (Hotkey.TryParse(viewModel.Settings.EmergencyStopHotkey, out var emergency) && currentHotkey == emergency)
        {
            args.Handled = true;
            _ = profiles.StopEngineAsync();
            return;
        }

        if (FocusManager?.GetFocusedElement() is TextBox) return;

        if (selected is not null && selected.TryGetSnapshot(out var profileSnapshot))
        {
            if (profileSnapshot.ClickMode is not (AutoClicker.Core.Models.ClickMode.MouseHold or AutoClicker.Core.Models.ClickMode.HoldMouseButton) && currentHotkey == profileSnapshot.StartStopHotkey)
            {
                args.Handled = true;
                if (profiles.EngineBusy) _ = profiles.StopEngineAsync();
                else _ = profiles.ToggleEngineCommand.ExecuteAsync(null);
            }
        }
    }
}
