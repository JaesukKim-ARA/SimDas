using SimDas.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SimDas.Views
{
    /// <summary>
    /// Interaction logic for ResultView.xaml
    /// </summary>
    public partial class ResultView : UserControl
    {
        public ResultView()
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

        private void TextBox_NumbericInput(object sender, TextCompositionEventArgs e)
        {
            // 숫자만 허용
            e.Handled = !IsTextNumeric(e.Text);
        }

        private static bool IsTextNumeric(string text)
        {
            // 허용 가능한 문자: 숫자, `-`, `+`, `.`
            foreach (char c in text)
            {
                if (!char.IsDigit(c) && c != '-' && c != '+' && c != '.')
                {
                    return false; // 숫자가 아니면 입력 불가
                }
            }

            // 이미 입력된 텍스트와 결합하여 전체 텍스트를 확인

            // `-` 또는 `+`는 첫 번째 문자로만 허용
            if ((text.Contains('-') && text.IndexOf('-') > 0) ||
                (text.Contains('+') && text.IndexOf('+') > 0))
            {
                return false;
            }

            // `.`은 한 번만 허용
            if (text.Split('.').Length - 1 > 1)
            {
                return false;
            }

            return true;
        }
    }
}
