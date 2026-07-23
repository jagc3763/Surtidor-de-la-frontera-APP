using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;

namespace SurtidorADM.Services
{
    public class DatosActualizacion
    {
        public string Version { get; set; } = string.Empty;
        public string UrlDescarga { get; set; } = string.Empty;
        public string Novedades { get; set; } = string.Empty;
    }

    public class ResultadoVerificacion
    {
        public bool HayActualizacion { get; set; }
        public string VersionNueva { get; set; } = string.Empty;
        public string UrlDescarga { get; set; } = string.Empty;
        public string Novedades { get; set; } = string.Empty;
    }

    public class UpdateService
    {
        public const string VersionActual = "1.0.9";
        private const string UrlJsonActualizacion = "https://raw.githubusercontent.com/jagc3763/Surtidor-de-la-frontera-APP/main/actualizaciones.json";
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<ResultadoVerificacion> VerificarActualizacionAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, UrlJsonActualizacion);
                request.Headers.IfModifiedSince = DateTimeOffset.UtcNow;

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return new ResultadoVerificacion { HayActualizacion = false };

                string jsonContent = await response.Content.ReadAsStringAsync();
                var datos = JsonSerializer.Deserialize<DatosActualizacion>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (datos == null || string.IsNullOrEmpty(datos.Version))
                    return new ResultadoVerificacion { HayActualizacion = false };

                if (VersionNuevaEsMayor(VersionActual, datos.Version))
                {
                    return new ResultadoVerificacion
                    {
                        HayActualizacion = true,
                        VersionNueva = datos.Version,
                        UrlDescarga = datos.UrlDescarga,
                        Novedades = datos.Novedades
                    };
                }
            }
            catch
            {
                // Silenciosamente ignoramos errores de red para no bloquear el arranque de la app
            }

            return new ResultadoVerificacion { HayActualizacion = false };
        }

        private bool VersionNuevaEsMayor(string versionActual, string versionNueva)
        {
            try
            {
                var vActual = new Version(versionActual);
                var vNueva = new Version(versionNueva);
                return vNueva > vActual;
            }
            catch
            {
                return versionNueva != versionActual;
            }
        }

        public async Task DescargarActualizacionAsync(string urlDescarga, string rutaDestino, IProgress<double> progreso)
        {
            using (var response = await _httpClient.GetAsync(urlDescarga, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(rutaDestino, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalReadBytes = 0;
                    int readBytes;

                    while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, readBytes);
                        totalReadBytes += readBytes;

                        if (totalBytes.HasValue)
                        {
                            double pct = (double)totalReadBytes / totalBytes.Value * 100.0;
                            progreso.Report(pct);
                        }
                    }
                }
            }
        }

        public void EjecutarInstalacionHot(string rutaNuevoExe)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("No se puede obtener la ruta del ejecutable.");
            string tempBatchPath = Path.Combine(Path.GetTempPath(), "update_surtidor.bat");

            string batchContent = $@"
@echo off
taskkill /f /im SurtidorADM.exe >nul 2>&1
timeout /t 2 /nobreak >nul
copy /y ""{rutaNuevoExe}"" ""{currentExe}""
if errorlevel 1 (
    powershell -Command ""Start-Process powershell -ArgumentList '-Command Start-Sleep -s 2; Copy-Item -Path ''{rutaNuevoExe}'' -Destination ''{currentExe}'' -Force; Start-Process ''{currentExe}''' -Verb RunAs""
) else (
    start """" ""{currentExe}""
)
del ""%~f0""
";

            File.WriteAllText(tempBatchPath, batchContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{tempBatchPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Application.Current.Shutdown();
        }
    }
}
