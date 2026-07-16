using ClosedXML.Excel;
using SurtidorADM.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurtidorADM.Services
{
    public class ExcelParserService
    {

        // Asegúrate de que este método en ExcelParserService reciba la tasa sin límites
        private decimal LimpiarTasa(string valor)
        {
            // Reemplazamos coma por punto para asegurar el formato decimal
            string limpio = valor.Replace(",", ".").Trim();

            // Parseamos con máxima precisión
            if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res))
                return res; // Devolvemos el decimal exacto
            return 0m;
        }

        // Método seguro para obtener texto de una celda específica
        private string ObtenerTexto(IXLWorksheet ws, int fila, int col)
        {
            var celda = ws.Cell(fila, col);
            if (celda == null || celda.IsEmpty()) return string.Empty;

            // Si Excel tiene la celda como fecha, la convertimos a formato texto
            if (celda.DataType == XLDataType.DateTime)
                return celda.GetDateTime().ToString("dd/MM/yyyy HH:mm:ss");

            return celda.Value.ToString().Trim();
        }

        private DateTime ParsearFecha(string fechaTexto)
        {
            string[] formatos = { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(fechaTexto, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha))
                return fecha;
            return DateTime.MinValue;
        }

        private decimal LimpiarMonto(string valor)
        {
            // Elimina símbolos de moneda y ajusta comas por puntos
            string limpio = valor.Replace("Bs.", "").Replace("$", "").Replace("%", "").Replace(",", ".").Trim();
            if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res)) return res;
            return 0m;
        }

        private IXLWorksheet ObtenerHojaPorPalabrasClave(XLWorkbook workbook, string[] palabrasClave)
        {
            foreach (var ws in workbook.Worksheets)
            {
                string nombre = ws.Name.ToLower();
                foreach (var pc in palabrasClave)
                {
                    if (nombre.Contains(pc.ToLower()))
                    {
                        return ws;
                    }
                }
            }
            return workbook.Worksheet(1);
        }

        public List<PagoLiquidacionCashea> ParsearReportePagos(string rutaArchivo)
        {
            var pagos = new List<PagoLiquidacionCashea>();
            using (var workbook = new XLWorkbook(rutaArchivo))
            {
                var ws = ObtenerHojaPorPalabrasClave(workbook, new[] { "reporte cashea", "pago", "lote", "liquidaci" });
                int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;

                // Empezamos en la fila 2 para saltar encabezados
                for (int i = 2; i <= ultimaFila; i++)
                {
                    string refBancaria = ObtenerTexto(ws, i, 2);
                    string fechaStr = ObtenerTexto(ws, i, 1);

                    // Saltar filas vacías o de encabezado
                    if (string.IsNullOrEmpty(refBancaria) || 
                        refBancaria.Equals("Referencia", StringComparison.OrdinalIgnoreCase) || 
                        refBancaria.Equals("Referencia / Operación", StringComparison.OrdinalIgnoreCase) ||
                        fechaStr.Contains("Fecha", StringComparison.OrdinalIgnoreCase) ||
                        fechaStr.Contains("REPORTE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string cuotaStr = ObtenerTexto(ws, i, 8); // H=8 (Nro cuota pagada)
                    string idOrden = ObtenerTexto(ws, i, 10); // J=10 (Orden)

                    int.TryParse(cuotaStr, out int cuotaNo);

                    var pago = new PagoLiquidacionCashea
                    {
                        // El ID único lo formamos con Referencia + Fecha para evitar duplicados
                        IdPago = refBancaria + "_" + fechaStr,

                        FechaLiquidacion = ParsearFecha(fechaStr),
                        ReferenciaBancaria = refBancaria,

                        // Mapeo de montos
                        TotalDepositadoBs = LimpiarMonto(ObtenerTexto(ws, i, 3)),
                        TotalDepositadoUsd = LimpiarMonto(ObtenerTexto(ws, i, 5)),

                        Estado = ObtenerTexto(ws, i, 6), // Columna F (Status)
                        
                        // Nuevos campos
                        IdOrden = idOrden,
                        NroCuotaPagada = cuotaNo
                    };

                    pagos.Add(pago);
                }
            }
            return pagos;
        }

        public List<VentaIndividualCashea> ParsearReporteVentas(string rutaArchivo)
        {
            var ventas = new List<VentaIndividualCashea>();
            using (var workbook = new XLWorkbook(rutaArchivo))
            {
                var ws = ObtenerHojaPorPalabrasClave(workbook, new[] { "venta", "factura", "individual" });
                int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;

                for (int i = 2; i <= ultimaFila; i++)
                {
                    string idOrden = ObtenerTexto(ws, i, 1);
                    if (string.IsNullOrEmpty(idOrden) || 
                        idOrden.Equals("Orden", StringComparison.OrdinalIgnoreCase) || 
                        idOrden.Equals("Número de Orden", StringComparison.OrdinalIgnoreCase) || 
                        idOrden.Contains("REPORTE", StringComparison.OrdinalIgnoreCase) ||
                        idOrden.Contains("SISTEMA", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var venta = new VentaIndividualCashea
                    {
                        IdOrden = idOrden,
                        NroFactura = ObtenerTexto(ws, i, 2),
                        Sucursal = ObtenerTexto(ws, i, 3),
                        VentaTotalUsd = LimpiarMonto(ObtenerTexto(ws, i, 4)),
                        FechaCompra = ParsearFecha(ObtenerTexto(ws, i, 5)),
                        PagadoCajaUsd = LimpiarMonto(ObtenerTexto(ws, i, 6)),
                        MontoFinanciado = LimpiarMonto(ObtenerTexto(ws, i, 7)),
                        Estatus = ObtenerTexto(ws, i, 8),

                        // Cuotas
                        FechaCuota1 = ConvertirFechaNula(ObtenerTexto(ws, i, 11)),
                        MontoCuota1 = LimpiarMonto(ObtenerTexto(ws, i, 12)),
                        FechaCuota2 = ConvertirFechaNula(ObtenerTexto(ws, i, 13)),
                        MontoCuota2 = LimpiarMonto(ObtenerTexto(ws, i, 14)),
                        FechaCuota3 = ConvertirFechaNula(ObtenerTexto(ws, i, 15)),
                        MontoCuota3 = LimpiarMonto(ObtenerTexto(ws, i, 16))
                    };

                    ventas.Add(venta);
                }
            }
            return ventas;
        }

        // Helper para manejar fechas que pueden estar vacías
        private DateTime? ConvertirFechaNula(string fechaTexto)
        {
            var fecha = ParsearFecha(fechaTexto);
            return (fecha == DateTime.MinValue) ? (DateTime?)null : fecha;
        }

        public List<MovimientoBancario> ParsearReporteBanco(string rutaArchivo)
        {
            var movimientos = new List<MovimientoBancario>();

            using (var workbook = new XLWorkbook(rutaArchivo))
            {
                var ws = ObtenerHojaPorPalabrasClave(workbook, new[] { "extracto", "banco", "banesco", "movimiento" });
                int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;

                for (int i = 2; i <= ultimaFila; i++)
                {
                    string refBanco = ObtenerTexto(ws, i, 2);
                    string fechaStr = ObtenerTexto(ws, i, 1);

                    if (string.IsNullOrEmpty(refBanco) || 
                        refBanco.Equals("Referencia", StringComparison.OrdinalIgnoreCase) || 
                        refBanco.Equals("Referencia / Operación", StringComparison.OrdinalIgnoreCase) ||
                        fechaStr.Contains("Fecha", StringComparison.OrdinalIgnoreCase) ||
                        fechaStr.Contains("EXTRACTO", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Columna D es el índice 4
                    decimal monto = LimpiarMonto(ObtenerTexto(ws, i, 4));

                    // REGLA DE ORO: Solo procesamos montos mayores a 0 (Ignoramos comisiones/débitos)
                    if (monto > 0)
                    {
                        var mov = new MovimientoBancario
                        {
                            Fecha = ParsearFecha(fechaStr),               // Col A
                            ReferenciaBanco = refBanco,                   // Col B
                            Descripcion = ObtenerTexto(ws, i, 3),         // Col C
                            MontoAbonadoVes = monto                       // Col D
                        };

                        movimientos.Add(mov);
                    }
                }
            }
            return movimientos;
        }
    }
}