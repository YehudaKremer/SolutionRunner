namespace SolutionRunner
{
    [Command(PackageIds.ShowSolutionRunnerWindowCommand)]
    internal sealed class SolutionRunnerWindowCommand : BaseCommand<SolutionRunnerWindowCommand>
    {
        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            return SolutionRunnerWindow.ShowAsync();
        }
    }
}
