using System.Windows.Controls;

namespace PostgresBackup.Wpf.Views;

public partial class RestoreView : UserControl
{
    public RestoreView()
    {
        InitializeComponent();
    }

    private void TerminalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TerminalBox.ScrollToEnd();
    }
}
