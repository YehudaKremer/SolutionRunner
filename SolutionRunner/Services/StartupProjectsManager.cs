using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SolutionRunner.Services
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

        public async Task SaveStringListAsync(IEnumerable<string> list)
        {
            string json = JsonConvert.SerializeObject(list.OrderBy(s => s));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            settingsStore.SetString(CollectionPath, ListPropertyName, json);
        }

        public async Task<List<string>> LoadStringListAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (settingsStore.PropertyExists(CollectionPath, ListPropertyName))
            {
                string json = settingsStore.GetString(CollectionPath, ListPropertyName);
                return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
            }

            return new List<string>();
        }
    }
}
