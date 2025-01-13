using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Input;

namespace SimDas.Models.Common
{
    public class ResizeBehavior : Behavior<FrameworkElement>
    {
        public static readonly DependencyProperty StartResizeCommandProperty =
            DependencyProperty.Register(
                nameof(StartResizeCommand),
                typeof(ICommand),
                typeof(ResizeBehavior),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ResizeCommandProperty =
            DependencyProperty.Register(
                nameof(ResizeCommand),
                typeof(ICommand),
                typeof(ResizeBehavior),
                new PropertyMetadata(null));

        public static readonly DependencyProperty EndResizeCommandProperty =
            DependencyProperty.Register(
                nameof(EndResizeCommand),
                typeof(ICommand),
                typeof(ResizeBehavior),
                new PropertyMetadata(null));

        public ICommand StartResizeCommand
        {
            get => (ICommand)GetValue(StartResizeCommandProperty);
            set => SetValue(StartResizeCommandProperty, value);
        }

        public ICommand ResizeCommand
        {
            get => (ICommand)GetValue(ResizeCommandProperty);
            set => SetValue(ResizeCommandProperty, value);
        }

        public ICommand EndResizeCommand
        {
            get => (ICommand)GetValue(EndResizeCommandProperty);
            set => SetValue(EndResizeCommandProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.MouseLeftButtonDown += OnMouseLeftButtonDown;
            AssociatedObject.MouseMove += OnMouseMove;
            AssociatedObject.MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            AssociatedObject.MouseMove -= OnMouseMove;
            AssociatedObject.MouseLeftButtonUp -= OnMouseLeftButtonUp;
            base.OnDetaching();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResizeCommand?.Execute(e.GetPosition(Application.Current.MainWindow));
            AssociatedObject.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (AssociatedObject.IsMouseCaptured)
            {
                ResizeCommand?.Execute(e.GetPosition(Application.Current.MainWindow));
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (AssociatedObject.IsMouseCaptured)
            {
                AssociatedObject.ReleaseMouseCapture();
                EndResizeCommand?.Execute(null);
            }
        }
    }
}
