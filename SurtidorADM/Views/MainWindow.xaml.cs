using SurtidorADM.ViewModels;
using System.Windows;
// Asegúrate de importar el namespace donde guardaste el archivo DashboardWindow
// Si DashboardWindow está en SurtidorADM.Views, no necesitas más 'using'.
// Si DashboardWindow está en SurtidorADM (la raíz), añade: using SurtidorADM;

namespace SurtidorADM.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void BtnAbrirDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Esto abre la ventana del Dashboard que creamos
            DashboardWindow dashboard = new DashboardWindow();
            dashboard.Show();
        }

        private void CotejoGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid grid && grid.SelectedItem is ItemCotejo selectedItem)
            {
                var explanationWindow = new ExplicacionDiferenciaWindow(selectedItem);
                explanationWindow.Owner = this;
                explanationWindow.ShowDialog();
            }
        }
    }
}