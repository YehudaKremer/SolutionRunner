using Microsoft.VisualStudio.Imaging;
using SolutionRunner.ToolWindows.Views;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SolutionRunner
{
    public class SolutionRunnerWindow : BaseToolWindow<SolutionRunnerWindow>
    {
        public override string GetTitle(int toolWindowId) => "Solution Runner";

        public override Type PaneType => typeof(Pane);

        public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
        {
            return Task.FromResult<FrameworkElement>(new SolutionRunnerWindowControl());
        }

        [Guid("01c0a0fa-2ec0-4cb2-9b9c-8d8b9d0454f9")]
        internal class Pane : ToolkitToolWindowPane
        {
            public Pane()
            {
                BitmapImageMoniker = KnownMonikers.ToolWindow;
            }
        }
    }
}