using System.Configuration;
using System.Data;
using System.Windows;
using SurtidorADM.Data;

namespace SurtidorADM
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Forzar la creación de la base de datos SQLite al iniciar
            using (var context = new SurtidorDbContext())
            {
                // Esto lee las clases y genera el archivo surtidor.db con sus tablas correspondientes
                context.Database.EnsureCreated();
            }

            // 1. Verificar si hay actualizaciones
            var updateService = new SurtidorADM.Services.UpdateService();
            var checkResult = await updateService.VerificarActualizacionAsync();

            if (checkResult.HayActualizacion)
            {
                var updateWin = new SurtidorADM.Views.ActualizacionWindow(checkResult);
                bool? updateAccepted = updateWin.ShowDialog();

                if (updateAccepted == true)
                {
                    // Si aceptó y se descargó con éxito, EjecutarInstalacionHot cerrará la app
                    return;
                }
            }

            // 2. Si no hay actualizaciones o decide posponer, abrir la ventana principal
            var mainWin = new SurtidorADM.Views.MainWindow();
            mainWin.Show();
        }
    }
}