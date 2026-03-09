using FrostySdk.Interfaces;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace BassesModManager
{
    /// <summary>
    /// Like Frosty: spinner during read phase (Loading Catalogs, Indexing).
    /// Progress bar only during write phase - smooth animation toward target so the bar fills gradually
    /// even when the backend completes a phase very quickly.
    /// </summary>
    internal class CacheInstallLogger : ILogger, INotifyPropertyChanged
    {
        private readonly CacheInstallWindow _window;
        private double _progress;
        private double _targetProgress;
        private string _status;
        private bool _isWritePhase;
        private string _currentWritePhase;
        private double _lastDispatchedTarget = -1;
        private DateTime _lastTargetDispatchTime = DateTime.MinValue;
        // Hvor mye målverdien må endre seg før vi oppdaterer (i prosentpoeng)
        private const double TargetThrottlePercent = 0.1;
        // Hvor ofte vi minst vil oppdatere målverdien (ms)
        private const int TargetThrottleMs = 15;
        // Hvor mye selve baren flytter seg per tick (i prosentpoeng)
        private const double ProgressStepPerTick = 0.4;
        // Hvor ofte timeren tikker (ms)
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
                        if (p <= 0) _progress = 0;
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

        public CacheInstallLogger(CacheInstallWindow window)
        {
            _window = window;
            BindingOperations.SetBinding(_window.ProgressBar, System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new Binding(nameof(Progress)) { Source = this, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(_window.StatusText, System.Windows.Controls.TextBlock.TextProperty,
                new Binding(nameof(Status)) { Source = this, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(_window.SpinnerPanel, System.Windows.UIElement.VisibilityProperty,
                new Binding(nameof(IsWritePhase)) { Source = this, Converter = new InvertBoolToVisibilityConverter() });
            BindingOperations.SetBinding(_window.ProgressBar, System.Windows.UIElement.VisibilityProperty,
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
