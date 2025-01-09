using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimDas.Models.Common
{
    public class RichTextBoxBindingBehavior : Behavior<RichTextBox>
    {
        private bool _isUpdatingText;

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(RichTextBoxBindingBehavior),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, TextPropertyChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.TextChanged += OnTextChanged;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.TextChanged -= OnTextChanged;
            base.OnDetaching();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingText) return;

            var richTextBox = AssociatedObject;
            richTextBox.BeginChange();
            try
            {
                var text = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd).Text;
                if (Text != text)
                {
                    Text = text;
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateHighlighting(richTextBox);
                    }), DispatcherPriority.Background);
                }
            }
            finally
            {
                richTextBox.EndChange();
            }
        }

        private static void TextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var behavior = d as RichTextBoxBindingBehavior;
            if (behavior?.AssociatedObject == null) return;

            if (!behavior._isUpdatingText)
            {
                behavior._isUpdatingText = true;
                try
                {
                    var text = e.NewValue as string ?? string.Empty;
                    behavior.UpdateDocument(text);
                }
                finally
                {
                    behavior._isUpdatingText = false;
                }
            }
        }

        private void UpdateDocument(string text)
        {
            var richTextBox = AssociatedObject;
            richTextBox.BeginChange();
            try
            {
                // 새 Document가 아닌 현재 Document 사용
                var document = richTextBox.Document;

                // Document 내용 초기화
                document.Blocks.Clear();

                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    var paragraph = new Paragraph(new Run(line));
                    if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("#"))
                    {
                        paragraph.Foreground = new SolidColorBrush(Colors.Gray);
                    }
                    document.Blocks.Add(paragraph);
                }
            }
            finally
            {
                richTextBox.EndChange();
            }
        }

        private void UpdateHighlighting(RichTextBox richTextBox)
        {
            if (richTextBox?.Document == null) return;

            richTextBox.BeginChange();
            try
            {
                foreach (var block in richTextBox.Document.Blocks.ToList())  // ToList로 복사본 사용
                {
                    if (block is Paragraph paragraph)
                    {
                        var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimStart();
                        if (text.StartsWith("//") || text.StartsWith("#"))
                        {
                            paragraph.Foreground = new SolidColorBrush(Colors.Gray);
                        }
                        else
                        {
                            paragraph.Foreground = new SolidColorBrush(Colors.Black);
                        }
                    }
                }
            }
            finally
            {
                richTextBox.EndChange();
            }
        }
    }
}
