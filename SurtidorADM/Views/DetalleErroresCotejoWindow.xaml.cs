using System;
using System.Collections.Generic;
using System.Windows;
using SurtidorADM.ViewModels;

namespace SurtidorADM.Views
{
    /// <summary>
    /// Lógica de interacción para DetalleErroresCotejoWindow.xaml
    /// </summary>
    public partial class DetalleErroresCotejoWindow : Window
    {
        public DetalleErroresCotejoWindow(IEnumerable<DetalleDiferencia> diferencias)
        {
            InitializeComponent();
            GridDiferencias.ItemsSource = diferencias;
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
