using System;
using System.Windows;
using SurtidorADM.Services; // ¡Esto es vital para que encuentre DashboardService!
using System.Collections.Generic;

namespace SurtidorADM.Views
{
    public partial class DashboardWindow : Window
    {

        // Botón nuevo: Mostrar Auditoría
        private async void BtnAuditoria_Click(object sender, RoutedEventArgs e)
        {
            LimpiarMetricas();
            var auditoria = await _dashboardService.ObtenerAuditoriaFinancieraAsync();
            dgResultados.ItemsSource = new List<dynamic> { auditoria };
        }

        // Modificación: Buscar Orden (Ahora llama al nuevo método detallado)
        private async void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            string idOrden = txtBusquedaOrden.Text.Trim();
            if (string.IsNullOrEmpty(idOrden)) return;

            var resultado = await _dashboardService.ConsultarEstadoOrdenDetalladoAsync(idOrden);

            if (resultado != null)
            {
                var lista = new List<dynamic> { resultado };
                dgResultados.ItemsSource = lista;
                ActualizarMetricas(lista);
            }
            else
            {
                LimpiarMetricas();
                dgResultados.ItemsSource = null;
                MessageBox.Show("No se encontró la orden.");
            }
        }

        // Nuevo: Buscar por Rango de Fechas
        private async void BtnBuscarPorFecha_Click(object sender, RoutedEventArgs e)
        {
            if (dpDesde.SelectedDate == null || dpHasta.SelectedDate == null)
            {
                MessageBox.Show("Por favor, seleccione ambas fechas.");
                return;
            }

            DateTime desde = dpDesde.SelectedDate.Value;
            DateTime hasta = dpHasta.SelectedDate.Value;

            if (desde > hasta)
            {
                MessageBox.Show("La fecha 'Desde' no puede ser mayor que la fecha 'Hasta'.");
                return;
            }

            var resultadoReporte = await _dashboardService.ConsultarOrdenesPorRangoFechasAsync(desde, hasta);

            if (resultadoReporte != null && resultadoReporte.Ordenes.Count > 0)
            {
                dgResultados.ItemsSource = resultadoReporte.Ordenes;
                ActualizarMetricas(resultadoReporte.Ordenes, resultadoReporte.TotalVendido, resultadoReporte.TotalCaja, resultadoReporte.TotalFinanciado);
            }
            else
            {
                dgResultados.ItemsSource = null;
                LimpiarMetricas();
                MessageBox.Show("No se encontraron órdenes en el rango de fechas seleccionado.");
            }
        }

        private void ActualizarMetricas(List<dynamic> ordenes, decimal? totalVentaManual = null, decimal? totalCajaManual = null, decimal? totalFinanciadoManual = null)
        {
            if (ordenes == null || ordenes.Count == 0)
            {
                LimpiarMetricas();
                return;
            }

            // Fila 1: Totales Generales
            decimal totalVenta = totalVentaManual ?? 0;
            decimal totalCaja = totalCajaManual ?? 0;
            decimal totalFinanciado = totalFinanciadoManual ?? 0;

            if (totalVentaManual == null)
            {
                foreach (var o in ordenes)
                {
                    totalVenta += (decimal)o.Venta_Total_USD;
                    totalCaja += (decimal)o.Pagado_Caja_USD;
                    totalFinanciado += (decimal)o.Monto_Financiado_USD;
                }
            }

            tbTotalVendido.Text = string.Format("{0:N3} $", totalVenta);
            tbTotalCaja.Text = string.Format("{0:N3} $", totalCaja);
            tbTotalFinanciado.Text = string.Format("{0:N3} $", totalFinanciado);

            // Fila 2: Métricas Secundarias
            int cuotasRestantesSum = 0;
            decimal cuotasRestantesMonto = 0;
            int cuotasPagadasSum = 0;
            decimal cuotasPagadasMonto = 0;
            int ordenesPendientesCount = 0;

            foreach (var o in ordenes)
            {
                int cr = (int)o.Cuotas_Restantes;
                decimal montoDebe = (decimal)o.Monto_Debe_USD;
                decimal montoFinanciado = (decimal)o.Monto_Financiado_USD;

                cuotasRestantesSum += cr;
                cuotasRestantesMonto += montoDebe;

                int cp = Math.Max(0, 3 - cr);
                cuotasPagadasSum += cp;
                cuotasPagadasMonto += Math.Max(0m, montoFinanciado - montoDebe);

                if (cr > 0)
                {
                    ordenesPendientesCount++;
                }
            }

            tbCuotasRestantes.Text = string.Format("{0} cuotas ({1} ord.) / {2:N3} $", cuotasRestantesSum, ordenesPendientesCount, cuotasRestantesMonto);
            tbCuotasPagadas.Text = string.Format("{0} cuotas / {1:N3} $", cuotasPagadasSum, cuotasPagadasMonto);
            tbDebeCashea.Text = string.Format("{0:N3} $", cuotasRestantesMonto);
        }

        private void LimpiarMetricas()
        {
            tbTotalVendido.Text = "0.000 $";
            tbTotalCaja.Text = "0.000 $";
            tbTotalFinanciado.Text = "0.000 $";
            tbCuotasRestantes.Text = "0 cuotas (0 ord.) / 0.000 $";
            tbCuotasPagadas.Text = "0 cuotas / 0.000 $";
            tbDebeCashea.Text = "0.000 $";
        }

        private readonly DashboardService _dashboardService = new DashboardService();

        public DashboardWindow()
        {
            InitializeComponent();
            // Inicializar los selectores de fecha con la fecha de hoy
            dpDesde.SelectedDate = DateTime.Today;
            dpHasta.SelectedDate = DateTime.Today;
        }

        private async void BtnVentas_Click(object sender, RoutedEventArgs e)
        {
            LimpiarMetricas();
            dgResultados.ItemsSource = await _dashboardService.ObtenerResumenVentasAsync();
        }

        private async void BtnConseguidas_Click(object sender, RoutedEventArgs e)
        {
            LimpiarMetricas();
            var data = await _dashboardService.ObtenerEstadoConciliacionAsync();
            dgResultados.ItemsSource = data.Conseguidas;
        }

        private async void BtnNoConseguidas_Click(object sender, RoutedEventArgs e)
        {
            LimpiarMetricas();
            var data = await _dashboardService.ObtenerEstadoConciliacionAsync();
            dgResultados.ItemsSource = data.NoConseguidas;
        }

        private async void BtnSobrantes_Click(object sender, RoutedEventArgs e)
        {
            LimpiarMetricas();
            var data = await _dashboardService.ObtenerEstadoConciliacionAsync();
            dgResultados.ItemsSource = data.Sobrantes;
        }
    }
}