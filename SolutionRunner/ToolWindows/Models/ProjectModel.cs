using CommunityToolkit.Mvvm.ComponentModel;

namespace SolutionRunner.ToolWindows.Models
{
    public class ProjectModel : ObservableObject
    {
        private string projectName;
        public string ProjectName
        {
            get => projectName;
            set => SetProperty(ref projectName, value);
        }

        public Project SolutionProject { get; set; }

        private RunType projectRunType;
        public RunType ProjectRunType
        {
            get => projectRunType;
            set
            {
                if (SetProperty(ref projectRunType, value))
                    OnPropertyChanged(nameof(IsChecked));
            }
        }

        private bool isStartingOrStopping;
        public bool IsStartingOrStopping
        {
            get => isStartingOrStopping;
            set => SetProperty(ref isStartingOrStopping, value);
        }

        private bool isRunning;
        public bool IsRunning
        {
            get => isRunning;
            set => SetProperty(ref isRunning, value);
        }

        private bool isDebugging;
        public bool IsDebugging
        {
            get => isDebugging;
            set => SetProperty(ref isDebugging, value);
        }

        private bool haveBuildError;
        public bool HaveBuildError
        {
            get => haveBuildError;
            set => SetProperty(ref haveBuildError, value);
        }

        private int numberOfWarnings;
        public int NumberOfWarnings
        {
            get => numberOfWarnings;
            set => SetProperty(ref numberOfWarnings, value);
        }

        private int numberOfErrors;
        public int NumberOfErrors
        {
            get => numberOfErrors;
            set => SetProperty(ref numberOfErrors, value);
        }
        public OutputWindowPane Output { get; set; }

        public bool? IsChecked
        {
            get => ProjectRunType switch
            {
                RunType.None => false,
                RunType.Run => true,
                RunType.Debug => null,
                _ => false
            };
            set
            {
                ProjectRunType = value switch
                {
                    true => RunType.Run,
                    false => RunType.None,
                    null => RunType.Debug
                };
            }
        }
    }
}
