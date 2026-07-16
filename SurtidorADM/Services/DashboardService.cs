using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SurtidorADM.Data;
using SurtidorADM.Models;

namespace SurtidorADM.Services
{
    public class EstadoConciliacionResult
    {
        public List<dynamic> Conseguidas { get; set; } = new();
        public List<dynamic> NoConseguidas { get; set; } = new();
        public List<dynamic> Sobrantes { get; set; } = new();
    }

    public class ReporteRangoFechasResult
    {
        public List<dynamic> Ordenes { get; set; } = new();
        public decimal TotalVendido { get; set; }
        public decimal TotalCaja { get; set; }
        public decimal TotalFinanciado { get; set; }
    }

    public class ReporteBancoRangoResult
    {
        public List<dynamic> Items { get; set; } = new();
        public decimal SaldoBancoBs { get; set; }
        public decimal SaldoBancoUsd { get; set; }
    }

    public class DashboardService
    {
        // AUDITORÍA: Calcula USD usando la tasa histórica de la "Fecha de Valor"
        public async Task<dynamic> ObtenerAuditoriaFinancieraAsync(DateTime? desde = null, DateTime? hasta = null)
        {
            using (var context = new SurtidorDbContext())
            {
                // Traemos los datos a memoria para evitar errores de compilación con EF Core
                var ventas = await context.VentasIndividualesCashea.ToListAsync();
                var pagos = await context.PagosLiquidacionesCashea.ToListAsync();
                var banco = await context.MovimientosBancarios.ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                if (desde.HasValue && hasta.HasValue)
                {
                    var fechaDesde = desde.Value.Date;
                    var fechaHasta = hasta.Value.Date.AddDays(1).AddTicks(-1);

                    ventas = ventas.Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra <= fechaHasta).ToList();
                    pagos = pagos.Where(p => p.FechaLiquidacion >= fechaDesde && p.FechaLiquidacion <= fechaHasta).ToList();
                    banco = banco.Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta).ToList();
                }

                var totalVentas = ventas.Sum(v => v.VentaTotalUsd);
                var totalPagadoCashea = pagos.Sum(p => p.TotalDepositadoUsd);

                decimal totalEnBancoUsd = 0;

                foreach (var m in banco)
                {
                    decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                    totalEnBancoUsd += (m.MontoAbonadoVes / tasa);
                }

                return new
                {
                    Total_Vendido_USD = TruncarA3Decimales(totalVentas),
                    Total_Pagado_Cashea_USD = TruncarA3Decimales(totalPagadoCashea),
                    Total_En_Banco_USD = TruncarA3Decimales(totalEnBancoUsd),
                    Diferencia_Conciliacion = TruncarA3Decimales(totalPagadoCashea - totalEnBancoUsd)
                };
            }
        }

        private bool EsPagoDeFactura(string refBancaria, string nroFactura)
        {
            if (string.IsNullOrEmpty(refBancaria) || string.IsNullOrEmpty(nroFactura))
                return false;

            string r = refBancaria.Trim().TrimStart('0');
            string f = nroFactura.Trim().TrimStart('0');

            if (r == f)
                return true;

            // Si ambos son puramente numéricos, requerimos coincidencia exacta para evitar falsos positivos
            bool rEsNumerico = r.All(char.IsDigit);
            bool fEsNumerico = f.All(char.IsDigit);

            if (rEsNumerico && fEsNumerico)
            {
                return false;
            }

            // Si la referencia contiene letras/texto, permitimos coincidencia parcial
            if (f.Length >= 4)
            {
                return r.Contains(f);
            }
            else
            {
                return r.StartsWith(f) || (r.StartsWith("6") && r.Length > 1 && r.Substring(1).StartsWith(f));
            }
        }

        // BÚSQUEDA DETALLADA: Cruce por número de orden
        public async Task<dynamic> ConsultarEstadoOrdenDetalladoAsync(string idOrden)
        {
            if (string.IsNullOrEmpty(idOrden)) return null;
            idOrden = idOrden.Trim();

            using (var context = new SurtidorDbContext())
            {
                VentaIndividualCashea venta = null;
                List<PagoLiquidacionCashea> pagos = new();

                // 1. Buscamos por IdOrden o por NroFactura en la tabla de Ventas
                venta = await context.VentasIndividualesCashea
                    .FirstOrDefaultAsync(v => v.IdOrden == idOrden || v.NroFactura == idOrden);

                if (venta == null)
                {
                    // 2. Si no se encuentra, buscamos en pagos por IdOrden o por ReferenciaBancaria
                    var pagoCoincidente = await context.PagosLiquidacionesCashea
                        .FirstOrDefaultAsync(p => p.IdOrden == idOrden || p.ReferenciaBancaria == idOrden);

                    if (pagoCoincidente != null)
                    {
                        if (!string.IsNullOrEmpty(pagoCoincidente.IdOrden))
                        {
                            venta = await context.VentasIndividualesCashea
                                .FirstOrDefaultAsync(v => v.IdOrden == pagoCoincidente.IdOrden);
                        }

                        if (venta == null && !string.IsNullOrEmpty(pagoCoincidente.ReferenciaBancaria))
                        {
                            var ventas = await context.VentasIndividualesCashea.ToListAsync();
                            venta = ventas.FirstOrDefault(v => EsPagoDeFactura(pagoCoincidente.ReferenciaBancaria, v.NroFactura));
                        }
                    }
                }

                // 3. Si encontramos la venta, buscamos todos los pagos asociados a ella
                if (venta != null)
                {
                    var todosPagos = await context.PagosLiquidacionesCashea.ToListAsync();
                    pagos = todosPagos
                        .Where(p => p.IdOrden == venta.IdOrden || 
                                    (string.IsNullOrEmpty(p.IdOrden) && EsPagoDeFactura(p.ReferenciaBancaria, venta.NroFactura)))
                        .ToList();
                }
                else
                {
                    // 4. Si NO encontramos la venta, pero hay pagos correspondientes al query (ej. orden o referencia),
                    // traemos esos pagos directamente para mostrar la orden virtual.
                    var todosPagos = await context.PagosLiquidacionesCashea.ToListAsync();
                    pagos = todosPagos
                        .Where(p => p.IdOrden == idOrden || p.ReferenciaBancaria == idOrden)
                        .ToList();
                }

                if (venta == null && !pagos.Any())
                {
                    return null;
                }

                var banco = await context.MovimientosBancarios.ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                List<string> listaDetalles = new();
                decimal totalAbonosUsd = 0;
                var refPagosProcesados = new HashSet<string>();

                foreach (var pago in pagos)
                {
                    var m = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == pago.ReferenciaBancaria.Trim());
                    if (m != null)
                    {
                        decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                        decimal abonoUsd = m.MontoAbonadoVes / tasa;
                        totalAbonosUsd += abonoUsd;
                        refPagosProcesados.Add(pago.ReferenciaBancaria.Trim());

                        listaDetalles.Add($"Ref: {m.ReferenciaBanco.Trim()} ({m.Fecha:dd/MM/yyyy}) - {TruncarA3Decimales(abonoUsd):N3} $");
                    }
                    else
                    {
                        listaDetalles.Add($"Ref: {pago.ReferenciaBancaria.Trim()} (No en Banco) - {TruncarA3Decimales(pago.TotalDepositadoUsd):N3} $");
                    }
                }

                // Buscar abono inicial (inicial / pagado en caja) registrado en banco por NroFactura
                if (venta != null && venta.PagadoCajaUsd > 0)
                {
                    var movsIniciales = banco
                        .Where(mb => mb.ReferenciaBanco != null && 
                                     EsPagoDeFactura(mb.ReferenciaBanco, venta.NroFactura) && 
                                     !refPagosProcesados.Contains(mb.ReferenciaBanco.Trim()))
                        .ToList();

                    foreach (var m in movsIniciales)
                    {
                        decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                        decimal abonoUsd = m.MontoAbonadoVes / tasa;
                        totalAbonosUsd += abonoUsd;
                        refPagosProcesados.Add(m.ReferenciaBanco.Trim());

                        listaDetalles.Add($"Ref: {m.ReferenciaBanco.Trim()} ({m.Fecha:dd/MM/yyyy}) [Inicial] - {TruncarA3Decimales(abonoUsd):N3} $");
                    }
                }

                string detalleAbonos = listaDetalles.Any() ? string.Join(" | ", listaDetalles) : "Ninguno";

                var pagosEnBanco = pagos
                    .Where(p => banco.Any(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == p.ReferenciaBancaria.Trim()))
                    .ToList();

                int maxCuotaNo = pagos.Any() ? pagos.Max(p => p.NroCuotaPagada) : 0;
                int cantPagos = pagosEnBanco.Count;
                int cuotasPagadas = Math.Max(maxCuotaNo, cantPagos);

                bool cuota1Pagada = cuotasPagadas >= 1;
                bool cuota2Pagada = cuotasPagadas >= 2;
                bool cuota3Pagada = cuotasPagadas >= 3;

                int cuotasRestantes = 0;
                decimal montoDebe = 0;

                if (venta != null)
                {
                    if (!cuota1Pagada) { cuotasRestantes++; montoDebe += venta.MontoCuota1; }
                    if (!cuota2Pagada) { cuotasRestantes++; montoDebe += venta.MontoCuota2; }
                    if (!cuota3Pagada) { cuotasRestantes++; montoDebe += venta.MontoCuota3; }

                    return new
                    {
                        Orden = venta.IdOrden,
                        Factura = venta.NroFactura,
                        Sucursal = venta.Sucursal,
                        Fecha = venta.FechaCompra.ToString("dd/MM/yyyy"),
                        Venta_Total_USD = TruncarA3Decimales(venta.VentaTotalUsd),
                        Pagado_Caja_USD = TruncarA3Decimales(venta.PagadoCajaUsd),
                        Monto_Financiado_USD = TruncarA3Decimales(venta.MontoFinanciado),
                        Estatus_Orden = venta.Estatus,
                        Abonos_Detectados_USD = TruncarA3Decimales(totalAbonosUsd),
                        Monto_Debe_USD = TruncarA3Decimales(montoDebe),
                        Cuotas_Restantes = cuotasRestantes,
                        Detalle_Abonos = detalleAbonos
                    };
                }
                else
                {
                    var primerPago = pagos.FirstOrDefault();
                    string ordenId = primerPago?.IdOrden ?? idOrden;
                    decimal financiadoEst = pagos.Sum(p => p.MontoFinanciado);
                    if (financiadoEst == 0) financiadoEst = pagos.Sum(p => p.TotalDepositadoUsd);

                    decimal cuotaEst = financiadoEst / 3m;
                    if (!cuota1Pagada) { cuotasRestantes++; montoDebe += cuotaEst; }
                    if (!cuota2Pagada) { cuotasRestantes++; montoDebe += cuotaEst; }
                    if (!cuota3Pagada) { cuotasRestantes++; montoDebe += cuotaEst; }

                    return new
                    {
                        Orden = ordenId,
                        Factura = "Sin Info Ventas",
                        Sucursal = "Desconocida (Sin Venta)",
                        Fecha = primerPago?.FechaLiquidacion.ToString("dd/MM/yyyy") ?? "N/A",
                        Venta_Total_USD = TruncarA3Decimales(financiadoEst),
                        Pagado_Caja_USD = 0m,
                        Monto_Financiado_USD = TruncarA3Decimales(financiadoEst),
                        Estatus_Orden = "VERIFICAR (Sin Venta)",
                        Abonos_Detectados_USD = TruncarA3Decimales(totalAbonosUsd),
                        Monto_Debe_USD = TruncarA3Decimales(montoDebe),
                        Cuotas_Restantes = cuotasRestantes,
                        Detalle_Abonos = detalleAbonos
                    };
                }
            }
        }

        // BÚSQUEDA POR RANGO DE FECHAS: Detalle de órdenes en rango
        public async Task<ReporteRangoFechasResult> ConsultarOrdenesPorRangoFechasAsync(DateTime desde, DateTime hasta)
        {
            using (var context = new SurtidorDbContext())
            {
                var fechaDesde = desde.Date;
                var fechaHasta = hasta.Date.AddDays(1).AddTicks(-1);

                // 1. Buscamos todas las ventas en el rango de fecha de compra
                var ventasEnRango = await context.VentasIndividualesCashea
                    .Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra <= fechaHasta)
                    .ToListAsync();

                // 2. Buscamos todos los pagos en el rango de fecha de liquidación
                var pagosEnRango = await context.PagosLiquidacionesCashea
                    .Where(p => p.FechaLiquidacion >= fechaDesde && p.FechaLiquidacion <= fechaHasta)
                    .ToListAsync();

                var todosPagos = await context.PagosLiquidacionesCashea.Where(p => p.FechaLiquidacion <= fechaHasta).ToListAsync();
                var banco = await context.MovimientosBancarios.Where(m => m.Fecha <= fechaHasta).ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                var todasVentas = await context.VentasIndividualesCashea.ToListAsync();

                // 3. Obtenemos las ventas que ocurrieron en el rango de compra
                var idOrdenesConVentas = ventasEnRango.Select(v => v.IdOrden).ToHashSet();
                var listVentasUnicas = new List<VentaIndividualCashea>(ventasEnRango);

                var listOrdenes = new List<dynamic>();

                // 4. Procesamos cada venta única encontrada
                foreach (var venta in listVentasUnicas)
                {
                    var pagosDeOrden = todosPagos
                        .Where(p => p.IdOrden == venta.IdOrden || 
                                    (string.IsNullOrEmpty(p.IdOrden) && EsPagoDeFactura(p.ReferenciaBancaria, venta.NroFactura)))
                        .ToList();

                    List<string> listaDetalles = new();
                    decimal totalAbonosUsd = 0;
                    var refPagosProcesados = new HashSet<string>();

                    foreach (var pago in pagosDeOrden)
                    {
                        var m = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == pago.ReferenciaBancaria.Trim());
                        if (m != null)
                        {
                            decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                            decimal abonoUsd = m.MontoAbonadoVes / tasa;
                            totalAbonosUsd += abonoUsd;
                            refPagosProcesados.Add(pago.ReferenciaBancaria.Trim());

                            listaDetalles.Add($"Ref: {m.ReferenciaBanco.Trim()} ({m.Fecha:dd/MM/yyyy}) - {TruncarA3Decimales(abonoUsd):N3} $");
                        }
                        else
                        {
                            listaDetalles.Add($"Ref: {pago.ReferenciaBancaria.Trim()} (No en Banco) - {TruncarA3Decimales(pago.TotalDepositadoUsd):N3} $");
                        }
                    }

                    // Buscar abono inicial (inicial / pagado en caja) registrado en banco por NroFactura
                    if (venta != null && venta.PagadoCajaUsd > 0)
                    {
                        var movsIniciales = banco
                            .Where(mb => mb.ReferenciaBanco != null && 
                                         EsPagoDeFactura(mb.ReferenciaBanco, venta.NroFactura) && 
                                         !refPagosProcesados.Contains(mb.ReferenciaBanco.Trim()))
                            .ToList();

                        foreach (var m in movsIniciales)
                        {
                            decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                            decimal abonoUsd = m.MontoAbonadoVes / tasa;
                            totalAbonosUsd += abonoUsd;
                            refPagosProcesados.Add(m.ReferenciaBanco.Trim());

                            listaDetalles.Add($"Ref: {m.ReferenciaBanco.Trim()} ({m.Fecha:dd/MM/yyyy}) [Inicial] - {TruncarA3Decimales(abonoUsd):N3} $");
                        }
                    }

                    string detalleAbonos = listaDetalles.Any() ? string.Join(" | ", listaDetalles) : "Ninguno";

                    var pagosEnBanco = pagosDeOrden
                        .Where(p => banco.Any(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == p.ReferenciaBancaria.Trim()))
                        .ToList();

                    int maxCuotaNo = pagosDeOrden.Any() ? pagosDeOrden.Max(p => p.NroCuotaPagada) : 0;
                    int cantPagos = pagosEnBanco.Count;
                    int cuotasPagadas = Math.Max(maxCuotaNo, cantPagos);

                    bool cuota1Pagada = cuotasPagadas >= 1;
                    bool cuota2Pagada = cuotasPagadas >= 2;
                    bool cuota3Pagada = cuotasPagadas >= 3;

                    int cuotasRestantes = 0;
                    decimal montoDebe = 0;

                    if (!cuota1Pagada) { cuotasRestantes++; montoDebe += venta.MontoCuota1; }
                    if (!cuota2Pagada) { cuotasRestantes++; montoDebe += venta.MontoCuota2; }
                    if (!cuota3Pagada) { cuotasRestantes++; montoDebe += venta.MontoCuota3; }

                    listOrdenes.Add(new
                    {
                        Orden = venta.IdOrden,
                        Factura = venta.NroFactura,
                        Sucursal = venta.Sucursal,
                        Fecha = venta.FechaCompra.ToString("dd/MM/yyyy"),
                        Venta_Total_USD = TruncarA3Decimales(venta.VentaTotalUsd),
                        Pagado_Caja_USD = TruncarA3Decimales(venta.PagadoCajaUsd),
                        Monto_Financiado_USD = TruncarA3Decimales(venta.MontoFinanciado),
                        Estatus_Orden = venta.Estatus,
                        Abonos_Detectados_USD = TruncarA3Decimales(totalAbonosUsd),
                        Monto_Debe_USD = TruncarA3Decimales(montoDebe),
                        Cuotas_Restantes = cuotasRestantes,
                        Detalle_Abonos = detalleAbonos
                    });
                }

                // 5. Agregamos también "órdenes virtuales" para aquellos pagos en rango que no tienen una venta en la base de datos
                var idOrdenesConVentasOVirtuales = listVentasUnicas.Select(v => v.IdOrden).ToHashSet();
                foreach (var pago in pagosEnRango)
                {
                    string ordenId = pago.IdOrden;
                    if (string.IsNullOrEmpty(ordenId)) continue; 

                    if (!idOrdenesConVentasOVirtuales.Contains(ordenId))
                    {
                        // Si la venta existe en la base de datos pero pertenece a otro período, no la agregamos como virtual
                        var ventaExisteEnOtroPeriodo = todasVentas.Any(v => v.IdOrden == ordenId);
                        if (ventaExisteEnOtroPeriodo) continue;

                        idOrdenesConVentasOVirtuales.Add(ordenId);

                        var pagosDeOrden = todosPagos.Where(p => p.IdOrden == ordenId).ToList();

                        List<string> listaDetalles = new();
                        decimal totalAbonosUsd = 0;

                        foreach (var p in pagosDeOrden)
                        {
                            var m = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == p.ReferenciaBancaria.Trim());
                            if (m != null)
                            {
                                decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                                decimal abonoUsd = m.MontoAbonadoVes / tasa;
                                totalAbonosUsd += abonoUsd;

                                listaDetalles.Add($"Ref: {m.ReferenciaBanco.Trim()} ({m.Fecha:dd/MM/yyyy}) - {TruncarA3Decimales(abonoUsd):N3} $");
                            }
                            else
                            {
                                listaDetalles.Add($"Ref: {p.ReferenciaBancaria.Trim()} (No en Banco) - {TruncarA3Decimales(p.TotalDepositadoUsd):N3} $");
                            }
                        }

                        string detalleAbonos = listaDetalles.Any() ? string.Join(" | ", listaDetalles) : "Ninguno";

                        var pagosEnBanco = pagosDeOrden
                            .Where(p => banco.Any(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == p.ReferenciaBancaria.Trim()))
                            .ToList();

                        int maxCuotaNo = pagosDeOrden.Any() ? pagosDeOrden.Max(p => p.NroCuotaPagada) : 0;
                        int cantPagos = pagosEnBanco.Count;
                        int cuotasPagadas = Math.Max(maxCuotaNo, cantPagos);

                        bool cuota1Pagada = cuotasPagadas >= 1;
                        bool cuota2Pagada = cuotasPagadas >= 2;
                        bool cuota3Pagada = cuotasPagadas >= 3;

                        int cuotasRestantes = 0;
                        decimal montoDebe = 0;
                        decimal financiadoEst = pagosDeOrden.Sum(p => p.MontoFinanciado);
                        if (financiadoEst == 0) financiadoEst = pagosDeOrden.Sum(p => p.TotalDepositadoUsd);

                        decimal cuotaEst = financiadoEst / 3m;
                        if (!cuota1Pagada) { cuotasRestantes++; montoDebe += cuotaEst; }
                        if (!cuota2Pagada) { cuotasRestantes++; montoDebe += cuotaEst; }
                        if (!cuota3Pagada) { cuotasRestantes++; montoDebe += cuotaEst; }

                        listOrdenes.Add(new
                        {
                            Orden = ordenId,
                            Factura = "Sin Info Ventas",
                            Sucursal = "Desconocida (Sin Venta)",
                            Fecha = pago.FechaLiquidacion.ToString("dd/MM/yyyy"),
                            Venta_Total_USD = TruncarA3Decimales(financiadoEst),
                            Pagado_Caja_USD = 0m,
                            Monto_Financiado_USD = TruncarA3Decimales(financiadoEst),
                            Estatus_Orden = "VERIFICAR (Sin Venta)",
                            Abonos_Detectados_USD = TruncarA3Decimales(totalAbonosUsd),
                            Monto_Debe_USD = TruncarA3Decimales(montoDebe),
                            Cuotas_Restantes = cuotasRestantes,
                            Detalle_Abonos = detalleAbonos
                        });
                    }
                }

                // 6. Ordenamos la lista final por Fecha
                listOrdenes = listOrdenes.OrderBy(o => DateTime.ParseExact(o.Fecha, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)).ToList();

                var result = new ReporteRangoFechasResult
                {
                    TotalVendido = TruncarA3Decimales(listVentasUnicas.Sum(v => v.VentaTotalUsd)),
                    TotalCaja = TruncarA3Decimales(listVentasUnicas.Sum(v => v.PagadoCajaUsd)),
                    TotalFinanciado = TruncarA3Decimales(listVentasUnicas.Sum(v => v.MontoFinanciado)),
                    Ordenes = listOrdenes
                };

                return result;
            }
        }

        // RESUMEN DE VENTAS: Agrupación por sucursal
        public async Task<List<dynamic>> ObtenerResumenVentasAsync(DateTime? desde = null, DateTime? hasta = null)
        {
            using (var context = new SurtidorDbContext())
            {
                var ventas = await context.VentasIndividualesCashea.ToListAsync();
                var banco = await context.MovimientosBancarios.ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                if (desde.HasValue && hasta.HasValue)
                {
                    var fechaDesde = desde.Value.Date;
                    var fechaHasta = hasta.Value.Date.AddDays(1).AddTicks(-1);

                    ventas = ventas.Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra <= fechaHasta).ToList();
                    banco = banco.Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta).ToList();
                }

                // Calcular total en banco en USD
                decimal totalEnBancoUsd = 0;
                foreach (var m in banco)
                {
                    decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                    totalEnBancoUsd += (m.MontoAbonadoVes / tasa);
                }

                var totalFinanciadoGlobal = ventas.Sum(v => v.MontoFinanciado);

                var resumen = ventas
                    .GroupBy(v => v.Sucursal)
                    .Select(g =>
                    {
                        var sucursal = g.Key ?? "Desconocida";
                        var totalFinanciadoUsd = g.Sum(v => v.MontoFinanciado);

                        // Prorrateo proporcional si hubiera múltiples sucursales
                        decimal bancoUsdSucursal = 0;
                        if (totalFinanciadoGlobal > 0)
                        {
                            bancoUsdSucursal = (totalFinanciadoUsd / totalFinanciadoGlobal) * totalEnBancoUsd;
                        }

                        var cuentasPorCobrar = totalFinanciadoUsd - bancoUsdSucursal;

                        return new
                        {
                            Sucursal = sucursal,
                            Total_Ordenes = g.Count(),
                            Total_Vendido_USD = TruncarA3Decimales(g.Sum(v => v.VentaTotalUsd)),
                            Total_Pagado_Caja_USD = TruncarA3Decimales(g.Sum(v => v.PagadoCajaUsd)),
                            Total_Financiado_USD = TruncarA3Decimales(totalFinanciadoUsd),
                            Cuentas_Por_Cobrar_USD = TruncarA3Decimales(cuentasPorCobrar)
                        };
                    })
                    .Cast<dynamic>()
                    .ToList();

                return resumen;
            }
        }

        private decimal TruncarA3Decimales(decimal valor)
        {
            return Math.Truncate(valor * 1000m) / 1000m;
        }

        private decimal ObtenerTasaFechaValor(DateTime fechaMovimiento, List<TasaDiaria> historicoTasas)
        {
            DateTime fechaBase = fechaMovimiento.Date;
            DateTime fechaBusqueda;

            if (fechaBase.DayOfWeek == DayOfWeek.Saturday)
                fechaBusqueda = fechaBase.AddDays(-2); // Sábado -> Jueves
            else if (fechaBase.DayOfWeek == DayOfWeek.Sunday)
                fechaBusqueda = fechaBase.AddDays(-3); // Domingo -> Jueves
            else
                fechaBusqueda = fechaBase.AddDays(-1); // Lunes a Viernes -> T-1

            var tasaAplicable = historicoTasas
                .Where(t => t.Fecha.Date <= fechaBusqueda.Date)
                .OrderByDescending(t => t.Fecha)
                .FirstOrDefault();

            if (tasaAplicable != null && tasaAplicable.TasaBcvVes > 0)
            {
                return tasaAplicable.TasaBcvVes;
            }

            // Fallback en caso extremo
            var fallback = historicoTasas.FirstOrDefault(t => t.Fecha.Date == fechaBase);
            return (fallback != null && fallback.TasaBcvVes > 0) ? fallback.TasaBcvVes : 1m;
        }

        // ESTADO DE CONCILIACIÓN: Conseguidas, No Conseguidas y Sobrantes en Banco
        public async Task<EstadoConciliacionResult> ObtenerEstadoConciliacionAsync(DateTime? desde = null, DateTime? hasta = null)
        {
            var motor = new MotorConciliacionService();
            var conciliados = await motor.EjecutarConciliacionAsync();

            using (var context = new SurtidorDbContext())
            {
                var todosBanco = await context.MovimientosBancarios.ToListAsync();

                if (desde.HasValue && hasta.HasValue)
                {
                    var fechaDesde = desde.Value.Date;
                    var fechaHasta = hasta.Value.Date.AddDays(1).AddTicks(-1);

                    conciliados = conciliados.Where(c => c.Fecha >= fechaDesde && c.Fecha <= fechaHasta).ToList();
                    todosBanco = todosBanco.Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta).ToList();
                }

                var referenciasConciliadas = conciliados.Select(c => c.Referencia).ToHashSet();

                var sobrantes = todosBanco
                    .Where(m => !referenciasConciliadas.Contains((m.ReferenciaBanco ?? "").Trim()))
                    .Select(m => new
                    {
                        Fecha = m.Fecha.ToString("dd/MM/yyyy"),
                        Referencia = m.ReferenciaBanco,
                        m.Descripcion,
                        Monto_VES = m.MontoAbonadoVes
                    })
                    .Cast<dynamic>()
                    .ToList();

                var conseguidas = conciliados
                    .Where(c => c.Estatus == "Éxito")
                    .Cast<dynamic>()
                    .ToList();

                var noConseguidas = conciliados
                    .Where(c => c.Estatus != "Éxito")
                    .Cast<dynamic>()
                    .ToList();

                return new EstadoConciliacionResult
                {
                    Conseguidas = conseguidas,
                    NoConseguidas = noConseguidas,
                    Sobrantes = sobrantes
                };
            }
        }

        // REPORTE BANCO POR RANGO: Datos del banco filtrados por fecha de compra de la orden
        public async Task<ReporteBancoRangoResult> ObtenerReporteBancoRangoAsync(DateTime desde, DateTime hasta)
        {
            using (var context = new SurtidorDbContext())
            {
                var fechaDesde = desde.Date;
                var fechaHasta = hasta.Date.AddDays(1).AddTicks(-1);

                // 1. Obtener todas las ventas en el rango
                var ventasEnRango = await context.VentasIndividualesCashea
                    .Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra <= fechaHasta)
                    .ToListAsync();

                var todosPagos = await context.PagosLiquidacionesCashea.Where(p => p.FechaLiquidacion <= fechaHasta).ToListAsync();
                var banco = await context.MovimientosBancarios.Where(m => m.Fecha <= fechaHasta).ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                var report = new List<dynamic>();

                foreach (var venta in ventasEnRango)
                {
                    // Obtener pagos de esta orden
                    var pagosDeOrden = todosPagos
                        .Where(p => p.IdOrden == venta.IdOrden || 
                                    (string.IsNullOrEmpty(p.IdOrden) && EsPagoDeFactura(p.ReferenciaBancaria, venta.NroFactura)))
                        .ToList();

                    decimal totalMontoBancoVes = 0;
                    decimal totalMontoBancoUsd = 0;
                    var refsConciliadas = new List<string>();
                    var refsNoBanco = new List<string>();

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
                            refsConciliadas.Add(pago.ReferenciaBancaria.Trim());
                            cantPagosEnBanco++;
                        }
                        else
                        {
                            refsNoBanco.Add(pago.ReferenciaBancaria.Trim());
                        }
                    }

                    // Cuotas pagadas (el máximo entre lo reportado por Cashea y lo encontrado en banco)
                    int cuotasPagadas = Math.Max(maxCuotaNo, cantPagosEnBanco);
                    string estadoPago = cuotasPagadas >= 3 ? "Totalmente Pagada" : (cuotasPagadas == 0 ? "No Pagada" : $"Parcial ({cuotasPagadas}/3)");

                    decimal esperadoBancoVes = pagosDeOrden.Sum(p => p.TotalDepositadoBs);
                    decimal montoDebe = 0;

                    bool cuota1Pagada = cuotasPagadas >= 1;
                    bool cuota2Pagada = cuotasPagadas >= 2;
                    bool cuota3Pagada = cuotasPagadas >= 3;

                    if (!cuota1Pagada) { montoDebe += venta.MontoCuota1; }
                    if (!cuota2Pagada) { montoDebe += venta.MontoCuota2; }
                    if (!cuota3Pagada) { montoDebe += venta.MontoCuota3; }

                    report.Add(new
                    {
                        Orden = venta.IdOrden,
                        Factura = venta.NroFactura,
                        Fecha_Compra = venta.FechaCompra.ToString("dd/MM/yyyy"),
                        Venta_Total_USD = TruncarA3Decimales(venta.VentaTotalUsd),
                        Monto_Financiado_USD = TruncarA3Decimales(venta.MontoFinanciado),
                        Monto_En_Banco_VES = TruncarA3Decimales(totalMontoBancoVes),
                        Monto_En_Banco_USD = TruncarA3Decimales(totalMontoBancoUsd),
                        Monto_Debe_USD = TruncarA3Decimales(montoDebe),
                        Esperado_Banco_VES = TruncarA3Decimales(esperadoBancoVes),
                        Cuotas_Pagadas = $"{cuotasPagadas}/3",
                        Estado_Banco = estadoPago,
                        Fecha_Cuota1 = venta.FechaCuota1?.ToString("dd/MM/yyyy") ?? "N/A",
                        Fecha_Cuota2 = venta.FechaCuota2?.ToString("dd/MM/yyyy") ?? "N/A",
                        Fecha_Cuota3 = venta.FechaCuota3?.ToString("dd/MM/yyyy") ?? "N/A"
                    });
                }

                // Calcular saldo en banco bruto recibidos durante el periodo [fechaDesde, fechaHasta]
                decimal saldoBancoBs = 0;
                decimal saldoBancoUsd = 0;

                var bancoEnPeriodo = banco.Where(m => m.Fecha >= fechaDesde && m.Fecha <= fechaHasta).ToList();
                foreach (var m in bancoEnPeriodo)
                {
                    var refBanco = (m.ReferenciaBanco ?? "").Trim();
                    var matchesPago = todosPagos.Any(p => p.ReferenciaBancaria != null && p.ReferenciaBancaria.Trim() == refBanco);
                    if (matchesPago)
                    {
                        decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                        saldoBancoBs += m.MontoAbonadoVes;
                        saldoBancoUsd += (m.MontoAbonadoVes / tasa);
                    }
                }

                return new ReporteBancoRangoResult
                {
                    Items = report,
                    SaldoBancoBs = TruncarA3Decimales(saldoBancoBs),
                    SaldoBancoUsd = TruncarA3Decimales(saldoBancoUsd)
                };
            }
        }

        public async Task<decimal> CalcularDebeCasheaPorVencimientoAsync(DateTime desde, DateTime hasta)
        {
            using (var context = new SurtidorDbContext())
            {
                var fechaDesde = desde.Date;
                var fechaHasta = hasta.Date.AddDays(1).AddTicks(-1);

                var ventas = await context.VentasIndividualesCashea.ToListAsync();
                var todosPagos = await context.PagosLiquidacionesCashea.Where(p => p.FechaLiquidacion <= fechaHasta).ToListAsync();
                var banco = await context.MovimientosBancarios.Where(m => m.Fecha <= fechaHasta).ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                decimal totalPendienteVencido = 0;

                foreach (var venta in ventas)
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
                        totalPendienteVencido += venta.MontoCuota1;
                    }
                    if (!cuota2Pagada && venta.FechaCuota2.HasValue && venta.FechaCuota2.Value >= fechaDesde && venta.FechaCuota2.Value <= fechaHasta)
                    {
                        totalPendienteVencido += venta.MontoCuota2;
                    }
                    if (!cuota3Pagada && venta.FechaCuota3.HasValue && venta.FechaCuota3.Value >= fechaDesde && venta.FechaCuota3.Value <= fechaHasta)
                    {
                        totalPendienteVencido += venta.MontoCuota3;
                    }
                }

                return TruncarA3Decimales(totalPendienteVencido);
            }
        }
    }
}