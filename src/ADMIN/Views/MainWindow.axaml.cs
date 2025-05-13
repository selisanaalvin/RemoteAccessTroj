using ADMIN.ViewModels;
using Avalonia.Controls;

namespace ADMIN.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SendMessage(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Get the message text from the TextBox
        var messageTextBox = this.FindControl<TextBox>("MessageTextBox");
        var message = messageTextBox.Text;

        var targetIp= this.FindControl<TextBox>("IPTarget");
        var ipTar= targetIp.Text;
        if (!string.IsNullOrWhiteSpace(message))
        {
            var viewModel = (MainWindowViewModel)DataContext;
            viewModel.SendMessageAsync(ipTar, message);
        }


    }
}