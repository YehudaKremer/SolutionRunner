using Newtonsoft.Json.Linq;
using SolutionRunner.ToolWindows.Views;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SolutionRunner
{
    public static class VersionChecker
    {
        public static string GetInstalledVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version.ToString();
            return version;
        }

        private const string MarketplaceUrl = "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery?api-version=6.1-preview.1";

        public static async Task<string> GetLatestVersionAsync(string publisherName, string extensionName)
        {
            using var httpClient = new HttpClient();
            var requestContent = new
            {
                filters = new[]
                {
                    new
                    {
                        criteria = new[]
                        {
                            new { filterType = 10, value = extensionName }
                        }
                    }
                },
                assetTypes = new string[0],
                flags = 512 // Flag for including version info
            };

            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(requestContent), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(MarketplaceUrl, content);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(jsonResponse);
                var latestVersion = json["results"]?[0]?["extensions"]?[0]?["versions"]?[0]?["version"]?.ToString();

                return latestVersion ?? throw new Exception("Could not retrieve version information from the Visual Studio Marketplace.");

            }
            catch (HttpRequestException error)
            {
                await SolutionRunnerWindowControl.Output.WriteLineAsync($"Error querying the Visual Studio Marketplace API, Error: {error.Message} | StackTrace: {error.StackTrace}");
                throw;
            }
        }

        public static async Task<bool> CheckIfExtensionHaveNewVersionAsync()
        {
            string currentVersion = GetInstalledVersion();
            string currentVersionShort = string.Join(".", currentVersion.Split('.').Take(3));

            string latestVersion = await GetLatestVersionAsync("YehudaK", "SolutionRunner");
            string latestVersionShort = string.Join(".", latestVersion.Split('.').Take(3));

            return !currentVersionShort.Equals(latestVersionShort, StringComparison.OrdinalIgnoreCase);
        }
    }
}
