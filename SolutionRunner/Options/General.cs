using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SolutionRunner
{
    internal partial class OptionsProvider
    {
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<General> { }
    }

    public class General : BaseOptionModel<General>
    {
        [Category("Processes")]
        [DisplayName("Hide Started Processes")]
        [Description("When starting multiple processes hide (minimize) them from the desktop.")]
        [DefaultValue(false)]
        public bool HideStartedProcesses { get; set; } = false;

        [Category("Processes")]
        [DisplayName("Hide When Number Of Processes Start")]
        [Description("Hide processes when a certain number of processes start.")]
        [DefaultValue(3)]
        public int HideWhenNumberOfProcessesStart { get; set; } = 3;
    }
}
