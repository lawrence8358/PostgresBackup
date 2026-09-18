using System.Windows.Controls;

namespace PostgresBackup.Wpf.Views;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
    }

    private void TerminalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TerminalBox.ScrollToEnd();
    }
}
