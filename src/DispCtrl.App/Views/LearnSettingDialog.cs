using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Display;
using DispCtrl.Display.Devices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views;

/// <summary>A small guided mapper for one manufacturer monitor setting.</summary>
public sealed class LearnSettingDialog
{
    private readonly DisplayInfo _display;
    private readonly ControlService _service = new();
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly CancellationTokenSource _cancel = new();
    private readonly ObservableCollection<LearnChange> _changes = [];
    private readonly Dictionary<byte, int> _baseline = [];
    private readonly Dictionary<byte, Dictionary<int, string>> _namedValues = [];
    private Dictionary<byte, ResolvedControl> _existing = [];
    private MonitorCapability _capabilities = MonitorCapability.None;
    private bool _saving;
    private Dictionary<int, string> Values(byte code)
    {
        if (!_namedValues.TryGetValue(code, out var values)) _namedValues[code] = values = [];
        return values;
    }
    private readonly ListBox _changed = new();
    private readonly TextBox _name = new() { Header = "Setting name", PlaceholderText = "Picture mode" };
    private readonly ComboBox _kind = new() { Header = "Kind", ItemsSource = new[] { "choice", "range" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _valueName = new() { Header = "Name for the current value", PlaceholderText = "Movie" };
    private readonly NumberBox _maximum = new() { Header = "Maximum value", Minimum = 0, Maximum = 65535, Value = double.NaN, Visibility = Visibility.Collapsed };
    private readonly CheckBox _writable = new()
    {
        Content = "I tested it or want DispCtrl to write this code",
        IsChecked = false,
    };
    private readonly TextBlock _seen = Muted("No values named yet.");
    private readonly TextBlock _status = Muted("Reading the monitor...");
    private ContentDialog? _dialog;

    public LearnSettingDialog(DisplayInfo display) => _display = display;

    public async Task<bool> ShowAsync(XamlRoot root)
    {
        _changed.ItemsSource = _changes;
        _changed.DisplayMemberPath = nameof(LearnChange.Label);
        _changed.MinHeight = 96;
        _changed.SelectionChanged += (_, _) =>
        {
            if (_changed.SelectedItem is not LearnChange c) return;
            DefinedControl? before = _existing.GetValueOrDefault(c.CodeValue)?.Definition;
            _name.Text = before?.Name ?? "";
            _kind.SelectedIndex = before?.Kind == DefinedKinds.Range ? 1 : 0;
            _maximum.Value = before?.Maximum ?? (c.Maximum >= 0 ? c.Maximum : double.NaN);
            _writable.IsChecked = false;
            if (Values(c.CodeValue).Count == 0 && before is not null)
                foreach (DefinedValue value in before.Values)
                    if (value.Number is uint number) Values(c.CodeValue)[(int)number] = value.Name;
            ReadSelectedValueName();
        };

        var addValue = new Button { Content = "Add another value" };
        addValue.Click += (_, _) => AddCurrentValue();
        _kind.SelectionChanged += (_, _) =>
        {
            bool range = _kind.SelectedIndex == 1;
            _maximum.Visibility = range ? Visibility.Visible : Visibility.Collapsed;
            _valueName.Visibility = addValue.Visibility = _seen.Visibility = range ? Visibility.Collapsed : Visibility.Visible;
        };

        var body = new StackPanel { Spacing = 10, MinWidth = 460 };
        body.Children.Add(new TextBlock
        {
            Text = "Change ONE setting using your monitor's own buttons/menu, then come back.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(_status);
        body.Children.Add(_changed);
        body.Children.Add(_name);
        body.Children.Add(_kind);
        body.Children.Add(_maximum);
        body.Children.Add(_valueName);
        body.Children.Add(addValue);
        body.Children.Add(_seen);
        body.Children.Add(new TextBlock
        {
            Text = "Raw writes are advanced. Mapping a code first is safer; an unknown code can change settings the monitor's own menu cannot undo.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        body.Children.Add(_writable);

        bool saved = false;
        var scroll = new ScrollViewer { Content = body };
        _dialog = new ContentDialog
        {
            Title = $"Learn a setting on {_display.Label}",
            Content = scroll,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        _dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                _saving = true;
                scroll.IsEnabled = false;
                if (await SaveAsync()) saved = true;
                else args.Cancel = true;
            }
            catch (Exception ex) { _status.Text = ex.Message; args.Cancel = true; }
            finally { _saving = false; scroll.IsEnabled = true; deferral.Complete(); }
        };
        _dialog.Closed += (_, _) => _cancel.Cancel();

        Task polling = PollAsync();
        try { await _dialog.ShowAsync(); }
        finally
        {
            _cancel.Cancel();
            _ = polling.ContinueWith(_ => _cancel.Dispose(), TaskScheduler.Default);
        }
        return saved;
    }

    private async Task PollAsync()
    {
        List<VcpControl> wanted;
        try
        {
            _capabilities = await Task.Run(() => MonitorCapabilities.Read(_display, readValues: false, includeMappings: false), _cancel.Token);
            _existing = await Task.Run(() => DeviceLibrary.Resolve(_display.Key.Model), _cancel.Token);
            wanted = _capabilities.Controls
                .Where(c => !MonitorCapabilities.IsNamed(c.Code) && c.Code is not 0x04)
                .OrderBy(c => c.Code).ToList();
            if (wanted.Count == 0)
            {
                _ui.TryEnqueue(() => _status.Text = "This monitor did not list unnamed codes to watch.");
                return;
            }
            await ReadAsync(wanted);
            foreach (VcpControl c in wanted) if (c.Current >= 0) _baseline[c.Code] = c.Current;
            _ui.TryEnqueue(() => _status.Text = "Watching. Change one monitor setting now.");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _ui.TryEnqueue(() => _status.Text = "Could not read the monitor: " + ex.Message);
            return;
        }

        while (!_cancel.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _cancel.Token);
                await ReadAsync(wanted);
                foreach (VcpControl c in wanted)
                {
                    if (c.Current < 0) continue;
                    if (!_baseline.TryGetValue(c.Code, out int old)) { _baseline[c.Code] = c.Current; continue; }
                    int now = c.Current;
                    int maximum = c.Maximum;
                    _ui.TryEnqueue(() =>
                    {
                        LearnChange? existing = _changes.FirstOrDefault(x => x.CodeValue == c.Code);
                        if (_cancel.IsCancellationRequested || _saving) return;
                        if (existing is null && old != now)
                        {
                            _changes.Add(new LearnChange(c.Code, old, now, maximum));
                            _status.Text = "Pick the code that changed, then name it.";
                        }
                        else if (existing is not null && existing.Now != now)
                        {
                            existing.Now = now;
                            if (ReferenceEquals(_changed.SelectedItem, existing)) ReadSelectedValueName();
                        }
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _ui.TryEnqueue(() => _status.Text = "Watching paused: " + ex.Message);
                break;
            }
        }
    }

    private void ReadSelectedValueName()
    {
        if (_changed.SelectedItem is not LearnChange c) return;
        if (Values(c.CodeValue).TryGetValue(c.Now, out string? name)) _valueName.Text = name;
        else _valueName.Text = "";
        ShowValues(c.CodeValue);
    }

    private void AddCurrentValue()
    {
        if (_changed.SelectedItem is not LearnChange c)
        {
            _status.Text = "Pick the code that changed first.";
            return;
        }
        string name = _valueName.Text.Trim();
        if (name.Length == 0)
        {
            _status.Text = "Name the value the monitor is on now.";
            return;
        }
        Values(c.CodeValue)[c.Now] = name;
        ShowValues(c.CodeValue);
    }

    private Task ReadAsync(List<VcpControl> controls) => Task.Run(() =>
    {
        foreach (VcpControl control in controls)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            MonitorCapabilities.ReadValues(_display, [control]);
        }
        DeviceObserver.Listed(_display, _capabilities.Raw, controls);
    }, _cancel.Token);

    private void ShowValues(byte code) => _seen.Text = "Values: " +
        string.Join(", ", Values(code).OrderBy(p => p.Key).Select(p => $"0x{p.Key:X2}={p.Value}"));

    private async Task<bool> SaveAsync()
    {
        if (_changed.SelectedItem is not LearnChange c)
        {
            _status.Text = "Pick the code that changed.";
            return false;
        }
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _status.Text = "Name the setting.";
            return false;
        }
        string kind = _kind.SelectedItem as string ?? "choice";
        if (kind == "range" && (!double.IsFinite(_maximum.Value) || _maximum.Value != Math.Truncate(_maximum.Value)))
        {
            _status.Text = "Enter the range's maximum as a whole number from 0 to 65535.";
            return false;
        }
        if (kind == "choice")
        {
            if (!string.IsNullOrWhiteSpace(_valueName.Text)) AddCurrentValue();
            if (Values(c.CodeValue).Count == 0)
            {
                _status.Text = "Name at least one value.";
                return false;
            }
        }
        var args = new JsonObject
        {
            ["monitor"] = _display.Token,
            ["code"] = DeviceDefinitions.FormatCode(c.CodeValue),
            ["name"] = name,
            ["kind"] = kind,
            ["writable"] = _writable.IsChecked == true,
            ["scope"] = "model",
            ["confidence"] = "observed",
        };
        if (kind == "choice")
            args["values"] = new JsonArray(Values(c.CodeValue).OrderBy(p => p.Key)
                .Select(p => (JsonNode)new JsonObject { ["value"] = $"0x{p.Key:X2}", ["name"] = p.Value }).ToArray());
        else
        {
            args["values"] = new JsonArray();
            args["maximum"] = (int)_maximum.Value;
        }
        JsonObject result = await Task.Run(() => _service.Execute(new JsonObject
        {
            ["version"] = 1,
            ["command"] = "devices.map",
            ["args"] = args,
        }));
        if (result["ok"]?.GetValue<bool>() == true)
        {
            _status.Text = "Saved. Share it with others from Devices > Contribute.";
            return true;
        }
        _status.Text = result["error"]?["message"]?.GetValue<string>() ?? "The mapping was not saved.";
        return false;
    }

    private sealed class LearnChange(byte code, int old, int now, int maximum) : System.ComponentModel.INotifyPropertyChanged
    {
        public int Maximum => maximum;
        private int _now = now;
        public byte CodeValue => code;
        public int Now
        {
            get => _now;
            set
            {
                if (_now == value) return;
                _now = value;
                PropertyChanged?.Invoke(this, new(nameof(Now)));
                PropertyChanged?.Invoke(this, new(nameof(Label)));
            }
        }
        public string Label => $"{DeviceDefinitions.FormatCode(code)}   {old} → {Now}";
        public override string ToString() => Label;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
}
