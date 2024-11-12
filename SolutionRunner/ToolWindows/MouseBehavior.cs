using System.Windows;
using System.Windows.Input;

namespace SolutionRunner.ToolWindows
{
    public static class MouseBehavior
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(MouseBehavior),
                new PropertyMetadata(null, OnCommandPropertyChanged));

        public static void SetCommand(DependencyObject d, ICommand value)
            => d.SetValue(CommandProperty, value);

        public static ICommand GetCommand(DependencyObject d)
            => (ICommand)d.GetValue(CommandProperty);

        private static void OnCommandPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if (e.OldValue != null)
                    element.MouseLeftButtonUp -= ElementOnMouseLeftButtonUp;

                if (e.NewValue != null)
                    element.MouseLeftButtonUp += ElementOnMouseLeftButtonUp;
            }
        }

        private static void ElementOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = sender as UIElement;
            var command = GetCommand(element);
            if (command?.CanExecute(null) == true)
                command.Execute(null);
        }
    }
}
