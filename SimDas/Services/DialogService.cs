using Microsoft.Win32;
using System.Windows;

namespace SimDas.Services
{
    public interface IDialogService
    {
        void ShowInformation(string message, string title = "Information");
        void ShowWarning(string message, string title = "Warning");
        void ShowError(string message, string title = "Error");
        bool ShowConfirmation(string message, string title = "Confirmation");
        string ShowSaveFileDialog(string defaultExtension = ".txt", string filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*");
    }

    public class DialogService : IDialogService
    {
        public void ShowInformation(string message, string title = "Information")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string title = "Warning")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string title = "Error")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool ShowConfirmation(string message, string title = "Confirmation")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;
        }

        public string ShowSaveFileDialog(string defaultExtension = ".txt", string filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*")
        {
            var dialog = new SaveFileDialog
            {
                DefaultExt = defaultExtension,
                Filter = filter,
                AddExtension = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}