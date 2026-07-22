using FrostySdk.Interfaces;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace BassesModManager
{
    /// <summary>
    /// Like Frosty: spinner during read phase (Loading Catalogs, Indexing).
    /// Progress bar only during write phase - smooth animation toward target so the bar fills gradually
    /// even when the backend completes a phase very quickly.
    /// Shared between the cache install window and the launch progress window; with
    /// barOnAnyProgress the bar is shown as soon as any "progress:" message arrives
    /// (used by the launch flow, where phases are not named "Writing to cache").
    /// </summary>
    internal class SmoothProgressLogger : ILogger, INotifyPropertyChanged
    {
        private readonly Window _window;
        private readonly bool _barOnAnyProgress;
        private double _progress;
        private double _targetProgress;
        private string _status;
        private bool _isWritePhase;
        private string _currentWritePhase;
        private double _lastDispatchedTarget = -1;
        private DateTime _lastTargetDispatchTime = DateTime.MinValue;
        
        private const double TargetThrottlePercent = 0.1;
        private const int TargetThrottleMs = 15;
        private const double ProgressStepPerTick = 0.4;
        private const int TimerIntervalMs = 12;
        private DispatcherTimer _progressTimer;

        public event PropertyChangedEventHandler PropertyChanged;

        public double Progress
        {
            get => _progress;
            set
            {
                var p = Math.Min(100, Math.Max(0, value));
                var now = DateTime.UtcNow;
                var shouldDispatch = p >= 100 || p <= 0
                    || (p - _lastDispatchedTarget) >= TargetThrottlePercent
                    || (now - _lastTargetDispatchTime).TotalMilliseconds >= TargetThrottleMs;

                if (shouldDispatch)
                {
                    _lastDispatchedTarget = p;
                    _lastTargetDispatchTime = now;
                    _window.Dispatcher.InvokeAsync(() =>
                    {
                        _targetProgress = p;
                        if (p <= 0)
                            _progress = 0;
                        else if (p < _progress)
                            _progress = p; // phase restarted at a lower value - snap back
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                    }, DispatcherPriority.Background);
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                var v = value;
                _window.Dispatcher.InvokeAsync(() =>
                {
                    _status = v;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        public bool IsWritePhase
        {
            get => _isWritePhase;
            set
            {
                if (_isWritePhase == value) return;
                var v = value;
                _window.Dispatcher.InvokeAsync(() =>
                {
                    _isWritePhase = v;
                    if (v)
                    {
                        _progress = 0;
                        _targetProgress = 0;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                        EnsureProgressTimer();
                        _progressTimer?.Start();
                    }
                    else
                    {
                        _progressTimer?.Stop();
                    }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWritePhase)));
                }, DispatcherPriority.Normal);
            }
        }

        private void EnsureProgressTimer()
        {
            if (_progressTimer != null) return;
            _progressTimer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(TimerIntervalMs)
            };
            _progressTimer.Tick += (s, e) =>
            {
                if (_progress < _targetProgress)
                {
                    _progress = Math.Min(_progress + ProgressStepPerTick, _targetProgress);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                }
            };
        }

        public SmoothProgressLogger(Window window, ProgressBar progressBar, TextBlock statusText, UIElement spinnerPanel, bool barOnAnyProgress = false)
        {
            _window = window;
            _barOnAnyProgress = barOnAnyProgress;
            BindingOperations.SetBinding(progressBar, System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new Binding(nameof(Progress)) { Source = this, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(statusText, TextBlock.TextProperty,
                new Binding(nameof(Status)) { Source = this, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(spinnerPanel, UIElement.VisibilityProperty,
                new Binding(nameof(IsWritePhase)) { Source = this, Converter = new InvertBoolToVisibilityConverter() });
            BindingOperations.SetBinding(progressBar, UIElement.VisibilityProperty,
                new Binding(nameof(IsWritePhase)) { Source = this, Converter = new BoolToVisibilityConverter() });
        }

        public void Log(string text, params object[] vars)
        {
            var fullText = string.Format(text, vars);
            if (fullText.StartsWith("progress:"))
            {
                fullText = fullText.Replace("progress:", "").Trim();
                if (double.TryParse(fullText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p))
                {
                    if (_barOnAnyProgress)
                        IsWritePhase = true;
                    Progress = p;
                }
            }
            else
            {
                Status = fullText;
                if (fullText.Contains("Writing to cache"))
                {
                    IsWritePhase = true;
                    var phase = fullText.Replace("Writing to cache (", "").Replace(")", "").Trim();
                    if (phase != _currentWritePhase)
                    {
                        _currentWritePhase = phase;
                        _lastDispatchedTarget = -1;
                        Progress = 0;
                    }
                }
                else if (_barOnAnyProgress)
                {
                    // new named phase in the launch flow - make sure the next progress
                    // value is dispatched immediately instead of being throttled away
                    _lastDispatchedTarget = -1;
                }
            }
        }

        public void LogWarning(string text, params object[] vars) => Log(text, vars);
        public void LogError(string text, params object[] vars) => Log(text, vars);
    }

    public class InvertBoolToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
