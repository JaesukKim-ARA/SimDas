using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace SimDas.Models.Common
{
    public class DraggablePopupBehavior : Behavior<FrameworkElement>
    {
        private Point _dragStart;
        private bool _isDragging;

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(
                nameof(Position),
                typeof(PopupPosition),
                typeof(DraggablePopupBehavior),
                new PropertyMetadata(new PopupPosition(), OnPositionChanged));

        public PopupPosition Position
        {
            get => (PopupPosition)GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        public static readonly DependencyProperty ParentPopupProperty =
            DependencyProperty.Register(
        nameof(ParentPopup),
        typeof(Popup),
        typeof(DraggablePopupBehavior),
        new PropertyMetadata(null));

        public Popup ParentPopup
        {
            get => (Popup)GetValue(ParentPopupProperty);
            set => SetValue(ParentPopupProperty, value);
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DraggablePopupBehavior behavior && behavior.ParentPopup != null)
            {
                behavior.ParentPopup.HorizontalOffset = behavior.Position.X;
                behavior.ParentPopup.VerticalOffset = behavior.Position.Y;
            }
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            AssociatedObject.MouseLeftButtonDown += OnMouseLeftButtonDown;
            AssociatedObject.MouseMove += OnMouseMove;
            AssociatedObject.MouseLeftButtonUp += OnMouseLeftButtonUp;

            // ParentPopup을 직접 설정하거나, FindParentPopup으로 대체
            if (ParentPopup == null)
            {
                ParentPopup = FindParentPopup(AssociatedObject);
            }
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
            if (ParentPopup == null) return;

            _isDragging = true;
            _dragStart = e.GetPosition(Application.Current.MainWindow); // 부모 윈도우 기준으로 좌표 계산
            AssociatedObject.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || ParentPopup == null) return;

            var currentPos = e.GetPosition(Application.Current.MainWindow);
            var offset = currentPos - _dragStart;

            Position.X += offset.X;
            Position.Y += offset.Y;

            ParentPopup.HorizontalOffset = Position.X;
            ParentPopup.VerticalOffset = Position.Y;

            _dragStart = currentPos;
        }

        private void OnMouseLeftButtonUp(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                AssociatedObject.ReleaseMouseCapture();
            }
        }

        private Popup FindParentPopup(FrameworkElement element)
        {
            DependencyObject parent = element;
            while (parent != null && !(parent is Popup))
            {
                parent = LogicalTreeHelper.GetParent(parent);
            }
            return parent as Popup;
        }
    }
}
