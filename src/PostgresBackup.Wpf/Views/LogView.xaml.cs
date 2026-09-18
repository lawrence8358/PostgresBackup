using System.Windows.Controls;

namespace PostgresBackup.Wpf.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LogTextBox.ScrollToEnd();
    }
}
