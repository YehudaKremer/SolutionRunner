using Microsoft.VisualStudio.PlatformUI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SolutionRunner.ToolWindows.Views
{
    public partial class DotCircleSpinnerControl : UserControl
    {
        public DotCircleSpinnerControl()
        {
            InitializeComponent();
            Loaded += DotCircleSpinner_Loaded;
            Unloaded += DotCircleSpinner_Unloaded;
        }

        private void DotCircleSpinner_Loaded(object sender, RoutedEventArgs e)
        {
            var highlightBrush = Application.Current.TryFindResource(EnvironmentColors.SystemHighlightBrushKey) as SolidColorBrush;
            foreach (var ellipse in new[] { ellipse01, ellipse02, ellipse03, ellipse04, ellipse05, ellipse06, ellipse07, ellipse08, ellipse09, ellipse10, ellipse11, ellipse12 })
            {
                ellipse.Fill = highlightBrush;
            }
            VisualStateManager.GoToState(this, "Active", true);
        }

        private void DotCircleSpinner_Unloaded(object sender, RoutedEventArgs e)
        {
            VisualStateManager.GoToState(this, "Inactive", true);
        }
    }
}
