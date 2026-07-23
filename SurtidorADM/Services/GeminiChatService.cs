using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SurtidorADM.Data;
using SurtidorADM.Models;

namespace SurtidorADM.Services
{
    public class GeminiChatService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string ConfigFileName = "config_ia.json";

        public string ObtenerApiKey()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (config != null && config.TryGetValue("ApiKey", out string? key))
                    {
                        return key ?? string.Empty;
                    }
                }
            }
            catch { /* Ignore read errors */ }
            return string.Empty;
        }

        public void GuardarApiKey(string key)
        {
            try
            {
                var config = new Dictionary<string, string> { { "ApiKey", key.Trim() } };
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"No se pudo guardar la API Key: {ex.Message}");
            }
        }

        public async Task<string> ResponderPreguntaAsync(List<ItemMensajeChat> historial, string contextoAdicional = "")
        {
            string apiKey = ObtenerApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "⚠️ Por favor, ingresa y guarda tu API Key de Google Gemini en la sección superior antes de iniciar el chat.";
            }

            if (historial == null || !historial.Any())
            {
                return "No hay mensajes en el historial.";
            }

            try
            {
                // Obtener la última pregunta del usuario
                var ultimoMensajeUsuario = historial.LastOrDefault(m => m.EsUsuario);
                string pregunta = ultimoMensajeUsuario?.Contenido ?? string.Empty;

                // 1. Compilar el contexto contable de la base de datos
                string contexto = await CompilarContextoLocalAsync(pregunta);
                if (!string.IsNullOrEmpty(contextoAdicional))
                {
                    contexto += "\n\n" + contextoAdicional;
                }

                // 2. Definir instrucciones del sistema
                string systemInstructions = 
                    "Eres el Asistente IA Contable de 'El Surtidor de la Frontera, C.A.'. " +
                    "Tu única función es responder preguntas sobre los datos de conciliación de la empresa (ventas, pagos, tasas cambiarias y banco) " +
                    "basándote exclusivamente en el contexto que te proporcione el sistema.\n\n" +
                    "REGLA CRÍTICA: Si el usuario te pregunta sobre cualquier tema que no esté relacionado con la contabilidad, ventas, conciliación bancaria de Cashea " +
                    "o los datos de la aplicación (por ejemplo, recetas de cocina, deportes, programación, etc.), debes responder con gentileza indicando: " +
                    "\"Disculpa, como tu asistente de conciliación de El Surtidor de la Frontera, mi función está limitada únicamente a responder dudas sobre los datos contables de la aplicación. No poseo información relacionada con otros temas.\"\n\n" +
                    "Sé siempre profesional, educado, conciso y responde en español. Muestra montos con formato de moneda.";

                // 3. Reconstruir los turnos de la conversación (historial) para pasárselo a Gemini
                var contents = new List<object>();
                for (int i = 0; i < historial.Count; i++)
                {
                    var msg = historial[i];
                    string role = msg.EsUsuario ? "user" : "model";
                    string text = msg.Contenido;

                    // Si es el último turno (el mensaje actual del usuario), le inyectamos la base de conocimientos y los datos de pantalla
                    if (i == historial.Count - 1 && msg.EsUsuario)
                    {
                        text = $"[CONTEXTO DE LA BASE DE DATOS LOCAL Y PANTALLA]\n{contexto}\n\n[PREGUNTA DEL USUARIO]\n{text}";
                    }

                    contents.Add(new
                    {
                        role = role,
                        parts = new[]
                        {
                            new { text = text }
                        }
                    });
                }

                // Formatear payload para la API de Gemini (API v1beta)
                var requestBody = new
                {
                    systemInstruction = new
                    {
                        parts = new[]
                        {
                            new { text = systemInstructions }
                        }
                    },
                    contents = contents.ToArray(),
                    generationConfig = new
                    {
                        temperature = 0.2,
                        maxOutputTokens = 8000
                    }
                };

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key={apiKey}";
                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string errorDetail = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && errorDetail.Contains("API_KEY_INVALID"))
                    {
                        return "❌ Error: La API Key ingresada es inválida. Por favor verifícala en Google AI Studio.";
                    }
                    return $"❌ Error al conectar con Google Gemini (Código {response.StatusCode}): {response.ReasonPhrase}";
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(jsonResponse))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var parts = candidates[0].GetProperty("content").GetProperty("parts");
                        if (parts.GetArrayLength() > 0)
                        {
                            return parts[0].GetProperty("text").GetString() ?? "No se recibió respuesta legible.";
                        }
                    }
                }

                return "⚠️ No se pudo procesar la respuesta de la IA.";
            }
            catch (Exception ex)
            {
                return $"❌ Error de conexión: {ex.Message}";
            }
        }

        private async Task<string> CompilarContextoLocalAsync(string pregunta)
        {
            var sb = new StringBuilder();

            using (var context = new SurtidorDbContext())
            {
                try
                {
                    // 1. Obtener agregados por mes en Ventas
                    var ventasQuery = await context.VentasIndividualesCashea
                        .ToListAsync();
                    
                    var ventasPorMes = ventasQuery
                        .GroupBy(v => v.FechaCompra.ToString("yyyy-MM"))
                        .Select(g => new {
                            Mes = g.Key,
                            Count = g.Count(),
                            Total = g.Sum(v => v.VentaTotalUsd),
                            Caja = g.Sum(v => v.PagadoCajaUsd),
                            Financiado = g.Sum(v => v.MontoFinanciado)
                        }).OrderBy(m => m.Mes).ToList();

                    sb.AppendLine("=== RESUMEN DE VENTAS POR MES ===");
                    foreach (var m in ventasPorMes)
                    {
                        sb.AppendLine($"- Mes: {m.Mes} | Ventas creadas: {m.Count} órdenes | Total vendido: {m.Total:N2} USD | Inicial cobrado en caja: {m.Caja:N2} USD | Monto financiado: {m.Financiado:N2} USD");
                    }
                    sb.AppendLine();

                    // 2. Obtener agregados por mes en Pagos de Cashea
                    var pagosQuery = await context.PagosLiquidacionesCashea.ToListAsync();
                    var pagosPorMes = pagosQuery
                        .GroupBy(p => p.FechaLiquidacion.ToString("yyyy-MM"))
                        .Select(g => new {
                            Mes = g.Key,
                            Count = g.Count(),
                            TotalBs = g.Sum(p => p.TotalDepositadoBs),
                            TotalUsd = g.Sum(p => p.TotalDepositadoUsd)
                        }).OrderBy(m => m.Mes).ToList();

                    sb.AppendLine("=== RESUMEN DE PAGOS LIQUIDADOS POR CASHEA (LOTES) ===");
                    foreach (var m in pagosPorMes)
                    {
                        sb.AppendLine($"- Mes: {m.Mes} | Liquidaciones recibidas: {m.Count} transacciones | Total recibido: {m.TotalBs:N2} Bs. ({m.TotalUsd:N2} USD)");
                    }
                    sb.AppendLine();

                    // 3. Obtener agregados por mes en Banco
                    var bancoQuery = await context.MovimientosBancarios.ToListAsync();
                    var bancoPorMes = bancoQuery
                        .GroupBy(b => b.Fecha.ToString("yyyy-MM"))
                        .Select(g => new {
                            Mes = g.Key,
                            Count = g.Count(),
                            Abonado = g.Sum(b => b.MontoAbonadoVes)
                        }).OrderBy(m => m.Mes).ToList();

                    sb.AppendLine("=== RESUMEN DE BANCO (BANESCO) ===");
                    foreach (var m in bancoPorMes)
                    {
                        sb.AppendLine($"- Mes: {m.Mes} | Abonos en cuenta: {m.Count} movimientos | Total abonado real: {m.Abonado:N2} Bs.");
                    }
                    sb.AppendLine();

                    // 4. Búsqueda RAG local de registros específicos (ID de orden o referencia de 6+ dígitos)
                    var numbers = Regex.Matches(pregunta, @"\b\d{6,12}\b");
                    if (numbers.Count > 0)
                    {
                        sb.AppendLine("=== REGISTROS DE DETALLE ENCONTRADOS (BÚSQUEDA ESPECÍFICA) ===");
                        foreach (Match match in numbers)
                        {
                            string code = match.Value;

                            // Buscar en Ventas
                            var ventaMatch = await context.VentasIndividualesCashea.FirstOrDefaultAsync(v => v.IdOrden == code || v.NroFactura == code);
                            if (ventaMatch != null)
                            {
                                sb.AppendLine($"* Detalle de Venta para Orden/Factura '{code}':");
                                sb.AppendLine($"  - IdOrden: {ventaMatch.IdOrden}");
                                sb.AppendLine($"  - Factura: {ventaMatch.NroFactura}");
                                sb.AppendLine($"  - Fecha: {ventaMatch.FechaCompra:dd/MM/yyyy}");
                                sb.AppendLine($"  - Total: {ventaMatch.VentaTotalUsd:N2} USD");
                                sb.AppendLine($"  - Pagado Caja: {ventaMatch.PagadoCajaUsd:N2} USD");
                                sb.AppendLine($"  - Financiado: {ventaMatch.MontoFinanciado:N2} USD");
                                sb.AppendLine($"  - Estatus: {ventaMatch.Estatus}");
                                sb.AppendLine($"  - Cuota 1: {ventaMatch.MontoCuota1:N2} USD ({ventaMatch.FechaCuota1:dd/MM/yyyy})");
                                sb.AppendLine($"  - Cuota 2: {ventaMatch.MontoCuota2:N2} USD ({ventaMatch.FechaCuota2:dd/MM/yyyy})");
                                sb.AppendLine($"  - Cuota 3: {ventaMatch.MontoCuota3:N2} USD ({ventaMatch.FechaCuota3:dd/MM/yyyy})");
                            }

                            // Buscar en Pagos
                            var pagoMatch = await context.PagosLiquidacionesCashea.FirstOrDefaultAsync(p => p.IdOrden == code || p.ReferenciaBancaria == code);
                            if (pagoMatch != null)
                            {
                                sb.AppendLine($"* Detalle de Pago para Orden/Referencia '{code}':");
                                sb.AppendLine($"  - IdPago: {pagoMatch.IdPago}");
                                sb.AppendLine($"  - Fecha Liquidación: {pagoMatch.FechaLiquidacion:dd/MM/yyyy}");
                                sb.AppendLine($"  - Referencia Bancaria: {pagoMatch.ReferenciaBancaria}");
                                sb.AppendLine($"  - Total Bs: {pagoMatch.TotalDepositadoBs:N2} Bs.");
                                sb.AppendLine($"  - Total Usd: {pagoMatch.TotalDepositadoUsd:N2} USD");
                                sb.AppendLine($"  - Cuota pagada: {(pagoMatch.NroCuotaPagada == -1 ? "No Conciliado" : pagoMatch.NroCuotaPagada == 0 ? "Pago Inicial" : "Cuota " + pagoMatch.NroCuotaPagada)}");
                                sb.AppendLine($"  - Estado: {pagoMatch.Estado}");
                            }

                            // Buscar en Banco
                            var mbMatch = await context.MovimientosBancarios.FirstOrDefaultAsync(m => m.ReferenciaBanco == code);
                            if (mbMatch != null)
                            {
                                sb.AppendLine($"* Movimiento Bancario encontrado para Referencia '{code}':");
                                sb.AppendLine($"  - Fecha: {mbMatch.Fecha:dd/MM/yyyy}");
                                sb.AppendLine($"  - Referencia: {mbMatch.ReferenciaBanco}");
                                sb.AppendLine($"  - Descripción: {mbMatch.Descripcion}");
                                sb.AppendLine($"  - Monto: {mbMatch.MontoAbonadoVes:N2} Bs.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[Error compilando contexto contable: {ex.Message}]");
                }
            }

            return sb.ToString();
        }
    }
}
