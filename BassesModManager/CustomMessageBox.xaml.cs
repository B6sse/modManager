using System.Windows;

namespace BassesModManager
{
   public partial class CustomMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    public CustomMessageBox(string message, string title = "Error", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        InitializeComponent();
        this.Title = title;
        MessageText.Text = message;
        CancelButton.Visibility = (buttons == MessageBoxButton.OKCancel) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.OK;
        this.DialogResult = true;
        this.Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        this.DialogResult = false;
        this.Close();
    }

    public static MessageBoxResult Show(Window owner, string message, string title = "Error", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var box = new CustomMessageBox(message, title, buttons, icon);
        box.Owner = owner;
        box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        box.ShowDialog();
        return box._result;
    }
}
}