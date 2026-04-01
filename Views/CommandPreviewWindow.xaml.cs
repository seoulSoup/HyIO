using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HyIO.Views
{
    public partial class CommandPreviewWindow : Window
    {
        public event Action<ImageOverlayView.PreviewImageMatch> CandidateChosen;

        public CommandPreviewWindow()
        {
            InitializeComponent();
        }

        public void ShowPreview(string commandText, IReadOnlyList<ImageOverlayView.PreviewImageMatch> candidates, Point anchorScreenPoint)
        {
            CommandTextBlock.Text = "/" + commandText;
            CandidatesList.ItemsSource = candidates;
            CandidatesList.SelectedIndex = candidates.Count > 0 ? 0 : -1;
            CandidatesList.Visibility = candidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = candidates.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            if (!IsVisible)
            {
                Show();
            }

            UpdateLayout();
            PositionNearAnchor(anchorScreenPoint);
            Activate();
            Focus();
            CandidatesList.Focus();
        }

        public void HidePreview()
        {
            Hide();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HidePreview();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                ConfirmSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            HidePreview();
        }

        private void CandidatesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void CandidatesList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) != null)
            {
                ConfirmSelection();
            }
        }

        private void MoveSelection(int delta)
        {
            if (CandidatesList.Items.Count == 0)
                return;

            int currentIndex = CandidatesList.SelectedIndex;
            if (currentIndex < 0)
                currentIndex = 0;

            int nextIndex = Math.Max(0, Math.Min(CandidatesList.Items.Count - 1, currentIndex + delta));
            CandidatesList.SelectedIndex = nextIndex;
            CandidatesList.ScrollIntoView(CandidatesList.SelectedItem);
        }

        private void ConfirmSelection()
        {
            if (CandidatesList.SelectedItem is not ImageOverlayView.PreviewImageMatch match)
                return;

            HidePreview();
            CandidateChosen?.Invoke(match);
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void PositionNearAnchor(Point anchorScreenPoint)
        {
            var targetScreen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)Math.Round(anchorScreenPoint.X),
                (int)Math.Round(anchorScreenPoint.Y)));

            var workingAreaTopLeft = DevicePixelsToDip(new Point(
                targetScreen.WorkingArea.Left,
                targetScreen.WorkingArea.Top));
            var workingAreaBottomRight = DevicePixelsToDip(new Point(
                targetScreen.WorkingArea.Right,
                targetScreen.WorkingArea.Bottom));
            var anchorDip = DevicePixelsToDip(anchorScreenPoint);

            const double margin = 8;
            const double anchorGap = 18;

            double preferredLeft = anchorDip.X - (ActualWidth / 2);
            double minLeft = workingAreaTopLeft.X + margin;
            double maxLeft = Math.Max(minLeft, workingAreaBottomRight.X - ActualWidth - margin);
            Left = Math.Min(Math.Max(preferredLeft, minLeft), maxLeft);

            double preferredTop = anchorDip.Y - ActualHeight - anchorGap;
            double minTop = workingAreaTopLeft.Y + margin;
            double maxTop = Math.Max(minTop, workingAreaBottomRight.Y - ActualHeight - margin);

            if (preferredTop < minTop)
            {
                preferredTop = anchorDip.Y + anchorGap;
            }

            Top = Math.Min(Math.Max(preferredTop, minTop), maxTop);
        }

        private Point DevicePixelsToDip(Point point)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null)
                return point;

            return source.CompositionTarget.TransformFromDevice.Transform(point);
        }
    }
}
