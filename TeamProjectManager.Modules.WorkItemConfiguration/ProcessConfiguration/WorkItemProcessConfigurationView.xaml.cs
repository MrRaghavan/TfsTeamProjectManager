using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Controls;

namespace TeamProjectManager.Modules.WorkItemConfiguration.ProcessConfiguration
{
    [Export]
    public partial class WorkItemProcessConfigurationView : UserControl
    {
        public WorkItemProcessConfigurationView()
        {
            InitializeComponent();
        }

        [Import]
        public WorkItemProcessConfigurationViewModel ViewModel
        {
            get
            {
                return (WorkItemProcessConfigurationViewModel)this.DataContext;
            }
            set
            {
                this.DataContext = value;
            }
        }

        private void processConfigurationsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.ViewModel.SelectedProcessConfigurations = this.processConfigurationsDataGrid.SelectedItems.Cast<WorkItemConfigurationItemExport>().ToList();
        }

        private void processConfigurationsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ViewModel.EditSelectedProcessConfigurationsCommand.CanExecute(ViewModel.SelectedProcessConfigurations))
            {
                ViewModel.EditSelectedProcessConfigurationsCommand.Execute(ViewModel.SelectedProcessConfigurations);
            }
        }
    }
}