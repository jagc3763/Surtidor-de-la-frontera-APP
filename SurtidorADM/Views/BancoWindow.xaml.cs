using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SurtidorADM.Services;

namespace SurtidorADM.Views
{
    public partial class BancoWindow : Window
    {
        private readonly DashboardService _dashboardService = new DashboardService();

        public BancoWindow()
        {
            InitializeComponent();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            EjecutarBusqueda();
        }

        public async void EjecutarBusqueda()
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

            try
            {
                var result = await _dashboardService.ObtenerReporteBancoRangoAsync(desde, hasta);
                dgResultados.ItemsSource = result.Items;

                ActualizarMetricasBanco(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar reporte de banco: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ActualizarMetricasBanco(ReporteBancoRangoResult result)
        {
            if (result == null || result.Items == null || !result.Items.Any())
            {
                tbSaldoBancoBs.Text = "0.000 Bs. / 0.000 $";
                tbOrdenesPagadasBs.Text = "0 ord. / 0.000 Bs. / 0.000 $";
                tbOrdenesPendientesUsd.Text = "0 ord. / 0.000 $";
                tbEsperadoBancoUsd.Text = "0.000 $";
                return;
            }

            var data = result.Items;

            // Card 1: Saldo en banco (use the calculated period-specific totals)
            decimal saldoBancoBs = result.SaldoBancoBs;
            decimal saldoBancoUsd = result.SaldoBancoUsd;
            
            // Card 2: Órdenes Pagadas
            int pagadasCount = 0;
            decimal pagadasMontoBs = 0;
            decimal pagadasMontoUsd = 0;

            // Card 3: Órdenes Pendientes
            int pendientesCount = 0;
            decimal pendientesMontoUsd = 0;

            // Card 4: Esperado en banco
            decimal esperadoBancoUsd = 0;

            foreach (var item in data)
            {
                decimal montoBancoVes = (decimal)item.Monto_En_Banco_VES;
                decimal montoBancoUsd = (decimal)item.Monto_En_Banco_USD;
                decimal montoDebeUsd = (decimal)item.Monto_Debe_USD;
                decimal esperadoUsd = (decimal)item.Monto_Financiado_USD;
                string estado = (string)item.Estado_Banco;

                esperadoBancoUsd += esperadoUsd;

                if (estado == "Totalmente Pagada")
                {
                    pagadasCount++;
                    pagadasMontoBs += montoBancoVes;
                    pagadasMontoUsd += montoBancoUsd;
                }
                else
                {
                    pendientesCount++;
                    pendientesMontoUsd += montoDebeUsd;
                }
            }

            tbSaldoBancoBs.Text = string.Format("{0:N3} Bs. / {1:N3} $", saldoBancoBs, saldoBancoUsd);
            tbOrdenesPagadasBs.Text = string.Format("{0} ord. / {1:N3} Bs. / {2:N3} $", pagadasCount, pagadasMontoBs, pagadasMontoUsd);
            tbOrdenesPendientesUsd.Text = string.Format("{0} ord. / {1:N3} $", pendientesCount, pendientesMontoUsd);
            tbEsperadoBancoUsd.Text = string.Format("{0:N3} $", esperadoBancoUsd);
        }
    }
}
