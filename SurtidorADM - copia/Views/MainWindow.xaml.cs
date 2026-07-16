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
    }
}