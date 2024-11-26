using CommunityToolkit.Mvvm.Input;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Threading;
using SolutionRunner.ToolWindows.Models;
using SolutionRunner.ToolWindows.Views;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Process = System.Diagnostics.Process;

namespace SolutionRunner.ToolWindows.ViewModels
{
    public class ProjectItemControlViewModel : System.IAsyncDisposable
    {
        public IAsyncRelayCommand StartProjectCommand { get; }
        public IAsyncRelayCommand RestartProjectCommand { get; }
        public IAsyncRelayCommand AttachProjectCommand { get; }
        public IAsyncRelayCommand DetachProjectCommand { get; }
        public IAsyncRelayCommand StopProjectCommand { get; }
        public IAsyncRelayCommand ShowProcessCommand { get; }
        public IAsyncRelayCommand TryActivateProjectCommand { get; }
        private Task pollProcessesTask;
        private CancellationTokenSource processStatusCheckCancellationTokenSource;
        public const int normalCheckProcessStatusDelay = 10000;
        public const int fastCheckProcessStatusDelay = 250;
        private Community.VisualStudio.Toolkit.OutputWindowPane output = null;
        private NamedPipeClientStream pipeClient = null;
        private readonly string[] logWarnKeywords = ["|warn", "| warn", "|warning", "| warning"];
        private readonly string[] logErrorKeywords = ["|error", "| error", "|fatal", "| fatal"];

        private ProjectModel projectItem;
        public ProjectModel ProjectItem
        {
            get => projectItem;
            set
            {
                projectItem = value;
                processStatusCheckCancellationTokenSource = new();
                pollProcessesTask = StartCheckProcessStatusAsync(normalCheckProcessStatusDelay,
                    processStatusCheckCancellationTokenSource.Token);

                ProjectItem.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(projectItem.IsRunning) ||
                        args.PropertyName == nameof(projectItem.IsDebugging) ||
                        args.PropertyName == nameof(projectItem.IsStartingOrStopping))
                    {
                        StartProjectCommand.NotifyCanExecuteChanged();
                        RestartProjectCommand.NotifyCanExecuteChanged();
                        AttachProjectCommand.NotifyCanExecuteChanged();
                        DetachProjectCommand.NotifyCanExecuteChanged();
                        StopProjectCommand.NotifyCanExecuteChanged();
                        ShowProcessCommand.NotifyCanExecuteChanged();
                    }
                };
            }
        }

        public ProjectItemControlViewModel()
        {
            StartProjectCommand = new AsyncRelayCommand(StartProjectAsync, () => CanStartProject);
            RestartProjectCommand = new AsyncRelayCommand(RestartProjectAsync, () => CanRestartProject);
            AttachProjectCommand = new AsyncRelayCommand(AttachProjectAsync, () => CanAttachProject);
            DetachProjectCommand = new AsyncRelayCommand(DetachProjectAsync, () => CanDetachProject);
            StopProjectCommand = new AsyncRelayCommand(StopProjectAsync, () => CanStopProject);
            ShowProcessCommand = new AsyncRelayCommand(ShowProcessAsync, () => CanStopProject);
            TryActivateProjectCommand = new AsyncRelayCommand(TryActivateProjectAsync);
        }

        private async Task RestartProjectAsync(CancellationToken cancellationToke)
        {
            await StopProjectAsync(cancellationToke);
            await StartProjectAsync(cancellationToke);
        }
        public bool CanRestartProject => ProjectItem != null &&
            ProjectItem.IsRunning && !ProjectItem.IsStartingOrStopping;

        public async Task StartProjectAsync(CancellationToken cancellationToken)
        {
            _ = TryConnectToLoggerNamedPipeAsync(cancellationToken);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            //await output.WriteLineAsync($"starting project");
            ProjectItem.IsStartingOrStopping = true;
            ProjectItem.NumberOfErrors = 0;
            ProjectItem.NumberOfWarnings = 0;
            var dte = await VS.GetRequiredServiceAsync<DTE, DTE2>();
            await SolutionRunnerWindowControlViewModel.WaitForBuildStateAsync(dte, cancellationToken);

            bool buildSuccess;

            try
            {
                buildSuccess = await ProjectItem.SolutionProject.BuildAsync(BuildAction.Build);
            }
            catch (ExternalException error)
            {
                ProjectItem.IsStartingOrStopping = false;
                return;
            }

            ReStartCheckProcessStatus(fastCheckProcessStatusDelay);

            if (buildSuccess)
            {
                //await output.WriteLineAsync("build success");
                ProjectItem.HaveBuildError = false;
                await SolutionRunnerWindowControlViewModel.WaitForBuildStateAsync(dte, cancellationToken);
                dte.Solution.SolutionBuild.StartupProjects =
                    new string[] { ProjectItem.SolutionProject.FullPath };

                switch (ProjectItem.ProjectRunType)
                {
                    case RunType.Debug:
                        dte.Solution.SolutionBuild.Debug();
                        ProjectItem.IsDebugging = true;
                        break;
                    default:
                        dte.Solution.SolutionBuild.Run();
                        break;
                }

                // TODO: can we check for idle console so we can do something else?
                _ = Task.Delay(5000, cancellationToken).ContinueWith(async _ =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    ProjectItem.IsStartingOrStopping = false;
                }, TaskScheduler.Default);
            }
            else
            {
                //await output.WriteLineAsync("fail to build");
                ProjectItem.HaveBuildError = true;
                ProjectItem.IsStartingOrStopping = false;
            }

            _ = Task.Delay(5000, cancellationToken)
                .ContinueWith(_ => ReStartCheckProcessStatus(normalCheckProcessStatusDelay), TaskScheduler.Default);
        }
        public bool CanStartProject => ProjectItem != null &&
            !ProjectItem.IsRunning && !ProjectItem.IsStartingOrStopping;

        public async Task AttachProjectAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            ProjectItem.IsStartingOrStopping = true;
            await Task.Delay(100, cancellationToken);

            var projectProcess = Process.GetProcessesByName(ProjectItem.SolutionProject.Name).FirstOrDefault();
            if (projectProcess != null)
            {
                var dte = await VS.GetRequiredServiceAsync<DTE, DTE2>();
                Processes processes = dte.Debugger.LocalProcesses;
                foreach (EnvDTE.Process process in processes)
                {
                    try
                    {
                        if (projectProcess.Id == process.ProcessID)
                        {
                            process.Attach();
                            ProjectItem.IsDebugging = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }

            ProjectItem.IsStartingOrStopping = false;
        }
        public bool CanAttachProject => ProjectItem != null && ProjectItem.IsRunning && !ProjectItem.IsStartingOrStopping && !ProjectItem.IsDebugging;

        public async Task DetachProjectAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            ProjectItem.IsStartingOrStopping = true;
            await Task.Delay(100, cancellationToken);

            var projectProcess = Process.GetProcessesByName(ProjectItem.SolutionProject.Name).FirstOrDefault();
            if (projectProcess != null)
            {
                var dte = await VS.GetRequiredServiceAsync<DTE, DTE2>();
                Processes processes = dte.Debugger.LocalProcesses;
                foreach (EnvDTE.Process process in processes)
                {
                    try
                    {
                        if (projectProcess.Id == process.ProcessID)
                        {
                            _ = Task.Run(async () =>
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                                process.Detach();
                            }, cancellationToken);
                            ProjectItem.IsDebugging = false;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }

            ProjectItem.IsStartingOrStopping = false;
        }

        public bool CanDetachProject => ProjectItem != null && ProjectItem.IsRunning && !ProjectItem.IsStartingOrStopping &&
            ProjectItem.IsDebugging;

        public Task StopProjectAsync(CancellationToken cancellationToken) => StopProjectAsync(false, cancellationToken);
        public async Task StopProjectAsync(bool closeAll, CancellationToken cancellationToken)
        {
            ReStartCheckProcessStatus(fastCheckProcessStatusDelay);

            ProjectItem.IsStartingOrStopping = true;
            await StopProcessByProjectFullPathAsync(ProjectItem.SolutionProject.FullPath, closeAll);

            _ = Task.Delay(5000, cancellationToken)
                .ContinueWith(_ => ReStartCheckProcessStatus(normalCheckProcessStatusDelay), TaskScheduler.Default);
        }
        public bool CanStopProject => ProjectItem != null &&
            ProjectItem.IsRunning && !ProjectItem.IsStartingOrStopping;

        private async Task ShowProcessAsync()
        {
            var projectFullPath = ProjectItem.SolutionProject.FullPath;

            await TaskScheduler.Default;

            try
            {
                foreach (var process in Process.GetProcessesByName("VsDebugConsole"))
                {
                    var title = process.MainWindowTitle.Split(new[] { "bin" }, StringSplitOptions.None)[0];
                    if (projectFullPath.StartsWith(title))
                    {
                        WindowHelper.BringProcessToFront(process);
                        return;
                    }
                }

                var projectName = Path.GetFileNameWithoutExtension(projectFullPath);
                foreach (var process in Process.GetProcessesByName(projectName))
                    WindowHelper.BringProcessToFront(process);
            }
            catch (Exception error)
            {
                await SolutionRunnerWindowControl.Output.WriteLineAsync($"Error: {error.Message} | StackTrace: {error.StackTrace}");
            }
        }

        public async Task TryActivateProjectAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var dte = await VS.GetRequiredServiceAsync<DTE, DTE2>();
                var solutionExplorer = dte.Windows.Item(EnvDTE.Constants.vsWindowKindSolutionExplorer).Object as UIHierarchy;
                var projectNode = await FindProjectNodeAsync(solutionExplorer.UIHierarchyItems,
                    ProjectItem.SolutionProject.Name, cancellationToken);
                projectNode?.Select(vsUISelectionType.vsUISelectionTypeSelect);
            }
            catch (Exception error)
            {
            }
        }

        private async Task<UIHierarchyItem> FindProjectNodeAsync(UIHierarchyItems items, string projectName,
            CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            foreach (UIHierarchyItem item in items)
            {
                if (item.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase) && item.Object is EnvDTE.Project)
                    return item;

                var child = await FindProjectNodeAsync(item.UIHierarchyItems, projectName, cancellationToken);
                if (child != null)
                    return child;
            }
            return null;
        }

        private async Task StartCheckProcessStatusAsync(int checkEveryMillisecondsDelay, CancellationToken cancellationToken)
        {
            try
            {
                //await output.WriteLineAsync("start monitoring project");

                while (!cancellationToken.IsCancellationRequested)
                {
                    await TaskScheduler.Default;

                    var runningProcesses = Process.GetProcessesByName(ProjectItem.SolutionProject.Name)
                       .Where(p => !IsIISExpressProcess(p.Id))
                       .ToList();
                    var isRunning = runningProcesses.Any() ||
                        IsIISExpressHostingProject(ProjectItem.SolutionProject.Name, out _);

                    if (ProjectItem.IsRunning != isRunning)
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        ProjectItem.IsStartingOrStopping = false;
                        ProjectItem.IsRunning = isRunning;
                        if (isRunning)
                        {
                            _ = TryConnectToLoggerNamedPipeAsync(cancellationToken);
                        }
                        else
                        {
                            ProjectItem.IsDebugging = false;
                        }

                        checkEveryMillisecondsDelay = normalCheckProcessStatusDelay;
                    }

                    await Task.Delay(checkEveryMillisecondsDelay, cancellationToken);
                }
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                //await output.WriteLineAsync($"error: {error.Message}, stack trace: {error.StackTrace}");
            }
        }

        private static bool IsIISExpressProcess(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                return process.ProcessName.Equals("iisexpress", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsIISExpressHostingProject(string projectName, out int iisExpressProcessId)
        {
            foreach (var processId in Process.GetProcessesByName("iisexpress").Select(i => i.Id))
            {
                string commandLine = GetCommandLine(processId);
                if (commandLine.Contains($"site:\"{projectName}\""))
                {
                    iisExpressProcessId = processId;
                    return true;
                }
            }
            iisExpressProcessId = 0;
            return false;
        }

        private async Task TryConnectToLoggerNamedPipeAsync(CancellationToken cancellationToken)
        {
            if (pipeClient == null)
            {
                try
                {
                    pipeClient = new NamedPipeClientStream(".", $"SolutionRunner-{ProjectItem.ProjectName}", PipeDirection.In);
                    await TaskScheduler.Default;
                    //await output.WriteLineAsync(
                    //    $"connecting to logger \"SolutionRunner-{ProjectItem.ProjectName}\"... (will close after 60s of initial inactivity)");
                    await pipeClient.ConnectAsync(60000, cancellationToken);
                    //await output.WriteLineAsync($"logger \"SolutionRunner-{ProjectItem.ProjectName}\" connected.");

                    var reader = new StreamReader(pipeClient);
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var lineLower = line.ToLower();
                        var isNewOutput = output == null;
                        output ??= await VS.Windows.CreateOutputWindowPaneAsync($"SolutionRunner - {ProjectItem.ProjectName}", true);

                        if (isNewOutput)
                        {
                            await output.ActivateAsync();
                        }

                        if (Array.Exists(logErrorKeywords, lineLower.Contains))
                        {
                            await output.ActivateAsync();
                            ProjectItem.NumberOfErrors++;
                        }
                        else if (Array.Exists(logWarnKeywords, lineLower.Contains))
                        {
                            ProjectItem.NumberOfWarnings++;
                        }

                        await output.WriteLineAsync(line);
                    }

                    reader.Dispose();
                    pipeClient.Dispose();
                    pipeClient = null;

                }
                catch (Exception error)
                {
                    if (pipeClient != null)
                    {
                        pipeClient.Dispose();
                        pipeClient = null;
                    }

                    if (output != null)
                    {
                        await output.WriteLineAsync($"error: {error.Message}, stack trace: {error.StackTrace}");
                    }
                }

                if (output != null && pipeClient != null && pipeClient.IsConnected)
                {
                    await output.WriteLineAsync($"logger SolutionRunner-{ProjectItem.ProjectName} closed.");
                }
            }
        }

        public static async Task StopProcessByProjectFullPathAsync(string projectFullPath, bool closeAll = false)
        {
            await TaskScheduler.Default;

            try
            {
                // Stop processes by matching the window title with the project path
                foreach (var process in Process.GetProcessesByName("VsDebugConsole"))
                {
                    var title = process.MainWindowTitle.Split(new[] { "bin" }, StringSplitOptions.None)[0];
                    if (projectFullPath.StartsWith(title))
                    {
                        process.Kill();
                    }
                    else if (closeAll)
                    {
                        var parentProcessId = WindowHelper.GetParentProcessId(process.Id);
                        if (parentProcessId == Process.GetCurrentProcess().Id) process.Kill();
                    }
                }

                var projectName = Path.GetFileNameWithoutExtension(projectFullPath);
                foreach (var process in Process.GetProcessesByName(projectName))
                    process.Kill();

                // Stop IIS Express processes that are associated with the project
                foreach (var process in Process.GetProcessesByName("iisexpress"))
                {
                    string commandLine = GetCommandLine(process.Id);
                    if (commandLine.Contains(projectName))
                        process.Kill();
                }
            }
            catch (Exception error)
            {
                await SolutionRunnerWindowControl.Output.WriteLineAsync($"Error: {error.Message} | StackTrace: {error.StackTrace}");
            }
        }

        private static string GetCommandLine(int processId)
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            return searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["CommandLine"]?.ToString() ?? string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsync(true);
            GC.SuppressFinalize(this);
        }

        protected virtual async Task DisposeAsync(bool disposing)
        {
            TryDisposeProcessStatusCheckTaskAndCancellationToken();

            if (pipeClient != null)
            {
                pipeClient.Dispose();
                pipeClient = null;
            }

            if (output != null)
            {
                await output.HideAsync();
                output = null;
            }
        }

        private void TryDisposeProcessStatusCheckTaskAndCancellationToken()
        {
            try
            {
                if (processStatusCheckCancellationTokenSource != null &&
                    !processStatusCheckCancellationTokenSource.IsCancellationRequested)
                {
                    processStatusCheckCancellationTokenSource.Cancel();
                }

                // Ensure the task has completed before attempting to dispose
                if (pollProcessesTask != null && (pollProcessesTask.IsCompleted || pollProcessesTask.IsCanceled ||
                    pollProcessesTask.IsFaulted))
                {
                    pollProcessesTask.Dispose();
                }
                pollProcessesTask = null;

                if (processStatusCheckCancellationTokenSource != null)
                {
                    processStatusCheckCancellationTokenSource.Dispose();
                    processStatusCheckCancellationTokenSource = null;
                }
            }
            catch (Exception error)
            {


            }
        }

        public void ReStartCheckProcessStatus(int delayMS)
        {
            TryDisposeProcessStatusCheckTaskAndCancellationToken();
            processStatusCheckCancellationTokenSource = new();
            pollProcessesTask = StartCheckProcessStatusAsync(delayMS, processStatusCheckCancellationTokenSource.Token);
        }
    }
}
