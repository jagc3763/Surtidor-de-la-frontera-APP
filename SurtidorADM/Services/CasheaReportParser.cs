using ClosedXML.Excel;
using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;

namespace SurtidorADM.Services
{
    public class CasheaReporteDatos
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public decimal VentasTotales { get; set; }
        public decimal PagadoCaja { get; set; }
        public decimal MontoFinanciado { get; set; }
        public decimal RecibidoBanco { get; set; }
        public decimal CuotasAdelantadas { get; set; }
        public decimal PagoInicialApp { get; set; }
        public decimal BancoNeto { get; set; }
        public decimal CuentasPorCobrar { get; set; }
    }

    public class CasheaReportParser
    {
        public CasheaReporteDatos ParsearReporteMensual(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("El archivo del reporte no existe.", filePath);

            string tempFile = Path.Combine(Path.GetTempPath(), "temp_cashea_report_" + Guid.NewGuid() + ".xlsx");
            File.Copy(filePath, tempFile, true);

            try
            {
                CleanExcelStylesXml(tempFile);

                using (var workbook = new XLWorkbook(tempFile))
                {
                    var ws = workbook.Worksheet("Reporte Mensual");
                    if (ws == null)
                        throw new Exception("No se encontró la hoja 'Reporte Mensual' en el archivo.");

                    var datos = new CasheaReporteDatos();

                    // 1. Buscar periodo de fechas (usualmente en la columna B, fila 8 o similar)
                    for (int r = 1; r <= 30; r++)
                    {
                        string text = ws.Cell(r, 2).Value.ToString().Trim();
                        if (text.Contains("Período:", StringComparison.OrdinalIgnoreCase) || text.Contains("Periodo:", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(text, @"Del\s+([0-9\-/]+)\s+Hasta\s+([0-9\-/]+)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string desdeStr = match.Groups[1].Value.Trim();
                                string hastaStr = match.Groups[2].Value.Trim();

                                string[] formats = { "dd/MM/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy" };
                                if (DateTime.TryParseExact(desdeStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime desdeVal))
                                {
                                    datos.FechaDesde = desdeVal.Date;
                                }
                                if (DateTime.TryParseExact(hastaStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime hastaVal))
                                {
                                    datos.FechaHasta = hastaVal.Date;
                                }
                            }
                            break;
                        }
                    }

                    // 2. Buscar las etiquetas financieras
                    int lastRow = ws.LastRowUsed()?.RowNumber() ?? 100;
                    for (int r = 1; r <= lastRow; r++)
                    {
                        string label = ws.Cell(r, 2).Value.ToString().Trim();
                        if (string.IsNullOrEmpty(label)) continue;

                        decimal val = 0;
                        string valStr = ws.Cell(r, 4).Value.ToString().Trim();
                        decimal.TryParse(valStr.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out val);

                        if (label.Contains("Ventas Totales", StringComparison.OrdinalIgnoreCase))
                        {
                            datos.VentasTotales = val;
                        }
                        else if (label.Contains("Pagado en Caja", StringComparison.OrdinalIgnoreCase) ||
                                 (label.Contains("Pagado", StringComparison.OrdinalIgnoreCase) && label.Contains("Caja", StringComparison.OrdinalIgnoreCase)))
                        {
                            datos.PagadoCaja = val;
                        }
                        else if (label.Contains("Monto Financiado", StringComparison.OrdinalIgnoreCase))
                        {
                            datos.MontoFinanciado = val;
                        }
                        else if (label.Contains("Recibido en Banco", StringComparison.OrdinalIgnoreCase) ||
                                 label.Equals("Recibido en Banco", StringComparison.OrdinalIgnoreCase))
                        {
                            datos.RecibidoBanco = val;
                        }
                        else if (label.Contains("Cuotas adelantadas", StringComparison.OrdinalIgnoreCase))
                        {
                            datos.CuotasAdelantadas = val;
                        }
                        else if (label.Contains("Pago inicial de clientes en App", StringComparison.OrdinalIgnoreCase) ||
                                 (label.Contains("Pago inicial", StringComparison.OrdinalIgnoreCase) && label.Contains("App", StringComparison.OrdinalIgnoreCase)))
                        {
                            datos.PagoInicialApp = val;
                        }
                        else if (label.Contains("Banco neto", StringComparison.OrdinalIgnoreCase) && label.Contains("corte", StringComparison.OrdinalIgnoreCase))
                        {
                            datos.BancoNeto = val;
                        }
                        else if (label.Equals("Cuentas por Cobrar", StringComparison.OrdinalIgnoreCase))
                        {
                            if (datos.CuentasPorCobrar == 0)
                            {
                                datos.CuentasPorCobrar = val;
                            }
                        }
                    }

                    return datos;
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch { /* Ignore deletion errors */ }
            }
        }

        private void CleanExcelStylesXml(string filePath)
        {
            try
            {
                using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Update))
                {
                    var stylesEntry = archive.GetEntry("xl/styles.xml");
                    if (stylesEntry == null) return;

                    string xmlContent;
                    using (var reader = new StreamReader(stylesEntry.Open()))
                    {
                        xmlContent = reader.ReadToEnd();
                    }

                    var doc = new XmlDocument();
                    doc.LoadXml(xmlContent);

                    var nsmgr = new XmlNamespaceManager(doc.NameTable);
                    nsmgr.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

                    var numFmtsNode = doc.SelectSingleNode("//x:numFmts", nsmgr);
                    if (numFmtsNode != null)
                    {
                        var idSet = new HashSet<string>();
                        var nodesToRemove = new List<XmlNode>();

                        foreach (XmlNode child in numFmtsNode.ChildNodes)
                        {
                            if (child.Attributes != null)
                            {
                                var attr = child.Attributes["numFmtId"];
                                if (attr != null)
                                {
                                    string id = attr.Value;
                                    if (idSet.Contains(id))
                                    {
                                        nodesToRemove.Add(child);
                                    }
                                    else
                                    {
                                        idSet.Add(id);
                                    }
                                }
                            }
                        }

                        if (nodesToRemove.Count > 0)
                        {
                            foreach (var node in nodesToRemove)
                            {
                                numFmtsNode.RemoveChild(node);
                            }

                            var countAttr = numFmtsNode.Attributes["count"];
                            if (countAttr != null)
                            {
                                countAttr.Value = numFmtsNode.ChildNodes.Count.ToString();
                            }

                            stylesEntry.Delete();
                            var newEntry = archive.CreateEntry("xl/styles.xml");
                            using (var writer = new StreamWriter(newEntry.Open()))
                            {
                                doc.Save(writer);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Non-blocking fallback
            }
        }
    }
}
