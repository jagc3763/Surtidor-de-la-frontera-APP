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

    public class DashboardService
    {
        // AUDITORÍA: Calcula USD usando la tasa histórica de la "Fecha de Valor"
        public async Task<dynamic> ObtenerAuditoriaFinancieraAsync()
        {
            using (var context = new SurtidorDbContext())
            {
                // Traemos los datos a memoria para evitar errores de compilación con EF Core
                var ventas = await context.VentasIndividualesCashea.ToListAsync();
                var pagos = await context.PagosLiquidacionesCashea.ToListAsync();
                var banco = await context.MovimientosBancarios.ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

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

            string r = refBancaria.Trim();
            string f = nroFactura.Trim();

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

                var todosPagos = await context.PagosLiquidacionesCashea.ToListAsync();
                var banco = await context.MovimientosBancarios.ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                var todasVentas = await context.VentasIndividualesCashea.ToListAsync();

                // 3. Obtenemos todas las ventas únicas que ocurrieron en el rango de compra,
                // o bien que tuvieron un pago en el rango de liquidación.
                var idOrdenesConVentas = ventasEnRango.Select(v => v.IdOrden).ToHashSet();
                var listVentasUnicas = new List<VentaIndividualCashea>(ventasEnRango);

                foreach (var pago in pagosEnRango)
                {
                    if (string.IsNullOrEmpty(pago.IdOrden))
                    {
                        // Buscamos en memoria
                        var ventaAsociada = todasVentas
                            .FirstOrDefault(v => !string.IsNullOrEmpty(v.NroFactura) && 
                                                 EsPagoDeFactura(pago.ReferenciaBancaria, v.NroFactura));

                        if (ventaAsociada != null && !idOrdenesConVentas.Contains(ventaAsociada.IdOrden))
                        {
                            idOrdenesConVentas.Add(ventaAsociada.IdOrden);
                            listVentasUnicas.Add(ventaAsociada);
                        }
                    }
                    else
                    {
                        var ventaAsociada = todasVentas.FirstOrDefault(v => v.IdOrden == pago.IdOrden);
                        if (ventaAsociada != null && !idOrdenesConVentas.Contains(ventaAsociada.IdOrden))
                        {
                            idOrdenesConVentas.Add(ventaAsociada.IdOrden);
                            listVentasUnicas.Add(ventaAsociada);
                        }
                    }
                }

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
        public async Task<List<dynamic>> ObtenerResumenVentasAsync()
        {
            using (var context = new SurtidorDbContext())
            {
                var ventas = await context.VentasIndividualesCashea.ToListAsync();
                var banco = await context.MovimientosBancarios.ToListAsync();
                var historicoTasas = await context.TasasDiarias.ToListAsync();

                // Calcular total en banco en USD (global)
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
        public async Task<EstadoConciliacionResult> ObtenerEstadoConciliacionAsync()
        {
            var motor = new MotorConciliacionService();
            var conciliados = await motor.EjecutarConciliacionAsync();

            using (var context = new SurtidorDbContext())
            {
                var todosBanco = await context.MovimientosBancarios.ToListAsync();
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
    }
}