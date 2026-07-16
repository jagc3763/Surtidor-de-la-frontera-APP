using HtmlAgilityPack;
using SurtidorADM.Data;
using SurtidorADM.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SurtidorADM.Services
{
    public class TipoCambioService
    {
        private const string UrlBcv = "https://www.bcv.org.ve/";

        /// <summary>
        /// Realiza el web scraping al portal del BCV para extraer la tasa oficial del USD.
        /// </summary>
        public async Task<decimal> ObtenerTasaBcvAsync()
        {
            try
            {
                // Configurar HttpClient para ignorar problemas estrictos de certificados si los hubiera
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };

                using var client = new HttpClient(handler);
                // El BCV a veces bloquea peticiones sin User-Agent común
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                string html = await client.GetStringAsync(UrlBcv);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Selector XPath basado en el contenedor de la tasa oficial del dólar
                var nodoDolar = doc.DocumentNode.SelectSingleNode("//div[@id='dolar']//strong[@class='strong-tb']");

                if (nodoDolar == null)
                {
                    // Selector alternativo en caso de cambios ligeros en el DOM
                    nodoDolar = doc.DocumentNode.SelectSingleNode("//strong[@class='strong-tb']");
                }

                if (nodoDolar != null)
                {
                    string valorTexto = nodoDolar.InnerText.Trim();

                    // Reemplazar coma por punto para asegurar la conversión a decimal limpia
                    valorTexto = valorTexto.Replace(",", ".");

                    if (decimal.TryParse(valorTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal tasa))
                    {
                        return tasa;
                    }
                }

                throw new Exception("No se pudo parsear el formato numérico de la tasa en el HTML.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al extraer la tasa del BCV: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Guarda o actualiza las tasas (BCV y Peso transaccional) para la fecha seleccionada.
        /// </summary>
        public async Task GuardarTasaDiariaAsync(DateTime fecha, decimal tasaBcv, decimal tasaCop)
        {
            using var context = new SurtidorDbContext();

            // Validar si ya existe un registro para ese día (solo la parte de la fecha)
            var fechaSolo = fecha.Date;
            var tasaExistente = context.TasasDiarias
                .FirstOrDefault(t => t.Fecha == fechaSolo);

            if (tasaExistente != null)
            {
                // Actualizar
                tasaExistente.TasaBcvVes = tasaBcv;
                tasaExistente.TasaFronteraCop = tasaCop;
            }
            else
            {
                // Crear nuevo
                var nuevaTasa = new TasaDiaria
                {
                    Fecha = fechaSolo,
                    TasaBcvVes = tasaBcv,
                    TasaFronteraCop = tasaCop
                };
                context.TasasDiarias.Add(nuevaTasa);
            }

            await context.SaveChangesAsync();
        }
    }
}