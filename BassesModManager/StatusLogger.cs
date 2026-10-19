using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace BassesModManager
{
    /// <summary>
    /// The status line of a progress panel, writable from any thread: the flow behind the
    /// panel runs on a worker and reports what it is doing through <see cref="Status"/> or
    /// <see cref="Log"/>, and the line on screen follows.
    /// </summary>
    internal sealed class StatusLogger : INotifyPropertyChanged
    {
        private readonly Window _window;
        private string _status;

        public event PropertyChangedEventHandler PropertyChanged;

        public StatusLogger(Window window, TextBlock statusText)
        {
            _window = window;
            BindingOperations.SetBinding(statusText, TextBlock.TextProperty,
                new Binding(nameof(Status)) { Source = this, Mode = BindingMode.OneWay });
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value)
                    return;
                string v = value;
                _window.Dispatcher.InvokeAsync(() =>
                {
                    _status = v;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                }, DispatcherPriority.Normal);
            }
        }

        public void Log(string text, params object[] vars) => Status = string.Format(text, vars);
    }
}
