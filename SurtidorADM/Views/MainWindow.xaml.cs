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

            // Cargar API Key guardada si existe
            try
            {
                var geminiService = new SurtidorADM.Services.GeminiChatService();
                PbApiKey.Password = geminiService.ObtenerApiKey();
            }
            catch { /* Ignore startup load errors */ }
        }

        private void BtnGuardarApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var geminiService = new SurtidorADM.Services.GeminiChatService();
                geminiService.GuardarApiKey(PbApiKey.Password);
                MessageBox.Show("¡API Key guardada exitosamente! Ya puedes interactuar con el Asistente IA.", "Guardar Clave", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error al guardar la clave: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TextBoxChat_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // Trigger the send command in the ViewModel
                if (DataContext is MainViewModel vm && vm.EnviarMensajeChatCommand.CanExecute(null))
                {
                    vm.EnviarMensajeChatCommand.Execute(null);
                    
                    // Hacer scroll hacia abajo en la conversación
                    System.Threading.Tasks.Task.Delay(50).ContinueWith(t => 
                    {
                        Dispatcher.Invoke(() => 
                        {
                            ChatScrollViewer.ScrollToEnd();
                        });
                    });
                }
            }
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