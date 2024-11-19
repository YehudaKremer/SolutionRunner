using CommunityToolkit.Mvvm.Input;
using EnvDTE;
using EnvDTE80;
using SolutionRunner.ToolWindows.Models;
using SolutionRunner.ToolWindows.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Project = Community.VisualStudio.Toolkit.Project;

namespace SolutionRunner.ToolWindows.ViewModels
{
    public class SolutionRunnerWindowControlViewModel
    {
        public IAsyncRelayCommand StartAllSelectedProjectsCommand { get; }
        public IAsyncRelayCommand StopAllProjectsCommand { get; }
        public IRelayCommand ShowAllProcessesCommand { get; }
        public IRelayCommand MinimizeAllProcessesCommand { get; }
        public IRelayCommand ToggleAllCheckBoxesCommand { get; }
        public ObservableCollection<ProjectItemControlViewModel> Projects { get; set; } = [];
        public SolutionRunnerWindowControl CurrentPage { get; set; }

        public SolutionRunnerWindowControlViewModel()
        {
            StartAllSelectedProjectsCommand = new AsyncRelayCommand(StartAllSelectedProjectsAsync, () => CanStartSelectedProjects);
            StopAllProjectsCommand = new AsyncRelayCommand(StopAllProjectsAsync, () => CanStopAllProjects);
            ShowAllProcessesCommand = new RelayCommand(ShowAllProcesses, () => CanStopAllProjects);
            MinimizeAllProcessesCommand = new RelayCommand(MinimizeAllProcesses, () => CanStopAllProjects);
            ToggleAllCheckBoxesCommand = new RelayCommand(ToggleAllCheckBoxes, () => CanToggleAllCheckBoxes);

            Projects.CollectionChanged += (_, _) =>
            {
                StartAllSelectedProjectsCommand.NotifyCanExecuteChanged();
                StopAllProjectsCommand.NotifyCanExecuteChanged();
                ShowAllProcessesCommand.NotifyCanExecuteChanged();
                MinimizeAllProcessesCommand.NotifyCanExecuteChanged();
                ToggleAllCheckBoxesCommand.NotifyCanExecuteChanged();
            };
        }

        private async Task StartAllSelectedProjectsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var projectsToStart = Projects
                .Where(p => p.ProjectItem.ProjectRunType != RunType.None &&
                    !p.ProjectItem.IsRunning && !p.ProjectItem.IsStartingOrStopping)
                .ToList();

            foreach (var project in projectsToStart.Select(i => i.ProjectItem))
            {
                project.IsStartingOrStopping = true;
                project.HaveBuildError = false;
            }

            foreach (var project in projectsToStart.Where(i => i.ProjectItem.ProjectRunType == RunType.Run))
            {
                await project.StartProjectAsync();
            }

            var projectsModelsToDebug = projectsToStart.Where(i => i.ProjectItem.ProjectRunType == RunType.Debug);
            var projectsToDebug = projectsModelsToDebug.Select(i => i.ProjectItem).ToList();

            if (projectsToDebug.Count > 0)
            {
                var dte = await VS.GetRequiredServiceAsync<DTE, DTE2>();

                foreach (var project in projectsToDebug)
                {
                    await WaitForBuildStateAsync(dte);
                    var buildSuccess = await project.SolutionProject.BuildAsync(BuildAction.Build);
                    if (!buildSuccess)
                    {
                        project.HaveBuildError = true;

                        foreach (var projectBuild in projectsToDebug)
                        {
                            projectBuild.IsStartingOrStopping = false;
                        }

                        return;
                    }
                }

                await WaitForBuildStateAsync(dte);

                dte.Solution.SolutionBuild.StartupProjects = projectsToDebug
                    .Select(i => i.SolutionProject.FullPath).ToArray();

                dte.Solution.SolutionBuild.Debug();

                foreach (var projectModel in projectsModelsToDebug)
                {
                    projectModel.ProjectItem.IsDebugging = true;
                    projectModel.ReStartCheckProcessStatus(ProjectItemControlViewModel.fastCheckProcessStatusDelay);
                }

                // TODO: can we check for idle console so we can do something else?
                _ = Task.Delay(5000).ContinueWith(async _ =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    foreach (var project in projectsToDebug)
                    {
                        project.IsStartingOrStopping = false;
                    }
                }, TaskScheduler.Default);
            }
        }
        public bool CanStartSelectedProjects => Projects
            .Any(p => p.ProjectItem.ProjectRunType != RunType.None &&
                !p.ProjectItem.IsRunning && !p.ProjectItem.IsStartingOrStopping);

        private async Task StopAllProjectsAsync()
        {
            foreach (var project in Projects.Where(i =>
                i.ProjectItem.IsRunning && !i.ProjectItem.IsStartingOrStopping))
            {
                await project.StopProjectAsync(true);
            }
        }
        public bool CanStopAllProjects => Projects
            .Any(p => p.ProjectItem.IsRunning && !p.ProjectItem.IsStartingOrStopping);

        private static void ShowAllProcesses() => WindowHelper.BringAllProcessesToFrontAndArrangeSideBySide();
        private static void MinimizeAllProcesses() => WindowHelper.MinimizeAllProcessesWindows();

        private void ToggleAllCheckBoxes()
        {
            var allUnmark = !Projects.Any(i => i.ProjectItem.ProjectRunType != RunType.None);
            var haveDebug = Projects.Any(i => i.ProjectItem.ProjectRunType == RunType.Debug);

            foreach (var project in Projects.Select(i => i.ProjectItem))
            {
                if (allUnmark)
                    project.IsChecked = true;
                else if (haveDebug)
                    project.IsChecked = false;
                else
                    project.IsChecked = null;
            }
        }
        public bool CanToggleAllCheckBoxes => Projects.Any();

        public static async Task<IEnumerable<Project>> LoadProjectsAsync()
        {
            var allProjects = await VS.Solutions.GetAllProjectsAsync();

            var executableProjectFiles = new string[]
            {
                "program.cs",
                "program.vb",
                "global.asax",
                "web.config",
                "app_start/routeconfig.cs",
                "form1.cs",
                "form1.vb",
                "app.xaml",
                "mainwindow.xaml",
                "service1.svc",
                "mainactivity.cs",
                "appdelegate.cs",
                "mainpage.xaml",
                "source.extension.vsixmanifest"
            };

            var programProjects = new List<Project>();

            foreach (var project in allProjects)
            {
                var projectFile = project.FullPath;
                if (projectFile == null) continue;

                var outputType = await project.GetAttributeAsync("OutputType");

                if (outputType.ToLower() == "exe" || project.Children.ToList().Exists(file =>
                    executableProjectFiles.ToList().Exists(exeFileName => file.Text.ToLower().EndsWith(exeFileName))))
                {
                    programProjects.Add(project);
                }
            }

            //    userControl.ProjectsHeadline.Content = $"Project ({programProjects.Count})";
            return programProjects;
        }

        public static async Task<IEnumerable<string>> GetSolutionStartupProjectsAsync()
        {
            var dte = await VS.GetRequiredServiceAsync<DTE, DTE2>();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var solutionStartupProjects = (object[])dte.Solution.SolutionBuild.StartupProjects;
                return solutionStartupProjects != null ? solutionStartupProjects.Select(i => i.ToString()) : [];
            }
            catch (Exception error)
            {
                await SolutionRunnerWindowControl.Output.WriteLineAsync($"Error: {error.Message} | StackTrace: {error.StackTrace}");
                return [];
            }
        }

        public static async Task WaitForBuildStateAsync(DTE2 dte)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            while (dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
            {
                await Task.Delay(100);
            }
        }
    }
}
