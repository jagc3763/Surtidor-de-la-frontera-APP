using System;
using System.Windows;
using SurtidorADM.ViewModels;

namespace SurtidorADM.Views
{
    /// <summary>
    /// Lógica de interacción para ExplicacionDiferenciaWindow.xaml
    /// </summary>
    public partial class ExplicacionDiferenciaWindow : Window
    {
        public ExplicacionDiferenciaWindow(ItemCotejo item)
        {
            InitializeComponent();
            
            TxtConcepto.Text = item.Concepto;
            TxtCashea.Text = item.ValorCashea;
            TxtSistema.Text = item.ValorSistema;
            TxtDiferencia.Text = item.Diferencia;

            // Highlight difference in green if zero/match, orange/red otherwise
            if (item.Coincide)
            {
                TxtDiferencia.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                TxtDiferencia.Foreground = System.Windows.Media.Brushes.Red;
            }

            CargarExplicacion(item.Concepto);
        }

        private void CargarExplicacion(string concepto)
        {
            // Obtener el ViewModel de la ventana principal
            MainViewModel vm = null;
            foreach (Window win in Application.Current.Windows)
            {
                if (win is MainWindow)
                {
                    vm = win.DataContext as MainViewModel;
                    break;
                }
            }

            // Calcular acumulados de discrepancias de la base de datos
            decimal totalTasaUsd = 0;
            decimal totalDescuadreUsd = 0;
            decimal totalFaltanteBancoUsd = 0;
            decimal totalFaltanteLotesUsd = 0;

            int countTasa = 0;
            int countDescuadre = 0;
            int countFaltanteBanco = 0;
            int countFaltanteLotes = 0;

            if (vm != null && vm.DetallesDiferencias != null)
            {
                foreach (var diff in vm.DetallesDiferencias)
                {
                    string cleanUsd = (diff.ImpactoUsd ?? "").Replace("$", "").Replace(" ", "").Trim();
                    decimal.TryParse(cleanUsd, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val);

                    if (diff.TipoError == "Diferencia de Tasa BCV")
                    {
                        totalTasaUsd += val;
                        countTasa++;
                    }
                    else if (diff.TipoError == "Descuadre de Depósito Bancario")
                    {
                        totalDescuadreUsd += val;
                        countDescuadre++;
                    }
                    else if (diff.TipoError == "Falta en Banco")
                    {
                        totalFaltanteBancoUsd += val;
                        countFaltanteBanco++;
                    }
                    else if (diff.TipoError == "Falta en Lotes Cashea")
                    {
                        totalFaltanteLotesUsd += val;
                        countFaltanteLotes++;
                    }
                }
            }

            if (concepto.Contains("Ventas Totales"))
            {
                TxtExplicacion.Text = "Representa el monto total facturado de las órdenes del período. " +
                    "Si existe una diferencia, se debe a que el rango de fechas seleccionado contiene órdenes del sistema " +
                    "que aún no se han reportado en los archivos cargados, o viceversa.";
                TxtAccionRecomendada.Text = "Verifica que el archivo de Ventas Individuales cargado cubra exactamente el mismo rango de fechas que el reporte de Cashea.";
            }
            else if (concepto.Contains("Pago en Caja"))
            {
                TxtExplicacion.Text = "Corresponde al pago inicial (cuota inicial) que los clientes realizaron directamente en la caja física " +
                    "de tu comercio (generalmente el 40%). Debe cuadrar exactamente con lo cobrado en tu punto de venta.";
                TxtAccionRecomendada.Text = "Verifica los recibos físicos de la caja de las órdenes cargadas si hay discrepancias significativas.";
            }
            else if (concepto.Contains("Monto Financiado"))
            {
                TxtExplicacion.Text = "Representa el total acumulado que fue financiado por Cashea (las cuotas diferidas, normalmente el 60%). " +
                    "Debe coincidir de manera matemática con el financiamiento de las órdenes registradas.";
                TxtAccionRecomendada.Text = "Confirma que no falten órdenes individuales por importar en la pestaña del sistema.";
            }
            else if (concepto.Contains("Recibido en Banco"))
            {
                TxtExplicacion.Text = "Este renglón concilia el dinero que ingresó físicamente a tu cuenta bancaria. La diferencia total detectada se desglosa en los siguientes hallazgos:\n\n" +
                    $"• 💸 {countTasa} depósitos con variaciones por tipo de cambio (tasa BCV diaria vs tasa fija Cashea). Impacto: {totalTasaUsd:N2} $.\n" +
                    $"• ⚠️ {countDescuadre} depósitos con descuadres de montos en bolívares depositados. Impacto: {totalDescuadreUsd:N2} $.\n" +
                    $"• 🔍 {countFaltanteBanco} transacciones declaradas por Cashea que no impactaron el banco. Impacto: {totalFaltanteBancoUsd:N2} $.\n" +
                    $"• 📊 {countFaltanteLotes} abonos recibidos en banco sin reporte de lote de Cashea. Impacto: {totalFaltanteLotesUsd:N2} $.";
                TxtAccionRecomendada.Text = "Haz clic en 'Ver Detalle de Discrepancias' para ver el listado de cada una de estas transacciones con sus respectivas referencias.";
            }
            else if (concepto.Contains("Pago Inicial en App"))
            {
                TxtExplicacion.Text = "Corresponde a pagos iniciales (primeras cuotas) de clientes que fueron pagados desde la aplicación de Cashea " +
                    "en lugar de pagarse en la caja física de la tienda, y que posteriormente Cashea transfiere al comercio.";
                TxtAccionRecomendada.Text = "Concilia los pagos iniciales de app con los lotes bancarios recibidos.";
            }
            else if (concepto.Contains("Cuotas Adelantadas"))
            {
                TxtExplicacion.Text = "Son abonos recibidos en el banco durante el mes actual que corresponden a cuotas de vencimientos futuros (meses siguientes).\n\n" +
                    "La diferencia de clasificación se debe a los pagos adelantados completos de los clientes. " +
                    "Mientras Cashea separa el dinero de la cuota del mes corriente y la del mes futuro, nuestro sistema lee el depósito bancario unificado bajo la cuota límite pagada.";
                TxtAccionRecomendada.Text = "Compara la suma conjunta de Banco Neto y Cuotas Adelantadas con el reporte; verás que la diferencia neta agregada es menor a 2.50 $ (redondeo de tasas diarias).";
            }
            else if (concepto.Contains("Banco Neto"))
            {
                TxtExplicacion.Text = "Es el subtotal neto de banco (Recibido en Banco - Cuotas Adelantadas - Pago Inicial en App). Representa las cuotas del período actual conciliadas al corte.\n\n" +
                    "Al igual que en las Cuotas Adelantadas, la diferencia se debe al prorrateo de pagos adelantados completos que realiza Cashea en sus reportes frente a la lectura unificada bancaria de nuestro sistema.";
                TxtAccionRecomendada.Text = "Revisa la suma global de banco en conjunto para mayor tranquilidad.";
            }
            else if (concepto.Contains("Cuentas por Cobrar"))
            {
                TxtExplicacion.Text = "Representa el total de las cuotas del comercio cuyo vencimiento estaba pautado para el mes en curso. " +
                    "Cashea reporta esto como el total del agendamiento. Si la base de datos difiere, es porque existen diferencias en las fechas de compra " +
                    "o en los plazos de cuotas registradas en el sistema.";
                TxtAccionRecomendada.Text = "Revisa las fechas de vencimiento de las cuotas de las ventas individuales en el módulo de ventas.";
            }
            else
            {
                TxtExplicacion.Text = "Comparación consolidada mensual entre el reporte del aliado (Cashea) y los registros cargados en la base de datos local.";
                TxtAccionRecomendada.Text = "Revisa el estado de la conciliación global para identificar diferencias.";
            }
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
