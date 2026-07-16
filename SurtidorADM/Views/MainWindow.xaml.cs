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

        private async void BtnBuscarActualizaciones_Click(object sender, RoutedEventArgs e)
        {
            var updateService = new SurtidorADM.Services.UpdateService();
            
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false;
            
            var checkResult = await updateService.VerificarActualizacionAsync();
            
            if (btn != null) btn.IsEnabled = true;

            if (checkResult.HayActualizacion)
            {
                var updateWin = new SurtidorADM.Views.ActualizacionWindow(checkResult);
                updateWin.Owner = this;
                updateWin.ShowDialog();
            }
            else
            {
                MessageBox.Show($"Su aplicación ya se encuentra actualizada a la última versión disponible (v{SurtidorADM.Services.UpdateService.VersionActual}).", 
                    "Buscar Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}