using System;
using System.ComponentModel.DataAnnotations;

namespace SurtidorADM.Models
{
    // Histórico de tasas (se mantiene intacto)
    public class TasaDiaria
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public decimal TasaBcvVes { get; set; }
        public decimal TasaFronteraCop { get; set; }
    }

    // NUEVO: Modelo para el Reporte de Pagos (Liquidaciones en lote)
    public class PagoLiquidacionCashea
    {
        [Key]
        public int Id { get; set; }

        public string IdPago { get; set; }

        public DateTime FechaLiquidacion { get; set; }

        public string ReferenciaBancaria { get; set; }

        public decimal TotalDepositadoBs { get; set; } // Columna C

        public decimal TotalDepositadoUsd { get; set; } // Columna E

        public string Estado { get; set; }
        public int CantidadCuotas { get; set; }
        public decimal MontoFinanciado { get; set; }

        // Nuevos campos para mapear la orden y la cuota pagada
        public string? IdOrden { get; set; } // Columna J (Orden)
        public int NroCuotaPagada { get; set; } // Columna H (Nro cuota pagada)
    }


    // NUEVO: Modelo para el Reporte de Ventas (Desglose individual)
    public class VentaIndividualCashea
    {
        [Key]
        public int Id { get; set; }

        public string IdOrden { get; set; }        // Columna A
        public string NroFactura { get; set; }     // Columna B
        public string Sucursal { get; set; }       // Columna C
        public decimal VentaTotalUsd { get; set; } // Columna D
        public DateTime FechaCompra { get; set; }  // Columna E
        public decimal PagadoCajaUsd { get; set; } // Columna F
        public decimal MontoFinanciado { get; set; } // Columna G
        public string Estatus { get; set; }        // Columna H

        // Fechas y montos de cuotas
        public DateTime? FechaCuota1 { get; set; } // Columna K (11)
        public decimal MontoCuota1 { get; set; }    // Columna L (12)
        public DateTime? FechaCuota2 { get; set; } // Columna M (13)
        public decimal MontoCuota2 { get; set; }    // Columna N (14)
        public DateTime? FechaCuota3 { get; set; } // Columna O (15)
        public decimal MontoCuota3 { get; set; }    // Columna P (16)
    }
    // Modelo del Banco (se mantiene, pero ajustado para cruzar con el PagoLiquidacionCashea)
    public class MovimientoBancario
    {
        [Key]
        public int Id { get; set; }

        public DateTime Fecha { get; set; }        // Columna A
        public string ReferenciaBanco { get; set; } // Columna B
        public string Descripcion { get; set; }    // Columna C
        public decimal MontoAbonadoVes { get; set; } // Columna D (Solo si > 0)
    }

    // DTO (Data Transfer Object) exclusivo para mostrar los resultados en la grilla visual
    public class ResultadoConciliacion
    {
        public string Referencia { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal MontoCasheaUsd { get; set; }
        public decimal TasaBcvAplicada { get; set; }
        public decimal MontoEsperadoVes { get; set; }
        public decimal MontoEnBancoVes { get; set; }
        public decimal DiferenciaVes { get; set; }
        public string Estatus { get; set; } = string.Empty; // "Éxito", "Diferencia", "No Encontrado"
        public decimal MontoCasheaBs { get; set; }
    }
}