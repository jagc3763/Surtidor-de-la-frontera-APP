using System.Configuration;
using System.Data;
using System.Windows;
using SurtidorADM.Data;

namespace SurtidorADM
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Forzar la creación de la base de datos SQLite al iniciar
            using (var context = new SurtidorDbContext())
            {
                // Esto lee las clases y genera el archivo surtidor.db con sus tablas correspondientes
                context.Database.EnsureCreated();
            }
        }
    }
}