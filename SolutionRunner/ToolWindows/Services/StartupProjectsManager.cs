using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Newtonsoft.Json;
using SolutionRunner.ToolWindows.Models;
using System.Collections.Generic;
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

        public async Task SaveStartupProjectsAsync(IEnumerable<StartupProject> list, CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(list);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            settingsStore.SetString(CollectionPath, ListPropertyName, json);
        }

        public async Task UpdateStartupProjectAsync(StartupProject startupProject, CancellationToken cancellationToken)
        {
            List<StartupProject> list = await LoadStartupProjectsAsync(cancellationToken);
            list.RemoveAll(s => s.ProjectFullPath == startupProject.ProjectFullPath);
            if (startupProject.RunType != RunType.None)
            {
                list.Add(startupProject);
            }
            await SaveStartupProjectsAsync(list, cancellationToken);
        }

        public async Task<List<StartupProject>> LoadStartupProjectsAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (settingsStore.PropertyExists(CollectionPath, ListPropertyName))
            {
                string json = settingsStore.GetString(CollectionPath, ListPropertyName);
                return JsonConvert.DeserializeObject<List<StartupProject>>(json) ?? [];
            }

            return [];
        }
    }
}
