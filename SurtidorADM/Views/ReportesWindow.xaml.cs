using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using SurtidorADM.Data;
using SurtidorADM.Models;

namespace SurtidorADM.Views
{
    public partial class ReportesWindow : Window
    {
        private bool _isInitializing = true;

        public ReportesWindow()
        {
            InitializeComponent();
            _isInitializing = false;
        }

        private async void DpDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            await CargarVistaPreviaAsync();
        }

        private async Task CargarVistaPreviaAsync()
        {
            if (dpDesde == null || dpHasta == null || dpDesde.SelectedDate == null || dpHasta.SelectedDate == null)
            {
                return;
            }

            DateTime desde = dpDesde.SelectedDate.Value;
            DateTime hasta = dpHasta.SelectedDate.Value;

            if (desde > hasta)
            {
                return;
            }

            try
            {
                using (var context = new SurtidorDbContext())
                {
                    var fechaDesde = desde.Date;
                    var fechaHasta = hasta.Date.AddDays(1).AddTicks(-1);

                    var ventas = await context.VentasIndividualesCashea
                        .Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra <= fechaHasta)
                        .ToListAsync();

                    var todasVentas = await context.VentasIndividualesCashea.ToListAsync();
                    var todosPagos = await context.PagosLiquidacionesCashea.Where(p => p.FechaLiquidacion <= fechaHasta).ToListAsync();
                    var banco = await context.MovimientosBancarios.Where(m => m.Fecha <= fechaHasta).ToListAsync();
                    var historicoTasas = await context.TasasDiarias.ToListAsync();

                    decimal ventasTotales = ventas.Sum(v => v.VentaTotalUsd);
                    decimal pagadoCaja = ventas.Sum(v => v.PagadoCajaUsd);
                    decimal montoFinanciado = ventas.Sum(v => v.MontoFinanciado);

                    decimal totalRecibidoBancoUsd = 0;
                    decimal pendienteUsd = 0;

                    // Calcular recibos de banco en el periodo actual
                    var bancoEnPeriodo = banco.Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta).ToList();
                    foreach (var m in bancoEnPeriodo)
                    {
                        var refBanco = (m.ReferenciaBanco ?? "").Trim();
                        var matchesPago = todosPagos.Any(p => p.ReferenciaBancaria != null && p.ReferenciaBancaria.Trim() == refBanco);
                        if (matchesPago)
                        {
                            decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                            totalRecibidoBancoUsd += (m.MontoAbonadoVes / tasa);
                        }
                    }

                    // Calcular pendienteUsd basándose en vencimiento de cuotas
                    foreach (var venta in todasVentas)
                    {
                        var pagosDeOrden = todosPagos
                            .Where(p => p.IdOrden == venta.IdOrden || 
                                        (string.IsNullOrEmpty(p.IdOrden) && EsPagoDeFactura(p.ReferenciaBancaria, venta.NroFactura)))
                            .ToList();

                        int maxCuotaNo = pagosDeOrden.Any() ? pagosDeOrden.Max(p => p.NroCuotaPagada) : 0;
                        int cantPagosEnBanco = 0;

                        foreach (var pago in pagosDeOrden)
                        {
                            var m = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == pago.ReferenciaBancaria.Trim());
                            if (m != null)
                            {
                                cantPagosEnBanco++;
                            }
                        }

                        int cuotasPagadas = Math.Max(maxCuotaNo, cantPagosEnBanco);

                        bool cuota1Pagada = cuotasPagadas >= 1;
                        bool cuota2Pagada = cuotasPagadas >= 2;
                        bool cuota3Pagada = cuotasPagadas >= 3;

                        if (!cuota1Pagada && venta.FechaCuota1.HasValue && venta.FechaCuota1.Value >= fechaDesde && venta.FechaCuota1.Value <= fechaHasta)
                        {
                            pendienteUsd += venta.MontoCuota1;
                        }
                        if (!cuota2Pagada && venta.FechaCuota2.HasValue && venta.FechaCuota2.Value >= fechaDesde && venta.FechaCuota2.Value <= fechaHasta)
                        {
                            pendienteUsd += venta.MontoCuota2;
                        }
                        if (!cuota3Pagada && venta.FechaCuota3.HasValue && venta.FechaCuota3.Value >= fechaDesde && venta.FechaCuota3.Value <= fechaHasta)
                        {
                            pendienteUsd += venta.MontoCuota3;
                        }
                    }

                    lblPreviewVentas.Text = string.Format("{0:N3} $", ventasTotales);
                    lblPreviewFinanciado.Text = string.Format("{0:N3} $", montoFinanciado);
                    lblPreviewBanco.Text = string.Format("{0:N3} $", totalRecibidoBancoUsd);
                    lblPreviewDebe.Text = string.Format("{0:N3} $", pendienteUsd);
                }
            }
            catch
            {
                // Silenciar errores en la vista previa automática
            }
        }

        private void ChkDetalleVentas_Changed(object sender, RoutedEventArgs e)
        {
            if (borderColumnasVentas != null && chkDetalleVentas != null)
            {
                borderColumnasVentas.IsEnabled = chkDetalleVentas.IsChecked == true;
            }
        }

        private void ChkDetalleBanco_Changed(object sender, RoutedEventArgs e)
        {
            if (borderColumnasBanco != null && chkDetalleBanco != null)
            {
                borderColumnasBanco.IsEnabled = chkDetalleBanco.IsChecked == true;
            }
        }

        private async void BtnExportar_Click(object sender, RoutedEventArgs e)
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

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                FileName = $"Reporte_Personalizado_{desde:yyyy_MM_dd}_a_{hasta:yyyy_MM_dd}.xlsx",
                Title = "Guardar Reporte Personalizado de Excel"
            };

            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            string destFilePath = saveFileDialog.FileName;
            this.Cursor = Cursors.Wait;

            try
            {
                await Task.Run(async () =>
                {
                    using (var context = new SurtidorDbContext())
                    {
                        var fechaDesde = desde.Date;
                        var fechaHasta = hasta.Date.AddDays(1).AddTicks(-1);

                        // 1. Obtener ventas en el rango
                        var ventas = await context.VentasIndividualesCashea
                            .Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra <= fechaHasta)
                            .ToListAsync();

                        var todasVentas = await context.VentasIndividualesCashea.ToListAsync();
                        var todosPagos = await context.PagosLiquidacionesCashea.Where(p => p.FechaLiquidacion <= fechaHasta).ToListAsync();
                        var banco = await context.MovimientosBancarios.Where(m => m.Fecha <= fechaHasta).ToListAsync();
                        var historicoTasas = await context.TasasDiarias.ToListAsync();

                        // 2. Intentar cargar plantilla
                        string templatePath = @"C:\Users\jagc3763\Downloads\Reporte_Mensual_04_2026.xlsx";
                        XLWorkbook workbook;
                        bool loadedFromTemplate = false;

                        if (File.Exists(templatePath))
                        {
                            try
                            {
                                workbook = new XLWorkbook(templatePath);
                                loadedFromTemplate = true;
                            }
                            catch
                            {
                                workbook = new XLWorkbook();
                            }
                        }
                        else
                        {
                            workbook = new XLWorkbook();
                        }

                        // Eliminar siempre "Servicios Prestados" y "Como leer el resumen del report"
                        if (workbook.Worksheets.Contains("Servicios Prestados"))
                            workbook.Worksheets.Delete("Servicios Prestados");
                        if (workbook.Worksheets.Contains("Como leer el resumen del report"))
                            workbook.Worksheets.Delete("Como leer el resumen del report");

                        // 3. Calcular métricas principales
                        decimal ventasTotales = ventas.Sum(v => v.VentaTotalUsd);
                        decimal pagadoCaja = ventas.Sum(v => v.PagadoCajaUsd);
                        decimal montoFinanciado = ventas.Sum(v => v.MontoFinanciado);

                        // Variables para conciliación
                        decimal totalRecibidoBancoVes = 0;
                        decimal totalRecibidoBancoUsd = 0;
                        decimal pendienteUsd = 0;

                        var reporteDetalladoVentas = new List<dynamic>();

                        foreach (var venta in ventas)
                        {
                            var pagosDeOrden = todosPagos
                                .Where(p => p.IdOrden == venta.IdOrden || 
                                            (string.IsNullOrEmpty(p.IdOrden) && EsPagoDeFactura(p.ReferenciaBancaria, venta.NroFactura)))
                                .ToList();

                            decimal totalMontoBancoVes = 0;
                            decimal totalMontoBancoUsd = 0;
                            int maxCuotaNo = pagosDeOrden.Any() ? pagosDeOrden.Max(p => p.NroCuotaPagada) : 0;
                            int cantPagosEnBanco = 0;

                            foreach (var pago in pagosDeOrden)
                            {
                                var m = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == pago.ReferenciaBancaria.Trim());
                                if (m != null)
                                {
                                    decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                                    totalMontoBancoVes += m.MontoAbonadoVes;
                                    totalMontoBancoUsd += (m.MontoAbonadoVes / tasa);
                                    cantPagosEnBanco++;
                                }
                            }

                            int cuotasPagadas = Math.Max(maxCuotaNo, cantPagosEnBanco);
                            totalRecibidoBancoVes += totalMontoBancoVes;
                            totalRecibidoBancoUsd += totalMontoBancoUsd;

                            decimal montoDebe = 0;
                            if (cuotasPagadas < 1) montoDebe += venta.MontoCuota1;
                            if (cuotasPagadas < 2) montoDebe += venta.MontoCuota2;
                            if (cuotasPagadas < 3) montoDebe += venta.MontoCuota3;

                            reporteDetalladoVentas.Add(new
                            {
                                Orden = venta.IdOrden,
                                Factura = venta.NroFactura,
                                Fecha = venta.FechaCompra,
                                Total = venta.VentaTotalUsd,
                                Financiado = venta.MontoFinanciado,
                                Caja = venta.PagadoCajaUsd,
                                BancoVes = totalMontoBancoVes,
                                BancoUsd = totalMontoBancoUsd,
                                Debe = montoDebe,
                                Esperado = pagosDeOrden.Sum(p => p.TotalDepositadoBs),
                                Cuotas = $"{cuotasPagadas}/3",
                                Estado = cuotasPagadas >= 3 ? "Totalmente Pagada" : (cuotasPagadas == 0 ? "No Pagada" : $"Parcial ({cuotasPagadas}/3)"),
                                FechaC1 = venta.FechaCuota1,
                                FechaC2 = venta.FechaCuota2,
                                FechaC3 = venta.FechaCuota3
                            });
                        }

                        // Calcular pendienteUsd basándose en vencimiento de cuotas
                        foreach (var venta in todasVentas)
                        {
                            var pagosDeOrden = todosPagos
                                .Where(p => p.IdOrden == venta.IdOrden || 
                                            (string.IsNullOrEmpty(p.IdOrden) && EsPagoDeFactura(p.ReferenciaBancaria, venta.NroFactura)))
                                .ToList();

                            int maxCuotaNo = pagosDeOrden.Any() ? pagosDeOrden.Max(p => p.NroCuotaPagada) : 0;
                            int cantPagosEnBanco = 0;

                            foreach (var pago in pagosDeOrden)
                            {
                                var m = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == pago.ReferenciaBancaria.Trim());
                                if (m != null)
                                {
                                    cantPagosEnBanco++;
                                }
                            }

                            int cuotasPagadas = Math.Max(maxCuotaNo, cantPagosEnBanco);

                            bool cuota1Pagada = cuotasPagadas >= 1;
                            bool cuota2Pagada = cuotasPagadas >= 2;
                            bool cuota3Pagada = cuotasPagadas >= 3;

                            if (!cuota1Pagada && venta.FechaCuota1.HasValue && venta.FechaCuota1.Value >= fechaDesde && venta.FechaCuota1.Value <= fechaHasta)
                            {
                                pendienteUsd += venta.MontoCuota1;
                            }
                            if (!cuota2Pagada && venta.FechaCuota2.HasValue && venta.FechaCuota2.Value >= fechaDesde && venta.FechaCuota2.Value <= fechaHasta)
                            {
                                pendienteUsd += venta.MontoCuota2;
                            }
                            if (!cuota3Pagada && venta.FechaCuota3.HasValue && venta.FechaCuota3.Value >= fechaDesde && venta.FechaCuota3.Value <= fechaHasta)
                            {
                                pendienteUsd += venta.MontoCuota3;
                            }
                        }

                        // Calcular valores de la portada del Banco (reconciliación del periodo)
                        decimal coverRecibidoBancoUsd = 0;
                        decimal coverCuotasAdelantadasUsd = 0;
                        decimal coverPagoInicialAppUsd = 0;

                        var bancoEnPeriodo = banco.Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta).ToList();
                        foreach (var m in bancoEnPeriodo)
                        {
                            var refBanco = (m.ReferenciaBanco ?? "").Trim();
                            var pagoMatch = todosPagos.FirstOrDefault(p => p.ReferenciaBancaria != null && p.ReferenciaBancaria.Trim() == refBanco);
                            if (pagoMatch != null)
                            {
                                decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                                decimal montoUsd = m.MontoAbonadoVes / tasa;
                                coverRecibidoBancoUsd += montoUsd;

                                var venta = todasVentas.FirstOrDefault(v => v.IdOrden == pagoMatch.IdOrden || 
                                                                             (string.IsNullOrEmpty(pagoMatch.IdOrden) && EsPagoDeFactura(pagoMatch.ReferenciaBancaria, v.NroFactura)));
                                bool isVentaPeriodo = false;
                                if (venta != null && venta.FechaCompra >= fechaDesde && venta.FechaCompra <= fechaHasta)
                                {
                                    isVentaPeriodo = true;
                                }

                                if (pagoMatch.NroCuotaPagada == 0)
                                {
                                    if (isVentaPeriodo)
                                    {
                                        coverPagoInicialAppUsd += montoUsd;
                                    }
                                }
                                else if (venta != null)
                                {
                                    int cuotaNo = pagoMatch.NroCuotaPagada;
                                    DateTime? dueDt = cuotaNo == 1 ? venta.FechaCuota1 : (cuotaNo == 2 ? venta.FechaCuota2 : venta.FechaCuota3);
                                    if (dueDt.HasValue && dueDt.Value > fechaHasta)
                                    {
                                        coverCuotasAdelantadasUsd += montoUsd;
                                    }
                                }
                            }
                        }

                        decimal coverBancoNetoUsd = coverRecibidoBancoUsd - coverCuotasAdelantadasUsd - coverPagoInicialAppUsd;

                        // 4. Modificar Hoja 1: Reporte Mensual
                        if (chkResumenGral.Dispatcher.Invoke(() => chkResumenGral.IsChecked) == true)
                        {
                            if (loadedFromTemplate && workbook.Worksheets.Contains("Reporte Mensual"))
                            {
                                var ws = workbook.Worksheet("Reporte Mensual");
                                ws.Cell("B8").Value = $"Período: Del {desde:dd/MM/yyyy} Hasta {hasta:dd/MM/yyyy}";
                                ws.Cell("D34").Value = ventasTotales;
                                ws.Cell("D35").Value = pagadoCaja;
                                ws.Cell("D36").Value = ventasTotales - pagadoCaja; // Monto Financiado
                                ws.Cell("D37").Value = (ventasTotales > 0) ? ((ventasTotales - pagadoCaja) / ventasTotales) : 0;

                                // Quitar filas de "Servicios Tecnológicos" (filas 39 a 45 en la plantilla original)
                                ws.Rows(39, 45).Delete();

                                // Tras eliminar 7 filas, la tabla de Banco se desplaza hacia arriba:
                                // La celda D58 original ahora es D51, etc.
                                ws.Cell("D51").Value = coverRecibidoBancoUsd;
                                ws.Cell("D52").Value = coverCuotasAdelantadasUsd;
                                ws.Cell("D53").Value = coverPagoInicialAppUsd;
                                ws.Cell("D54").Value = 0;
                                ws.Cell("D55").Value = 0;
                                ws.Cell("D56").Value = coverBancoNetoUsd;

                                ws.Cell("D60").Value = pendienteUsd;
                                ws.Cell("D62").Value = pendienteUsd;

                                ws.Cell("D66").Value = coverBancoNetoUsd;
                                ws.Cell("D67").Value = pendienteUsd;
                                ws.Cell("D68").Value = coverBancoNetoUsd - pendienteUsd;

                                InyectarLogo(ws);
                            }
                            else
                            {
                                var ws = workbook.Worksheets.Add("Reporte Mensual");
                                ws.Cell("B2").Value = "Reporte Mensual de Conciliación";
                                ws.Cell("B3").Value = $"Período: Del {desde:dd/MM/yyyy} Hasta {hasta:dd/MM/yyyy}";
                                ws.Cell("B5").Value = "Ventas Totales:";
                                ws.Cell("C5").Value = ventasTotales;
                                ws.Cell("B6").Value = "Pagado en Caja:";
                                ws.Cell("C6").Value = pagadoCaja;
                                ws.Cell("B7").Value = "Monto Financiado:";
                                ws.Cell("C7").Value = ventasTotales - pagadoCaja;
                                ws.Cell("B8").Value = "Recibido en Banco (USD Equiv):";
                                ws.Cell("C8").Value = coverRecibidoBancoUsd;
                                ws.Cell("B9").Value = "Pendiente por Cobrar (USD):";
                                ws.Cell("C9").Value = pendienteUsd;
                                ws.Columns().AdjustToContents();

                                InyectarLogo(ws);
                            }
                        }
                        else
                        {
                            if (loadedFromTemplate && workbook.Worksheets.Contains("Reporte Mensual"))
                                workbook.Worksheets.Delete("Reporte Mensual");
                        }

                        // 5. Modificar Hoja 2: Ajustes
                        if (chkAjustes.Dispatcher.Invoke(() => chkAjustes.IsChecked) == true)
                        {
                            if (loadedFromTemplate && workbook.Worksheets.Contains("Ajustes"))
                            {
                                var ws = workbook.Worksheet("Ajustes");
                                ws.Cell("C6").Value = $"Período: Del {desde:dd/MM/yyyy} Hasta {hasta:dd/MM/yyyy}";
                                
                                // Limpiar filas anteriores de datos de Ajustes a partir de la fila 10
                                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 9;
                                if (lastRow >= 10)
                                {
                                    ws.Rows(10, lastRow).Delete();
                                }

                                InyectarLogo(ws);
                            }
                            else
                            {
                                var ws = workbook.Worksheets.Add("Ajustes");
                                ws.Cell("A1").Value = "Ajustes y Órdenes Especiales";
                                ws.Cell("A2").Value = $"Período: Del {desde:dd/MM/yyyy} Hasta {hasta:dd/MM/yyyy}";
                                ws.Columns().AdjustToContents();

                                InyectarLogo(ws);
                            }
                        }
                        else
                        {
                            if (loadedFromTemplate && workbook.Worksheets.Contains("Ajustes"))
                                workbook.Worksheets.Delete("Ajustes");
                        }

                        // 6. Generar Hoja 3: Detalle Ventas
                        if (chkDetalleVentas.Dispatcher.Invoke(() => chkDetalleVentas.IsChecked) == true)
                        {
                            if (workbook.Worksheets.Contains("Detalle Ventas"))
                                workbook.Worksheets.Delete("Detalle Ventas");

                            var ws = workbook.Worksheets.Add("Detalle Ventas");
                            
                            InyectarEstiloCabecera(ws, "Reporte Detallado de Ventas", desde, hasta);

                            // Determinar columnas elegidas por el usuario
                            var cols = new List<(string Header, Func<dynamic, object> Getter)>();

                            chkDetalleVentas.Dispatcher.Invoke(() =>
                            {
                                if (colVentaOrden.IsChecked == true) cols.Add(("Número de Orden", x => x.Orden));
                                if (colVentaFactura.IsChecked == true) cols.Add(("Número de Factura", x => x.Factura));
                                if (colVentaFecha.IsChecked == true) cols.Add(("Fecha Compra", x => ((DateTime)x.Fecha).ToString("dd/MM/yyyy")));
                                if (colVentaTotal.IsChecked == true) cols.Add(("Total Venta ($)", x => x.Total));
                                if (colVentaFinanciado.IsChecked == true) cols.Add(("Monto Financiado ($)", x => x.Financiado));
                                if (colVentaBancoVes.IsChecked == true) cols.Add(("Monto Banco (Bs.)", x => x.BancoVes));
                                if (colVentaBancoUsd.IsChecked == true) cols.Add(("Monto Banco ($)", x => x.BancoUsd));
                                if (colVentaDebe.IsChecked == true) cols.Add(("Monto Debe ($)", x => x.Debe));
                                if (colVentaEsperado.IsChecked == true) cols.Add(("Esperado Banco (Bs.)", x => x.Esperado));
                                if (colVentaCuotas.IsChecked == true) cols.Add(("Cuotas Pagadas", x => x.Cuotas));
                                if (colVentaEstado.IsChecked == true) cols.Add(("Estado Pago", x => x.Estado));
                                if (colVentaFechasCuotas.IsChecked == true)
                                {
                                    cols.Add(("Fecha Cuota 1", x => x.FechaC1 != null ? ((DateTime)x.FechaC1).ToString("dd/MM/yyyy") : "N/A"));
                                    cols.Add(("Fecha Cuota 2", x => x.FechaC2 != null ? ((DateTime)x.FechaC2).ToString("dd/MM/yyyy") : "N/A"));
                                    cols.Add(("Fecha Cuota 3", x => x.FechaC3 != null ? ((DateTime)x.FechaC3).ToString("dd/MM/yyyy") : "N/A"));
                                }
                            });

                            // Escribir cabecera en fila 6
                            for (int i = 0; i < cols.Count; i++)
                            {
                                var cell = ws.Cell(6, i + 1);
                                cell.Value = cols[i].Header;
                                cell.Style.Font.Bold = true;
                                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#112A46");
                                cell.Style.Font.FontColor = XLColor.White;
                            }

                            // Escribir datos a partir de fila 7
                            int rowIdx = 7;
                            foreach (var rowData in reporteDetalladoVentas)
                            {
                                for (int i = 0; i < cols.Count; i++)
                                {
                                    ws.Cell(rowIdx, i + 1).Value = XLCellValue.FromObject(cols[i].Getter(rowData));
                                }
                                rowIdx++;
                            }

                            ws.Columns().AdjustToContents();
                        }

                        // 7. Generar Hoja 4: Detalle Banco (Con Órdenes Relacionadas)
                        if (chkDetalleBanco.Dispatcher.Invoke(() => chkDetalleBanco.IsChecked) == true)
                        {
                            if (workbook.Worksheets.Contains("Detalle Banco"))
                                workbook.Worksheets.Delete("Detalle Banco");

                            var ws = workbook.Worksheets.Add("Detalle Banco");
                            
                            InyectarEstiloCabecera(ws, "Reporte Detallado de Banco", desde, hasta);

                            // Filtrar movimientos bancarios y mapearlos a sus órdenes
                            var movimientos = banco
                                .Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta)
                                .OrderBy(m => m.Fecha)
                                .ToList();

                            var reporteDetalladoBanco = movimientos.Select(m =>
                            {
                                var refBanco = (m.ReferenciaBanco ?? "").Trim();
                                var pagoMatch = todosPagos.FirstOrDefault(p => p.ReferenciaBancaria != null && p.ReferenciaBancaria.Trim() == refBanco);

                                string orden = "No Conciliado";
                                string factura = "N/A";
                                string cuota = "N/A";
                                string fechaLiq = "N/A";

                                if (pagoMatch != null)
                                {
                                    orden = pagoMatch.IdOrden ?? "Orden Virtual";
                                    cuota = pagoMatch.NroCuotaPagada > 0 ? pagoMatch.NroCuotaPagada.ToString() : "Abono/Caja";
                                    fechaLiq = pagoMatch.FechaLiquidacion.ToString("dd/MM/yyyy");

                                    var ventaMatch = ventas.FirstOrDefault(v => v.IdOrden == pagoMatch.IdOrden);
                                    if (ventaMatch != null)
                                    {
                                        factura = ventaMatch.NroFactura;
                                    }
                                    else if (!string.IsNullOrEmpty(pagoMatch.ReferenciaBancaria))
                                    {
                                        var vf = ventas.FirstOrDefault(v => EsPagoDeFactura(pagoMatch.ReferenciaBancaria, v.NroFactura));
                                        if (vf != null)
                                        {
                                            factura = vf.NroFactura;
                                        }
                                    }
                                }

                                return new
                                {
                                    FechaMov = m.Fecha.ToString("dd/MM/yyyy"),
                                    Referencia = refBanco,
                                    m.Descripcion,
                                    MontoBs = m.MontoAbonadoVes,
                                    Orden = orden,
                                    Factura = factura,
                                    Cuota = cuota,
                                    FechaLiquidacion = fechaLiq
                                };
                            }).ToList();

                            var cols = new List<(string Header, Func<dynamic, object> Getter)>();

                            chkDetalleBanco.Dispatcher.Invoke(() =>
                            {
                                if (colBancoFecha.IsChecked == true) cols.Add(("Fecha Movimiento", x => x.FechaMov));
                                if (colBancoReferencia.IsChecked == true) cols.Add(("Referencia Bancaria", x => x.Referencia));
                                if (colBancoDescripcion.IsChecked == true) cols.Add(("Descripción", x => x.Descripcion));
                                if (colBancoMonto.IsChecked == true) cols.Add(("Monto Abonado (Bs.)", x => x.MontoBs));
                                if (colBancoOrden.IsChecked == true) cols.Add(("Orden Asociada", x => x.Orden));
                                if (colBancoFactura.IsChecked == true) cols.Add(("Factura Asociada", x => x.Factura));
                                if (colBancoCuota.IsChecked == true) cols.Add(("Cuota Pagada", x => x.Cuota));
                                if (colBancoFechaLiq.IsChecked == true) cols.Add(("Fecha Liquidación Cashea", x => x.FechaLiquidacion));
                            });

                            // Escribir cabecera en fila 6
                            for (int i = 0; i < cols.Count; i++)
                            {
                                var cell = ws.Cell(6, i + 1);
                                cell.Value = cols[i].Header;
                                cell.Style.Font.Bold = true;
                                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A3A5D");
                                cell.Style.Font.FontColor = XLColor.White;
                            }

                            // Escribir datos a partir de fila 7
                            int rowIdx = 7;
                            foreach (var rowData in reporteDetalladoBanco)
                            {
                                for (int i = 0; i < cols.Count; i++)
                                {
                                    ws.Cell(rowIdx, i + 1).Value = XLCellValue.FromObject(cols[i].Getter(rowData));
                                }
                                rowIdx++;
                            }

                            ws.Columns().AdjustToContents();
                        }

                        workbook.SaveAs(destFilePath);

                        // Funciones auxiliares internas de ClosedXML para inyectar estilo y logo
                        void InyectarLogo(IXLWorksheet ws)
                        {
                            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "surtidor_logo.png");
                            if (File.Exists(logoPath))
                            {
                                foreach (var p in ws.Pictures.ToList())
                                {
                                    p.Delete();
                                }
                                var pic = ws.AddPicture(logoPath);
                                pic.MoveTo(ws.Cell("B2"));
                                pic.Width = 180;
                                pic.Height = 50;
                            }
                        }

                        void InyectarEstiloCabecera(IXLWorksheet ws, string titulo, DateTime desdeDate, DateTime hastaDate)
                        {
                            InyectarLogo(ws);

                            var cellTitulo = ws.Cell("D2");
                            cellTitulo.Value = titulo;
                            cellTitulo.Style.Font.Bold = true;
                            cellTitulo.Style.Font.FontSize = 16;
                            cellTitulo.Style.Font.FontColor = XLColor.FromHtml("#112A46");

                            var cellSub = ws.Cell("D3");
                            cellSub.Value = $"Período: Del {desdeDate:dd/MM/yyyy} Hasta {hastaDate:dd/MM/yyyy}";
                            cellSub.Style.Font.Italic = true;
                            cellSub.Style.Font.FontSize = 11;
                            cellSub.Style.Font.FontColor = XLColor.FromHtml("#7F8C8D");
                        }
                    }
                });

                MessageBox.Show("Reporte exportado exitosamente a Excel.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar reporte: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private bool EsPagoDeFactura(string referencia, string factura)
        {
            if (string.IsNullOrEmpty(referencia) || string.IsNullOrEmpty(factura))
                return false;

            string r = referencia.Trim();
            string f = factura.Trim();

            if (r.All(char.IsDigit) && f.All(char.IsDigit))
            {
                return r == f;
            }

            return r.Contains(f) || f.Contains(r);
        }

        private decimal ObtenerTasaFechaValor(DateTime fecha, List<TasaDiaria> historico)
        {
            var fechaBase = fecha.Date;
            var fechaBusqueda = fechaBase;
            if (fechaBase.DayOfWeek == DayOfWeek.Sunday)
                fechaBusqueda = fechaBase.AddDays(-2); // Domingo -> T-2 (Viernes)
            else if (fechaBase.DayOfWeek == DayOfWeek.Monday)
                fechaBusqueda = fechaBase.AddDays(-1); // Lunes -> T-1 (Viernes)
            else
                fechaBusqueda = fechaBase.AddDays(-1); // Lunes a Viernes -> T-1

            var tasaAplicable = historico
                .Where(t => t.Fecha.Date <= fechaBusqueda.Date)
                .OrderByDescending(t => t.Fecha)
                .FirstOrDefault();

            if (tasaAplicable != null && tasaAplicable.TasaBcvVes > 0)
            {
                return tasaAplicable.TasaBcvVes;
            }

            var fallback = historico.FirstOrDefault(t => t.Fecha.Date == fechaBase);
            return (fallback != null && fallback.TasaBcvVes > 0) ? fallback.TasaBcvVes : 36m;
        }
    }
}
