using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Threading;
using SolutionRunner.ToolWindows.Models;
using SolutionRunner.ToolWindows.Services;
using SolutionRunner.ToolWindows.ViewModels;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace SolutionRunner.ToolWindows.Views
{
    public partial class SolutionRunnerWindowControl : UserControl
    {
        private bool isSolutionOpened;
        private bool extensionVesionChecked;
        public static StartupProjectsManager StartupProjects { get; private set; }
        public readonly CancellationToken cancellationToken;

        public static OutputWindowPane Output { get; private set; }


        public SolutionRunnerWindowControl(ToolkitPackage toolkitPackage, CancellationToken cancellationToken)
        {
            StartupProjects = new StartupProjectsManager(toolkitPackage);

            InitializeComponent();
            ((SolutionRunnerWindowControlViewModel)DataContext).CurrentPage = this;
            _ = InitializeOutputWindowPaneAsync();
            _ = RegisterEventsWhenSolutionOpenAsync();
            this.cancellationToken = cancellationToken;
        }

        private static async Task InitializeOutputWindowPaneAsync()
        {
            Output = await VS.Windows.CreateOutputWindowPaneAsync("SolutionRunner", true);
        }

        private async Task RegisterEventsWhenSolutionOpenAsync()
        {
            var isOpen = await VS.Solutions.IsOpenAsync();
            if (isOpen)
            {
                isSolutionOpened = true;
                InitializeAction();
            }

            RegisterSolutionEvents();
        }

        private void RegisterSolutionEvents()
        {
            var solutionEvents = VS.Events.SolutionEvents;
            solutionEvents.OnAfterOpenSolution += (Solution solution) =>
            {
                isSolutionOpened = true;
                InitializeAction(solution);
            };
            solutionEvents.OnBeforeCloseSolution += () =>
            {
                isSolutionOpened = false;
            };
            solutionEvents.OnAfterCloseSolution += InitializeAction;
            solutionEvents.OnAfterOpenProject += (Project project) =>
            {
                if (isSolutionOpened) InitializeAction(project);
            };
            solutionEvents.OnBeforeCloseProject += project => _ = Task.Run(async () =>
            {
                if (isSolutionOpened)
                {
                    await Task.Delay(1000, cancellationToken);
                    InitializeAction(project);
                }
            }, cancellationToken);
            solutionEvents.OnAfterRenameProject += InitializeAction;
        }

#pragma warning disable S1172 // Unused method parameters should be removed
        private void InitializeAction(Solution _) => InitializeAction();
#pragma warning restore S1172 // Unused method parameters should be removed
        private void InitializeAction(Project _) => InitializeAction();
        private void InitializeAction() => _ = InitializeAsync();

        private async Task InitializeAsync()
        {
            try
            {
                //await ProcessesMonitorInstance.ResetAsync();
                //foreach (var output in ProjectsManagerInstance.ProjectsRunDetails.Select(i => i.Value.Output))
                //{
                //    if (output != null) await output.HideAsync();
                //}
                //ProjectsManagerInstance.ProjectsRunDetails.Clear();

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var solutionProjects = await SolutionRunnerWindowControlViewModel.LoadProjectsAsync();
                var solutionRunnerViewModel = (SolutionRunnerWindowControlViewModel)DataContext;
                var projects = solutionRunnerViewModel.Projects;
                var startupProjects = await StartupProjects.LoadStartupProjectsAsync(cancellationToken);
                var projectsToRemove = projects
                    .Where(project => solutionProjects
                    .FirstOrDefault(i => i.FullPath == project.ProjectItem.SolutionProject.FullPath) == null)
                    .ToList();

                foreach (var project in projectsToRemove)
                {
                    await project.DisposeAsync();
                    projects.Remove(project);
                }

                if (solutionProjects.Any())
                {
                    foreach (var project in solutionProjects.OrderBy(i => i.Name))
                    {
                        var existedProject = projects
                            .FirstOrDefault(i => i.ProjectItem.SolutionProject.FullPath == project.FullPath);
                        var projectNameWithoutExtension = project.Name.Contains(Path.DirectorySeparatorChar) ?
                            Path.GetFileNameWithoutExtension(project.FullPath) : project.Name;
                        var projectDefaultRunType = startupProjects
                            .Find(i => i.ProjectFullPath == project.FullPath)?.RunType ?? RunType.None;

                        if (existedProject != null)
                        {
                            existedProject.ProjectItem.ProjectName = projectNameWithoutExtension;
                            existedProject.ProjectItem.ProjectRunType = projectDefaultRunType;
                            existedProject.ProjectItem.SolutionProject = project;
                        }
                        else
                        {
                            var projectItem = new ProjectModel
                            {
                                ProjectName = projectNameWithoutExtension,
                                SolutionProject = project,
                                ProjectRunType = projectDefaultRunType
                            };
                            var newProjectItemControlViewModel = new ProjectItemControlViewModel { ProjectItem = projectItem };

                            newProjectItemControlViewModel.ProjectItem.PropertyChanged += (projectModel, args) =>
                            {
                                if (args.PropertyName == nameof(projectItem.IsRunning) ||
                                    args.PropertyName == nameof(projectItem.IsStartingOrStopping) ||
                                    args.PropertyName == nameof(projectItem.ProjectRunType) ||
                                    args.PropertyName == nameof(projectItem.SolutionProject))
                                {
                                    solutionRunnerViewModel.StartAllSelectedProjectsCommand.NotifyCanExecuteChanged();
                                    solutionRunnerViewModel.StopAllProjectsCommand.NotifyCanExecuteChanged();
                                    solutionRunnerViewModel.ShowAllProcessesCommand.NotifyCanExecuteChanged();
                                    solutionRunnerViewModel.MinimizeAllProcessesCommand.NotifyCanExecuteChanged();
                                }

                                if (args.PropertyName == nameof(projectItem.ProjectRunType))
                                {
                                    _ = StartupProjects.UpdateStartupProjectAsync((projectModel as ProjectModel).ToStartupProject(),
                                        cancellationToken);
                                }
                            };

                            //// Find the correct index to insert the new project in order by ProjectName
                            //int insertIndex = projects.TakeWhile(p =>
                            //    string.Compare(p.SolutionProject.Name, project.Name, StringComparison.Ordinal) < 0)
                            //    .Count();

                            //projects.Insert(insertIndex, newProject);

                            projects.Add(newProjectItemControlViewModel);
                        }

                        //await ProcessesMonitorInstance.CheckProcessStatusWithPollingAsync(project, projectRow);
                    }
                }

                solutionRunnerViewModel.StartAllSelectedProjectsCommand.NotifyCanExecuteChanged();
                solutionRunnerViewModel.StopAllProjectsCommand.NotifyCanExecuteChanged();

                //var projectsViewModels = ((SolutionRunnerWindowControlViewModel)DataContext).ProjectsViewModels;

                //projectsViewModels.Clear();

                //foreach (var project in projects)
                //{
                //    projectsViewModels.Add(new ProjectItemControlViewModel { ProjectItem = project });
                //}

                if (!extensionVesionChecked)
                {
                    _ = CheckForNewVersionAsync();
                }
            }
            catch (Exception error)
            {
                await Output.WriteLineAsync($"Error: {error.Message} | StackTrace: {error.StackTrace}");
                throw;
            }
        }

        private async Task CheckForNewVersionAsync()
        {
            await TaskScheduler.Default;
            extensionVesionChecked = true;

            try
            {
                var (currentVersion, latestVersion) = await VersionChecker.GetCurrentAndLatestVersionAsync(cancellationToken);
                var haveNewVersion = !currentVersion.Equals(latestVersion, StringComparison.OrdinalIgnoreCase);
                if (haveNewVersion)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    await Output.WriteLineAsync($"A new version of the extension is available, current version: {currentVersion}, new version: {latestVersion}, Please update to the latest version.");

                    var model = new InfoBarModel([
                            new InfoBarTextSpan($"New version ({latestVersion}) is available. "),
                            new InfoBarButton("Update")], KnownMonikers.Extension, true);

                    InfoBar infoBar = await VS.InfoBar.CreateAsync("01c0a0fa-2ec0-4cb2-9b9c-8d8b9d0454f9", model);
                    infoBar.ActionItemClicked += (object sender, InfoBarActionItemEventArgs e) =>
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://marketplace.visualstudio.com/items?itemName=YehudaK.SolutionRunner",
                            UseShellExecute = true
                        });
                    };
                    await infoBar.TryShowInfoBarUIAsync();
                }
            }
            catch (Exception error)
            {
                await Output.WriteLineAsync($"Error while checking for new version: {error.Message} | StackTrace: {error.StackTrace}");
            }
        }
    }
}
