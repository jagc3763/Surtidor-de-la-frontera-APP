using ClosedXML.Excel;
using SurtidorADM.Data;
using SurtidorADM.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SurtidorADM.Services
{
    public class ImportadorHistoricoBcv
    {
        public async Task ImportarArchivoXlsxAsync(string rutaArchivo)
        {
            using var context = new SurtidorDbContext();

            // Abrir el archivo .xlsx moderno con ClosedXML
            using var workbook = new XLWorkbook(rutaArchivo);

            // Iterar por todas las hojas (días) del archivo
            foreach (var hoja in workbook.Worksheets)
            {
                string nombreHoja = hoja.Name.Trim();

                // Validar que la hoja sea un día válido (ej. "02012026")
                if (!DateTime.TryParseExact(nombreHoja, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaAplica))
                {
                    continue; // Si hay una hoja de "Resumen", la ignora y sigue
                }

                // Apuntar directamente a la celda G15
                string valorCelda = hoja.Cell("G15").GetString();

                // Normalizar coma por punto
                valorCelda = valorCelda.Replace(",", ".");

                if (decimal.TryParse(valorCelda, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal tasaUsd))
                {
                    var fechaSolo = fechaAplica.Date;
                    var tasaExistente = context.TasasDiarias.FirstOrDefault(t => t.Fecha == fechaSolo);

                    if (tasaExistente != null)
                    {
                        // Si por error importas el mismo archivo dos veces, solo actualiza
                        tasaExistente.TasaBcvVes = tasaUsd;
                    }
                    else
                    {
                        // Insertar nuevo día histórico
                        context.TasasDiarias.Add(new TasaDiaria
                        {
                            Fecha = fechaSolo,
                            TasaBcvVes = tasaUsd,
                            TasaFronteraCop = 0 // El COP lo dejarás en 0 o lo actualizarás manualmente si hubo operaciones
                        });
                    }
                }
            }

            // Guardar masivamente todos los días en la base de datos
            await context.SaveChangesAsync();
        }
    }
}