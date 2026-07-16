using SurtidorADM.Data;
using SurtidorADM.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SurtidorADM.Services
{
    public class MotorConciliacionService
    {
        private decimal TruncarA3Decimales(decimal valor)
        {
            return Math.Truncate(valor * 1000m) / 1000m;
        }

        public async Task<List<ResultadoConciliacion>> EjecutarConciliacionAsync()
        {
            var resultados = new List<ResultadoConciliacion>();

            using var context = new SurtidorDbContext();

            var pagosCashea = await context.PagosLiquidacionesCashea.ToListAsync();
            var movimientosBanco = await context.MovimientosBancarios.ToListAsync();
            var tasasBcv = await context.TasasDiarias.ToListAsync();

            foreach (var pago in pagosCashea)
            {
                string refCashea = (pago.ReferenciaBancaria ?? "").Trim();

                // --- NUEVA LÓGICA: Sábado/Domingo buscan JUEVES (T-2 o T-3) ---
                DateTime fechaBase = pago.FechaLiquidacion.Date;
                DateTime fechaBusqueda;

                if (fechaBase.DayOfWeek == DayOfWeek.Saturday)
                    fechaBusqueda = fechaBase.AddDays(-2); // Sábado -> Jueves
                else if (fechaBase.DayOfWeek == DayOfWeek.Sunday)
                    fechaBusqueda = fechaBase.AddDays(-3); // Domingo -> Jueves
                else
                    fechaBusqueda = fechaBase.AddDays(-1); // Lunes a Viernes -> T-1

                // Buscamos la tasa vigente hasta la fecha calculada
                var tasaAplicable = tasasBcv
                    .Where(t => t.Fecha.Date <= fechaBusqueda.Date)
                    .OrderByDescending(t => t.Fecha)
                    .FirstOrDefault();

                var resultado = new ResultadoConciliacion
                {
                    Referencia = refCashea,
                    Fecha = pago.FechaLiquidacion,
                    MontoCasheaUsd = TruncarA3Decimales(pago.TotalDepositadoUsd)
                };

                if (tasaAplicable == null)
                {
                    resultado.Estatus = "Error: Sin Tasa (Buscada: " + fechaBusqueda.ToString("dd/MM/yyyy") + ")";
                }
                else
                {
                    decimal tasaReal = tasaAplicable.TasaBcvVes;

                    resultado.TasaBcvAplicada = tasaReal;
                    resultado.MontoEsperadoVes = Math.Round(pago.TotalDepositadoUsd * tasaReal, 2);
                    resultado.MontoCasheaBs = TruncarA3Decimales(resultado.MontoCasheaUsd * tasaReal);

                    var movimientoBanco = movimientosBanco
                        .FirstOrDefault(m => (m.ReferenciaBanco ?? "").Trim() == refCashea);

                    if (movimientoBanco != null)
                    {
                        resultado.MontoEnBancoVes = movimientoBanco.MontoAbonadoVes;
                        resultado.DiferenciaVes = Math.Round(resultado.MontoEsperadoVes - resultado.MontoEnBancoVes, 2);

                        // Si la diferencia es menor o igual a 0.05, es éxito
                        if (Math.Abs(resultado.DiferenciaVes) <= 0.05m)
                        {
                            resultado.Estatus = "Éxito";
                        }
                        else
                        {
                            // Diagnóstico: mostramos la fecha de tasa aplicada
                            resultado.Estatus = "Dif: " + resultado.DiferenciaVes.ToString("F2") + " (Tasa Fecha: " + tasaAplicable.Fecha.ToString("dd/MM") + ")";
                        }
                    }
                    else
                    {
                        resultado.Estatus = "Falta en Banco";
                    }
                }
                resultados.Add(resultado);
            }

            return resultados.OrderBy(r => r.Fecha).ToList();
        }
    }
}