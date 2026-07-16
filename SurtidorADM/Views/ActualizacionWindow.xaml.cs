using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using SurtidorADM.Services;

namespace SurtidorADM.Views
{
    /// <summary>
    /// Lógica de interacción para ActualizacionWindow.xaml
    /// </summary>
    public partial class ActualizacionWindow : Window
    {
        private readonly string _urlDescarga;
        private readonly UpdateService _updateService;

        public ActualizacionWindow(ResultadoVerificacion checkResult)
        {
            InitializeComponent();
            _urlDescarga = checkResult.UrlDescarga;
            _updateService = new UpdateService();

            TxtVersiones.Text = $"Versión {UpdateService.VersionActual} → Versión {checkResult.VersionNueva}";
            TxtNovedades.Text = string.IsNullOrEmpty(checkResult.Novedades) 
                ? "• Mejoras generales de estabilidad y conciliación bancaria."
                : checkResult.Novedades;
        }

        private void RecordarLuego_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void ActualizarAhora_Click(object sender, RoutedEventArgs e)
        {
            // Ocultar botones de acción y mostrar barra de progreso
            PanelBotones.Visibility = Visibility.Collapsed;
            PanelProgreso.Visibility = Visibility.Visible;
            TxtEstado.Text = "Descargando archivos de actualización en segundo plano...";

            try
            {
                // Nombre del archivo temporal nuevo
                string tempFile = Path.Combine(Path.GetTempPath(), "SurtidorADM_new.exe");

                // Reporte de progreso asincrónico
                var progress = new Progress<double>(val =>
                {
                    BarraProgreso.Value = val;
                    TxtProgreso.Text = $"Descargando actualización... {(int)val}%";
                });

                // Descargamos asincrónicamente
                await Task.Run(() => _updateService.DescargarActualizacionAsync(_urlDescarga, tempFile, progress));

                TxtEstado.Text = "Descarga completa. Reiniciando aplicación y aplicando cambios...";
                await Task.Delay(1500); // Dar un respiro visual al usuario

                // Lanzamos la actualización y cerramos
                _updateService.EjecutarInstalacionHot(tempFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error al descargar la actualización:\n{ex.Message}", 
                    "Error de Actualización", MessageBoxButton.OK, MessageBoxImage.Error);

                // Volver a mostrar controles si falla
                PanelBotones.Visibility = Visibility.Visible;
                PanelProgreso.Visibility = Visibility.Collapsed;
                TxtEstado.Text = "Error en la descarga. Por favor, intente de nuevo más tarde.";
            }
        }
    }
}
