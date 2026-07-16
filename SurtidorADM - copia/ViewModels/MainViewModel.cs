using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using SurtidorADM.Data;
using SurtidorADM.Models;
using SurtidorADM.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SurtidorADM.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // Servicios inyectados
        private readonly TipoCambioService _tipoCambioService;
        private readonly ImportadorHistoricoBcv _importadorBcv;
        private readonly ExcelParserService _excelParser;
        private readonly MotorConciliacionService _motorConciliacion;

        // Propiedades enlazadas a la Interfaz de Usuario (UI)
        private decimal _tasaBcvActual;
        public decimal TasaBcvActual
        {
            get => _tasaBcvActual;
            set => SetProperty(ref _tasaBcvActual, value);
        }

        private string _mensajeEstado = "Sistema inicializado y listo.";
        public string MensajeEstado
        {
            get => _mensajeEstado;
            set => SetProperty(ref _mensajeEstado, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        // Colección dinámica para la grilla de resultados
        private ObservableCollection<ResultadoConciliacion> _resultadosConciliacion = new();
        public ObservableCollection<ResultadoConciliacion> ResultadosConciliacion
        {
            get => _resultadosConciliacion;
            set => SetProperty(ref _resultadosConciliacion, value);
        }

        // Comandos de Botones
        public ICommand SincronizarBcvCommand { get; }
        public ICommand ImportarHistoricoCommand { get; }
        public ICommand ImportarPagosCasheaCommand { get; }
        public ICommand ImportarVentasCasheaCommand { get; }
        public ICommand ImportarBancoCommand { get; }
        public ICommand EjecutarConciliacionCommand { get; }
        public ICommand LimpiarBaseDatosCommand { get; }

        public MainViewModel()
        {
            // Inicialización de Servicios
            _tipoCambioService = new TipoCambioService();
            _importadorBcv = new ImportadorHistoricoBcv();
            _excelParser = new ExcelParserService();
            _motorConciliacion = new MotorConciliacionService();

            // Inicialización de Comandos protegidos contra doble clic
            SincronizarBcvCommand = new RelayCommand(async _ => await SincronizarBcvAsync(), _ => !IsBusy);
            ImportarHistoricoCommand = new RelayCommand(async _ => await ImportarHistoricoAsync(), _ => !IsBusy);
            ImportarPagosCasheaCommand = new RelayCommand(async _ => await ImportarPagosAsync(), _ => !IsBusy);
            ImportarVentasCasheaCommand = new RelayCommand(async _ => await ImportarVentasAsync(), _ => !IsBusy);
            ImportarBancoCommand = new RelayCommand(async _ => await ImportarBancoAsync(), _ => !IsBusy);
            EjecutarConciliacionCommand = new RelayCommand(async _ => await EjecutarConciliacionAsync(), _ => !IsBusy);
            LimpiarBaseDatosCommand = new RelayCommand(async _ => await LimpiarBaseDatosAsync(), _ => !IsBusy);
        }

        // ==========================================
        // MÉTODOS DE EXTRACCIÓN Y PERSISTENCIA
        // ==========================================

        private async Task SincronizarBcvAsync()
        {
            IsBusy = true;
            MensajeEstado = "Conectando con el portal del BCV...";

            try
            {
                TasaBcvActual = await _tipoCambioService.ObtenerTasaBcvAsync();
                await _tipoCambioService.GuardarTasaDiariaAsync(DateTime.Now, TasaBcvActual, 0);
                MensajeEstado = $"Sincronización exitosa. Tasa oficial de hoy: {TasaBcvActual} VES/USD";
            }
            catch (Exception ex)
            {
                MensajeEstado = "Error de sincronización con el BCV.";
                MessageBox.Show(ex.Message, "Error de Red", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ImportarHistoricoAsync()
        {
            var openFileDialog = new OpenFileDialog { Title = "Seleccionar Histórico BCV", Filter = "Excel (*.xlsx)|*.xlsx" };

            if (openFileDialog.ShowDialog() == true)
            {
                IsBusy = true;
                MensajeEstado = "Importando tasas históricas...";

                try
                {
                    await _importadorBcv.ImportarArchivoXlsxAsync(openFileDialog.FileName);
                    MensajeEstado = "Histórico importado correctamente a la base de datos.";
                }
                catch (Exception ex)
                {
                    MensajeEstado = "Error importando histórico.";
                    MessageBox.Show(ex.Message, "Error de Lectura", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task ImportarPagosAsync()
        {
            var openFileDialog = new OpenFileDialog { Title = "Seleccionar Reporte de Pagos Cashea", Filter = "Excel (*.xlsx)|*.xlsx" };

            if (openFileDialog.ShowDialog() == true)
            {
                IsBusy = true;
                MensajeEstado = "Extrayendo lotes...";

                try
                {
                    // 1. Parseamos el archivo
                    var pagosDesdeArchivo = await Task.Run(() => _excelParser.ParsearReportePagos(openFileDialog.FileName));

                    // 2. DEDUPLICACIÓN INTERNA: Eliminamos cualquier duplicado que venga DENTRO del mismo archivo
                    var pagosUnicosEnArchivo = pagosDesdeArchivo
                        .GroupBy(p => p.IdPago)
                        .Select(g => g.First())
                        .ToList();

                    using (var context = new SurtidorDbContext())
                    {
                        // Limpiamos cualquier rastro anterior en la memoria de EF
                        context.ChangeTracker.Clear();

                        // 3. Obtenemos todos los pagos ya existentes en la base de datos
                        var pagosExistentes = await context.PagosLiquidacionesCashea.ToListAsync();
                        var dictExistentes = pagosExistentes.ToDictionary(p => p.IdPago);

                        int nuevos = 0;
                        int actualizados = 0;

                        foreach (var pago in pagosUnicosEnArchivo)
                        {
                            if (dictExistentes.TryGetValue(pago.IdPago, out var existente))
                            {
                                // Si existe, actualizamos si alguno de los nuevos campos cambió
                                if (existente.IdOrden != pago.IdOrden || existente.NroCuotaPagada != pago.NroCuotaPagada)
                                {
                                    existente.IdOrden = pago.IdOrden;
                                    existente.NroCuotaPagada = pago.NroCuotaPagada;
                                    context.PagosLiquidacionesCashea.Update(existente);
                                    actualizados++;
                                }
                            }
                            else
                            {
                                context.PagosLiquidacionesCashea.Add(pago);
                                nuevos++;
                            }
                        }

                        if (nuevos > 0 || actualizados > 0)
                        {
                            await context.SaveChangesAsync();
                            MensajeEstado = $"Éxito: Se registraron {nuevos} nuevos lotes y se actualizaron {actualizados} existentes.";
                        }
                        else
                        {
                            MensajeEstado = "No hay cambios: todos los lotes ya existen y están actualizados.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    MensajeEstado = "Error procesando pagos.";
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task ImportarVentasAsync()
        {
            var openFileDialog = new OpenFileDialog { Title = "Seleccionar Reporte de Ventas Individuales", Filter = "Excel (*.xlsx)|*.xlsx" };

            if (openFileDialog.ShowDialog() == true)
            {
                IsBusy = true;
                MensajeEstado = "Procesando ventas...";

                try
                {
                    // 1. Extraemos todo el archivo
                    var ventasDesdeArchivo = await Task.Run(() => _excelParser.ParsearReporteVentas(openFileDialog.FileName));

                    // 2. DEDUPLICACIÓN CRÍTICA: Eliminamos cualquier ID duplicado que venga DENTRO del mismo archivo
                    // Esto es necesario porque si el Excel tiene filas repetidas, EF colapsa.
                    var ventasUnicasEnArchivo = ventasDesdeArchivo
                        .GroupBy(v => v.IdOrden)
                        .Select(g => g.First())
                        .ToList();

                    using (var context = new SurtidorDbContext())
                    {
                        // Limpiamos el rastreador por si acaso
                        context.ChangeTracker.Clear();

                        // 3. Obtenemos IDs que ya existen en la base de datos
                        var idsExistentes = context.VentasIndividualesCashea.AsNoTracking().Select(v => v.IdOrden).ToHashSet();

                        // 4. Filtramos lo que viene del archivo contra lo que ya está guardado
                        var ventasParaGuardar = ventasUnicasEnArchivo.Where(v => !idsExistentes.Contains(v.IdOrden)).ToList();

                        if (ventasParaGuardar.Any())
                        {
                            context.VentasIndividualesCashea.AddRange(ventasParaGuardar);
                            await context.SaveChangesAsync();
                            MensajeEstado = $"Éxito: Se registraron {ventasParaGuardar.Count} nuevas ventas.";
                        }
                        else
                        {
                            MensajeEstado = "No hay ventas nuevas (todo está duplicado o ya existía).";
                        }
                    }
                }
                catch (Exception ex)
                {
                    MensajeEstado = "Error procesando ventas.";
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task ImportarBancoAsync()
        {
            var openFileDialog = new OpenFileDialog { Title = "Seleccionar Estado de Cuenta Bancario", Filter = "Excel (*.xlsx)|*.xlsx" };

            if (openFileDialog.ShowDialog() == true)
            {
                IsBusy = true;
                MensajeEstado = "Procesando movimientos bancarios y comisiones...";

                try
                {
                    var movimientosParseados = await Task.Run(() => _excelParser.ParsearReporteBanco(openFileDialog.FileName));

                    using var context = new SurtidorDbContext();
                    var movsExistentes = context.MovimientosBancarios.ToList();

                    var nuevosMovimientos = movimientosParseados.Where(mNuevo =>
                        !movsExistentes.Any(mExistente =>
                            mExistente.ReferenciaBanco == mNuevo.ReferenciaBanco &&
                            mExistente.MontoAbonadoVes == mNuevo.MontoAbonadoVes)).ToList();

                    if (nuevosMovimientos.Any())
                    {
                        context.MovimientosBancarios.AddRange(nuevosMovimientos);
                        await context.SaveChangesAsync();
                        MensajeEstado = $"Éxito: Se inyectaron {nuevosMovimientos.Count} movimientos bancarios al motor.";
                    }
                    else
                    {
                        MensajeEstado = "No hay movimientos nuevos. El estado de cuenta ya estaba registrado íntegramente.";
                    }
                }
                catch (Exception ex)
                {
                    MensajeEstado = "Error procesando estado de cuenta.";
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        // ==========================================
        // MOTOR DE CONCILIACIÓN
        // ==========================================

        private async Task EjecutarConciliacionAsync()
        {
            IsBusy = true;
            MensajeEstado = "Auditando transacciones y cruzando referencias...";

            try
            {
                var resultados = await Task.Run(() => _motorConciliacion.EjecutarConciliacionAsync());

                ResultadosConciliacion = new ObservableCollection<ResultadoConciliacion>(resultados);

                int exitos = resultados.Count(r => r.Estatus == "Éxito");
                int fallos = resultados.Count - exitos;

                MensajeEstado = $"Auditoría finalizada. {exitos} liquidaciones conciliadas con éxito, {fallos} con observaciones.";
            }
            catch (Exception ex)
            {
                MensajeEstado = "Error en el motor de conciliación.";
                MessageBox.Show(ex.Message, "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LimpiarBaseDatosAsync()
        {
            var result = MessageBox.Show(
                "¿Está seguro de que desea borrar TODOS los datos registrados (tasas, ventas, pagos y movimientos bancarios)? Esta acción eliminará permanentemente la base de datos actual para iniciar una nueva auditoría.",
                "Confirmar Reinicio de Base de Datos",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                IsBusy = true;
                MensajeEstado = "Borrando base de datos y recreando esquema limpio...";

                try
                {
                    await Task.Run(() =>
                    {
                        using (var context = new SurtidorDbContext())
                        {
                            context.Database.EnsureDeleted();
                            context.Database.EnsureCreated();
                        }
                    });

                    // Restablecer valores en la interfaz gráfica
                    ResultadosConciliacion.Clear();
                    TasaBcvActual = 0;

                    MensajeEstado = "Base de datos restablecida. Lista para una nueva auditoría.";
                    MessageBox.Show("Base de datos limpiada con éxito. Todos los datos históricos fueron eliminados.", "Limpieza Completada", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MensajeEstado = "Error al intentar borrar la base de datos.";
                    MessageBox.Show(ex.Message, "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
    }
}