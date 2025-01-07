using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Newtonsoft.Json;
using SolutionRunner.ToolWindows.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SolutionRunner.ToolWindows.Services
{
    public class StartupProjectsManager
    {
        private readonly WritableSettingsStore settingsStore;
        private const string CollectionPath = "SolutionRunner";
        private const string ListPropertyName = "StartupProjects";

        public StartupProjectsManager(IServiceProvider serviceProvider)
        {
            var shellSettingsManager = new ShellSettingsManager(serviceProvider);
            settingsStore = shellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (!settingsStore.CollectionExists(CollectionPath))
            {
                settingsStore.CreateCollection(CollectionPath);
            }
        }

        public async Task UpdateStartupProjectAsync(StartupProject startupProject, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var solutionName = GetSolutionFullPath();

            List<StartupProject> projects = await LoadStartupProjectsAsync(cancellationToken);

            projects.RemoveAll(s => string.IsNullOrWhiteSpace(s.SolutionName) ||
                (s.SolutionName == solutionName && s.ProjectFullPath == startupProject.ProjectFullPath));

            if (startupProject.RunType != RunType.None)
            {
                startupProject.SolutionName = solutionName;
                projects.Add(startupProject);
            }

            string json = JsonConvert.SerializeObject(projects);
            settingsStore.SetString(CollectionPath, ListPropertyName, json);
        }

        public async Task<List<StartupProject>> LoadStartupProjectsAsync(CancellationToken cancellationToken)
        {
            List<StartupProject> solutionStartupProjects = [];
            var solutionName = GetSolutionFullPath();

            if (!string.IsNullOrEmpty(solutionName))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                if (settingsStore.PropertyExists(CollectionPath, ListPropertyName))
                {
                    string json = settingsStore.GetString(CollectionPath, ListPropertyName);
                    solutionStartupProjects = JsonConvert.DeserializeObject<List<StartupProject>>(json).ToList();
                }
            }

            return solutionStartupProjects;
        }

        private static string GetSolutionFullPath() =>
            Directory.GetFiles(Environment.CurrentDirectory, "*.sln").FirstOrDefault();
    }
}
