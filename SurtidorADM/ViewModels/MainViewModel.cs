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
using System.Text;

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
        public ICommand CargarReporteMensualCommand { get; }
        public ICommand VerDetallesDiferenciasCommand { get; }
        public ICommand EnviarMensajeChatCommand { get; }

        private string _mensajeChatTexto = string.Empty;
        public string MensajeChatTexto
        {
            get => _mensajeChatTexto;
            set => SetProperty(ref _mensajeChatTexto, value);
        }

        public ObservableCollection<ItemMensajeChat> MensajesChat { get; } = new()
        {
            new ItemMensajeChat 
            { 
                Contenido = "¡Hola! Soy tu Asistente IA Contable de El Surtidor de la Frontera. ¿En qué puedo ayudarte hoy referente a tus ventas, pagos o conciliaciones bancarias?", 
                EsUsuario = false 
            }
        };

        private ObservableCollection<ItemCotejo> _itemsCotejo = new();
        public ObservableCollection<ItemCotejo> ItemsCotejo
        {
            get => _itemsCotejo;
            set => SetProperty(ref _itemsCotejo, value);
        }

        private ObservableCollection<DetalleDiferencia> _detallesDiferencias = new();
        public ObservableCollection<DetalleDiferencia> DetallesDiferencias
        {
            get => _detallesDiferencias;
            set
            {
                SetProperty(ref _detallesDiferencias, value);
                OnPropertyChanged(nameof(HayDiferencias));
            }
        }

        public bool HayDiferencias => DetallesDiferencias != null && DetallesDiferencias.Any();

        private string _periodoCotejo = "Ningún reporte cargado.";
        public string PeriodoCotejo
        {
            get => _periodoCotejo;
            set => SetProperty(ref _periodoCotejo, value);
        }

        private bool _mostrarPanelCotejo = false;
        public bool MostrarPanelCotejo
        {
            get => _mostrarPanelCotejo;
            set => SetProperty(ref _mostrarPanelCotejo, value);
        }

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
            CargarReporteMensualCommand = new RelayCommand(async _ => await CargarReporteMensualAsync(), _ => !IsBusy);
            VerDetallesDiferenciasCommand = new RelayCommand(_ => VerDetallesDiferencias(), _ => HayDiferencias && !IsBusy);
            EnviarMensajeChatCommand = new RelayCommand(async _ => await EnviarMensajeChatAsync(), _ => !IsBusy);
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

        private async Task CargarReporteMensualAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                Title = "Seleccionar Reporte Mensual de Cashea"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                IsBusy = true;
                MensajeEstado = "Analizando Reporte Mensual de Cashea...";
                ItemsCotejo.Clear();

                try
                {
                    var parser = new CasheaReportParser();
                    var datosCashea = await Task.Run(() => parser.ParsearReporteMensual(openFileDialog.FileName));

                    // Validar nombre de empresa / aliado para alertar al usuario si es incorrecto
                    if (!string.IsNullOrEmpty(datosCashea.NombreEmpresa) && 
                        !datosCashea.NombreEmpresa.Contains("SURTIDOR DE LA FRONTERA", StringComparison.OrdinalIgnoreCase))
                    {
                        var warningMsg = $"⚠️ ADVERTENCIA: El reporte mensual cargado pertenece a '{datosCashea.NombreEmpresa}'.\n\n" +
                                         $"Recuerda que estás conciliando en el sistema de 'EL SURTIDOR DE LA FRONTERA, C.A.'. " +
                                         $"Si subes el reporte mensual de otra empresa, verás discrepancias masivas en el cotejo.\n\n" +
                                         $"¿Deseas continuar con el cotejo de todas formas?";
                        
                        var result = MessageBox.Show(warningMsg, "Alerta: Reporte de Empresa Incorrecto", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.No)
                        {
                            MensajeEstado = "Operación cancelada. Por favor, sube el reporte mensual correcto.";
                            return;
                        }
                    }

                    PeriodoCotejo = $"Período: Del {datosCashea.FechaDesde:dd/MM/yyyy} Hasta {datosCashea.FechaHasta:dd/MM/yyyy}";

                    using (var context = new SurtidorDbContext())
                    {
                        var fechaDesde = datosCashea.FechaDesde;
                        var fechaHasta = datosCashea.FechaHasta;
                        var fechaHastaMasUno = fechaHasta.AddDays(1);

                        // 1. Obtener ventas en el rango
                        var ventas = await context.VentasIndividualesCashea
                            .Where(v => v.FechaCompra >= fechaDesde && v.FechaCompra < fechaHastaMasUno)
                            .ToListAsync();

                        var todasVentas = await context.VentasIndividualesCashea.ToListAsync();
                        var todosPagos = await context.PagosLiquidacionesCashea.Where(p => p.FechaLiquidacion < fechaHastaMasUno).ToListAsync();
                        var banco = await context.MovimientosBancarios.Where(m => m.Fecha < fechaHastaMasUno).ToListAsync();
                        var historicoTasas = await context.TasasDiarias.ToListAsync();

                        decimal ventasTotalesSystem = ventas.Sum(v => v.VentaTotalUsd);
                        decimal pagadoCajaSystem = ventas.Sum(v => v.PagadoCajaUsd);
                        decimal financiadoSystem = ventas.Sum(v => v.MontoFinanciado);

                        // 2. Calcular recibido en banco en el periodo actual e iniciar la auditoría de diferencias
                        decimal recibidoBancoSystem = 0;
                        decimal cuotasAdelantadasSystem = 0;
                        decimal pagoInicialAppSystem = 0;

                        DetallesDiferencias.Clear();

                        var bancoEnPeriodo = banco.Where(m => m.Fecha >= fechaDesde && m.Fecha < fechaHastaMasUno).ToList();
                        foreach (var m in bancoEnPeriodo)
                        {
                            var refBanco = (m.ReferenciaBanco ?? "").Trim();
                            var pagoMatch = todosPagos.FirstOrDefault(p => p.ReferenciaBancaria != null && p.ReferenciaBancaria.Trim() == refBanco);
                            if (pagoMatch != null)
                            {
                                decimal tasa = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                                decimal montoUsd = m.MontoAbonadoVes / tasa;
                                recibidoBancoSystem += montoUsd;

                                // Auditoría inteligente por transacción
                                decimal tasaCashea = pagoMatch.TotalDepositadoUsd > 0 ? (pagoMatch.TotalDepositadoBs / pagoMatch.TotalDepositadoUsd) : 0;
                                
                                // Check 1: Diferencia de Tasa BCV
                                if (tasaCashea > 0 && Math.Abs(tasaCashea - tasa) > 0.01m)
                                {
                                    decimal diffUsd = montoUsd - pagoMatch.TotalDepositadoUsd;
                                    string sign = diffUsd >= 0 ? "+" : "";
                                    DetallesDiferencias.Add(new DetalleDiferencia
                                    {
                                        Referencia = refBanco,
                                        Fecha = m.Fecha.ToString("dd/MM/yyyy"),
                                        TipoError = "Diferencia de Tasa BCV",
                                        Detalle = $"Cashea aplicó tasa de {tasaCashea:F4} VES/USD, mientras que el sistema oficial BCV esperaba {tasa:F4} VES/USD.",
                                        MontoBsCashea = pagoMatch.TotalDepositadoBs,
                                        MontoBsBanco = m.MontoAbonadoVes,
                                        TasaCashea = tasaCashea,
                                        TasaBcvSistema = tasa,
                                        ImpactoUsd = $"{sign}{diffUsd:N2} $"
                                    });
                                }

                                // Check 2: Descuadre de Depósito Bancario
                                if (Math.Abs(m.MontoAbonadoVes - pagoMatch.TotalDepositadoBs) > 0.05m)
                                {
                                    decimal diffBs = m.MontoAbonadoVes - pagoMatch.TotalDepositadoBs;
                                    decimal diffUsd = diffBs / tasa;
                                    string sign = diffUsd >= 0 ? "+" : "";
                                    DetallesDiferencias.Add(new DetalleDiferencia
                                    {
                                        Referencia = refBanco,
                                        Fecha = m.Fecha.ToString("dd/MM/yyyy"),
                                        TipoError = "Descuadre de Depósito Bancario",
                                        Detalle = $"Cashea declaró {pagoMatch.TotalDepositadoBs:N2} Bs., pero el abono real recibido en Banesco fue de {m.MontoAbonadoVes:N2} Bs.",
                                        MontoBsCashea = pagoMatch.TotalDepositadoBs,
                                        MontoBsBanco = m.MontoAbonadoVes,
                                        TasaCashea = tasaCashea,
                                        TasaBcvSistema = tasa,
                                        ImpactoUsd = $"{sign}{diffUsd:N2} $"
                                    });
                                }

                                // Buscar venta
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
                                        pagoInicialAppSystem += montoUsd;
                                    }
                                }
                                else if (venta != null)
                                {
                                    int cuotaNo = pagoMatch.NroCuotaPagada;
                                    DateTime? dueDt = cuotaNo == 1 ? venta.FechaCuota1 : (cuotaNo == 2 ? venta.FechaCuota2 : venta.FechaCuota3);
                                    if (dueDt.HasValue && dueDt.Value > fechaHasta)
                                    {
                                        cuotasAdelantadasSystem += montoUsd;
                                    }
                                }
                            }
                        }

                        // Check 3: Transacciones del Lote Cashea que faltan en el Banco Banesco
                        var pagosEnPeriodo = todosPagos.Where(p => p.FechaLiquidacion >= fechaDesde && p.FechaLiquidacion < fechaHastaMasUno).ToList();
                        foreach (var p in pagosEnPeriodo)
                        {
                            var mMatch = banco.FirstOrDefault(mb => mb.ReferenciaBanco != null && mb.ReferenciaBanco.Trim() == p.ReferenciaBancaria.Trim());
                            if (mMatch == null)
                            {
                                DetallesDiferencias.Add(new DetalleDiferencia
                                {
                                    Referencia = p.ReferenciaBancaria,
                                    Fecha = p.FechaLiquidacion.ToString("dd/MM/yyyy"),
                                    TipoError = "Falta en Banco",
                                    Detalle = $"Lote Cashea declara abono de {p.TotalDepositadoBs:N2} Bs. (orden: {p.IdOrden ?? "N/A"}), pero no figura en el estado de cuenta bancario.",
                                    MontoBsCashea = p.TotalDepositadoBs,
                                    MontoBsBanco = 0,
                                    TasaCashea = p.TotalDepositadoUsd > 0 ? (p.TotalDepositadoBs / p.TotalDepositadoUsd) : 0,
                                    TasaBcvSistema = 0,
                                    ImpactoUsd = $"-{p.TotalDepositadoUsd:N2} $"
                                });
                            }
                        }

                        // Check 4: Movimientos de Banesco que faltan en los Lotes de Cashea
                        foreach (var m in bancoEnPeriodo)
                        {
                            var refBanco = (m.ReferenciaBanco ?? "").Trim();
                            if ((m.Descripcion ?? "").Contains("CASHEA", StringComparison.OrdinalIgnoreCase))
                            {
                                var pMatch = todosPagos.FirstOrDefault(p => p.ReferenciaBancaria != null && p.ReferenciaBancaria.Trim() == refBanco);
                                if (pMatch == null)
                                {
                                    decimal tasaBcv = ObtenerTasaFechaValor(m.Fecha, historicoTasas);
                                    decimal montoUsd = m.MontoAbonadoVes / tasaBcv;
                                    DetallesDiferencias.Add(new DetalleDiferencia
                                    {
                                        Referencia = refBanco,
                                        Fecha = m.Fecha.ToString("dd/MM/yyyy"),
                                        TipoError = "Falta en Lotes Cashea",
                                        Detalle = $"Se recibió abono en Banesco de {m.MontoAbonadoVes:N2} Bs. (Ref: {refBanco}), pero no figura en los archivos de Lotes Cashea.",
                                        MontoBsCashea = 0,
                                        MontoBsBanco = m.MontoAbonadoVes,
                                        TasaCashea = 0,
                                        TasaBcvSistema = tasaBcv,
                                        ImpactoUsd = $"+{montoUsd:N2} $"
                                    });
                                }
                            }
                        }

                        decimal bancoNetoSystem = recibidoBancoSystem - cuotasAdelantadasSystem - pagoInicialAppSystem;

                        // 3. Calcular cuentas por cobrar (cuotas con vencimiento en el periodo)
                        decimal cuentasPorCobrarSystem = 0;
                        foreach (var venta in todasVentas)
                        {
                            if (venta.FechaCuota1.HasValue && venta.FechaCuota1.Value >= fechaDesde && venta.FechaCuota1.Value < fechaHastaMasUno)
                            {
                                cuentasPorCobrarSystem += venta.MontoCuota1;
                            }
                            if (venta.FechaCuota2.HasValue && venta.FechaCuota2.Value >= fechaDesde && venta.FechaCuota2.Value < fechaHastaMasUno)
                            {
                                cuentasPorCobrarSystem += venta.MontoCuota2;
                            }
                            if (venta.FechaCuota3.HasValue && venta.FechaCuota3.Value >= fechaDesde && venta.FechaCuota3.Value < fechaHastaMasUno)
                            {
                                cuentasPorCobrarSystem += venta.MontoCuota3;
                            }
                        }

                        // Agregar comparaciones
                        AgregarCotejo("Ventas Totales", datosCashea.VentasTotales, ventasTotalesSystem);
                        AgregarCotejo("Pago en Caja (Inicial)", datosCashea.PagadoCaja, pagadoCajaSystem);
                        AgregarCotejo("Monto Financiado", datosCashea.MontoFinanciado, financiadoSystem);
                        AgregarCotejo("Recibido en Banco (Bruto)", datosCashea.RecibidoBanco, recibidoBancoSystem);
                        AgregarCotejo("Pago Inicial en App", datosCashea.PagoInicialApp, pagoInicialAppSystem);
                        AgregarCotejo("Cuotas Adelantadas", datosCashea.CuotasAdelantadas, cuotasAdelantadasSystem);
                        AgregarCotejo("Banco Neto (Cuotas Reconocidas)", datosCashea.BancoNeto, bancoNetoSystem);
                        AgregarCotejo("Cuentas por Cobrar (Deuda Vencida)", datosCashea.CuentasPorCobrar, cuentasPorCobrarSystem);

                        // Guardar en el estado de sesión global
                        SessionState.LastDatosCashea = datosCashea;
                        SessionState.LastItemsCotejo = ItemsCotejo.ToList();
                        SessionState.LastDetallesDiferencias = DetallesDiferencias.ToList();
                        SessionState.LastReportPath = openFileDialog.FileName;
                    }

                    OnPropertyChanged(nameof(HayDiferencias));
                    MostrarPanelCotejo = true;
                    MensajeEstado = "Cotejo de reporte mensual completado exitosamente.";
                }
                catch (Exception ex)
                {
                    MensajeEstado = "Error al cotejar el reporte mensual.";
                    MessageBox.Show($"Ocurrió un error al procesar el reporte mensual: {ex.Message}", "Error de Cotejo", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private void AgregarCotejo(string concepto, decimal valorCashea, decimal valorSistema, string suffix = " $")
        {
            decimal diff = Math.Abs(valorCashea - valorSistema);
            
            // Tolerancia inteligente: 5$ para ventas y caja, 85$ para montos convertidos por tasa
            decimal limiteTolerancia = 5.00m;
            if (concepto.Contains("Banco") || concepto.Contains("Adelantadas") || concepto.Contains("Cuentas por Cobrar"))
            {
                limiteTolerancia = 85.00m;
            }

            bool coincide = diff <= limiteTolerancia;

            var item = new ItemCotejo
            {
                Concepto = concepto,
                ValorCashea = string.Format("{0:N2}{1}", valorCashea, suffix),
                ValorSistema = string.Format("{0:N2}{1}", valorSistema, suffix),
                Diferencia = string.Format("{0:N2}{1}", valorCashea - valorSistema, suffix),
                Estado = coincide ? "✔️ Coincide" : "❌ Discrepancia",
                Coincide = coincide
            };
            ItemsCotejo.Add(item);
        }

        private decimal ObtenerTasaFechaValor(DateTime fechaMov, List<TasaDiaria> tasas)
        {
            var fechaBase = fechaMov.Date;
            DateTime fechaBusqueda;
            if (fechaBase.DayOfWeek == DayOfWeek.Saturday)
                fechaBusqueda = fechaBase.AddDays(-2);
            else if (fechaBase.DayOfWeek == DayOfWeek.Sunday)
                fechaBusqueda = fechaBase.AddDays(-3);
            else
                fechaBusqueda = fechaBase.AddDays(-1);

            var tasaAplicable = tasas
                .OrderByDescending(t => t.Fecha)
                .FirstOrDefault(t => t.Fecha.Date <= fechaBusqueda);

            if (tasaAplicable != null && tasaAplicable.TasaBcvVes > 0)
                return tasaAplicable.TasaBcvVes;

            var fallback = tasas.FirstOrDefault(t => t.Fecha.Date == fechaBase);
            if (fallback != null && fallback.TasaBcvVes > 0)
                return fallback.TasaBcvVes;

            return 36.0m;
        }

        private bool EsPagoDeFactura(string referencia, string factura)
        {
            if (string.IsNullOrEmpty(referencia) || string.IsNullOrEmpty(factura)) return false;
            string r = referencia.Trim();
            string f = factura.Trim();
            if (r.All(char.IsDigit) && f.All(char.IsDigit))
            {
                return r == f;
            }
            return r.Contains(f) || f.Contains(r);
        }

        private void VerDetallesDiferencias()
        {
            var win = new SurtidorADM.Views.DetalleErroresCotejoWindow(DetallesDiferencias);
            win.Owner = System.Windows.Application.Current.MainWindow;
            win.ShowDialog();
        }

        private readonly GeminiChatService _geminiChatService = new();

        private async Task EnviarMensajeChatAsync()
        {
            string userMsg = MensajeChatTexto.Trim();
            if (string.IsNullOrEmpty(userMsg)) return;

            // Limpiar caja de texto
            MensajeChatTexto = string.Empty;

            // Agregar mensaje del usuario a la lista
            MensajesChat.Add(new ItemMensajeChat { Contenido = userMsg, EsUsuario = true });

            IsBusy = true;
            MensajeEstado = "El Asistente IA está analizando los datos...";

            try
            {
                // Compilar el contexto en memoria (cotejo de reporte mensual y discrepancias de esta sesión)
                var sbContexto = new StringBuilder();
                if (ItemsCotejo != null && ItemsCotejo.Any())
                {
                    sbContexto.AppendLine("=== COTEJO DE REPORTE MENSUAL EN PANTALLA (DATOS EN MEMORIA DE ESTA SESIÓN) ===");
                    foreach (var item in ItemsCotejo)
                    {
                        sbContexto.AppendLine($"- Concepto: {item.Concepto} | Monto Reporte: {item.ValorCashea} | Monto Sistema: {item.ValorSistema} | Diferencia: {item.Diferencia} | Estado: {item.Estado} | Coincide: {(item.Coincide ? "Sí" : "No")}");
                    }
                    sbContexto.AppendLine();
                }

                if (DetallesDiferencias != null && DetallesDiferencias.Any())
                {
                    sbContexto.AppendLine("=== RESUMEN DE DISCREPANCIAS INDIVIDUALES ===");
                    sbContexto.AppendLine($"Total general de transacciones con discrepancia: {DetallesDiferencias.Count}");

                    decimal LocalParseDecimal(string valStr)
                    {
                        if (string.IsNullOrWhiteSpace(valStr)) return 0;
                        string clean = System.Text.RegularExpressions.Regex.Replace(valStr, @"[^\d\.\-]", "");
                        if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal res))
                        {
                            return res;
                        }
                        return 0;
                    }

                    var grupos = DetallesDiferencias
                        .GroupBy(d => d.TipoError)
                        .Select(g => new { 
                            Tipo = g.Key, 
                            Cant = g.Count(), 
                            Suma = g.Sum(x => LocalParseDecimal(x.ImpactoUsd)) 
                        });

                    foreach (var grp in grupos)
                    {
                        sbContexto.AppendLine($"- {grp.Tipo}: {grp.Cant} casos | Impacto total: {grp.Suma:N2} USD");
                    }
                    sbContexto.AppendLine();

                    // Incluir las 15 discrepancias con mayor impacto absoluto para contexto visual
                    sbContexto.AppendLine("=== TOP 15 DISCREPANCIAS MAYORES (IMPACTO ABSOLUTO) ===");
                    var top15 = DetallesDiferencias
                        .OrderByDescending(d => Math.Abs(LocalParseDecimal(d.ImpactoUsd)))
                        .Take(15);
                    foreach (var err in top15)
                    {
                        sbContexto.AppendLine($"- Ref: {err.Referencia} | Fecha: {err.Fecha} | Tipo: {err.TipoError} | Detalle: {err.Detalle} | Impacto: {err.ImpactoUsd}");
                    }
                    sbContexto.AppendLine();

                    // Buscar coincidencias específicas si el usuario consulta una referencia en su mensaje
                    var refsEncontradas = new List<string>();
                    var matches = System.Text.RegularExpressions.Regex.Matches(userMsg, @"\b\d{6,12}\b");
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        string refBusqueda = match.Value;
                        var coincidentes = DetallesDiferencias
                            .Where(d => d.Referencia == refBusqueda || (d.Detalle != null && d.Detalle.Contains(refBusqueda)))
                            .ToList();
                        
                        if (coincidentes.Any() && !refsEncontradas.Contains(refBusqueda))
                        {
                            refsEncontradas.Add(refBusqueda);
                            sbContexto.AppendLine($"=== INFORMACION EXTRA DE TRANSACCION CONSULTADA ({refBusqueda}) ===");
                            foreach (var err in coincidentes)
                            {
                                sbContexto.AppendLine($"- Ref: {err.Referencia} | Fecha: {err.Fecha} | Tipo: {err.TipoError} | Detalle: {err.Detalle} | Impacto: {err.ImpactoUsd}");
                            }
                            sbContexto.AppendLine();
                        }
                    }
                }

                // Llamado asíncrono a la IA enviándole el contexto de la base de datos + datos de la sesión actual
                string respuestaIa = await _geminiChatService.ResponderPreguntaAsync(MensajesChat.ToList(), sbContexto.ToString());
                
                // Agregar respuesta de la IA a la lista
                MensajesChat.Add(new ItemMensajeChat { Contenido = respuestaIa, EsUsuario = false });
                
                MensajeEstado = "Listo.";
            }
            catch (Exception ex)
            {
                MensajesChat.Add(new ItemMensajeChat { Contenido = $"❌ Error: No se pudo obtener respuesta de la IA. {ex.Message}", EsUsuario = false });
                MensajeEstado = "Error en comunicación con la IA.";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    public class DetalleDiferencia
    {
        public string Referencia { get; set; }
        public string Fecha { get; set; }
        public string TipoError { get; set; }
        public string Detalle { get; set; }
        public decimal MontoBsCashea { get; set; }
        public decimal MontoBsBanco { get; set; }
        public decimal TasaCashea { get; set; }
        public decimal TasaBcvSistema { get; set; }
        public string ImpactoUsd { get; set; }
    }

    public class ItemCotejo
    {
        public string Concepto { get; set; }
        public string ValorCashea { get; set; }
        public string ValorSistema { get; set; }
        public string Diferencia { get; set; }
        public string Estado { get; set; }
        public bool Coincide { get; set; }
    }
}