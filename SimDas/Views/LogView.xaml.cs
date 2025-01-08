using System.Linq;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SimDas.Services;

namespace SimDas.Views
{
    /// <summary>
    /// Interaction logic for LogView.xaml
    /// </summary>
    public partial class LogView : UserControl
    {
        public LogView()
        {
            InitializeComponent();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll(); // TextBox 안의 모든 텍스트 선택
            }
        }

        private bool _autoScroll = true;

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var listBox = sender as ListBox;

            if (VisualTreeHelper.GetChildrenCount(listBox) > 0)
            {
                Border border = (Border)VisualTreeHelper.GetChild(listBox, 0);
                ScrollViewer scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);

                // 사용자가 스크롤을 위로 올릴 경우 AutoScroll 비활성화
                if (e.ExtentHeightChange == 0)
                {
                    _autoScroll = scrollViewer.VerticalOffset == scrollViewer.ScrollableHeight;
                }

                // 새 로그가 추가되었을 때 AutoScroll 활성화
                if (_autoScroll && e.ExtentHeightChange != 0)
                {
                    scrollViewer.ScrollToEnd();
                }
            }
        }

        private void LogListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var listBox = sender as ListBox;
                if (listBox != null && listBox.SelectedItems.Count > 0)
                {
                    var selectedLogs = listBox.SelectedItems
                        .Cast<LogEntry>()
                        .Select(log => log.ToString());

                    Clipboard.SetText(string.Join(Environment.NewLine, selectedLogs));
                }
            }
        }
    }
}
