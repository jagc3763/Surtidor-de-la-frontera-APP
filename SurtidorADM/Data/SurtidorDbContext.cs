using Microsoft.EntityFrameworkCore;
using SurtidorADM.Models;
using System;
using System.IO;

namespace SurtidorADM.Data
{
    public class SurtidorDbContext : DbContext
    {
        // 1. Las tablas que existirán físicamente en tu archivo SQLite
        public DbSet<TasaDiaria> TasasDiarias { get; set; }
        public DbSet<PagoLiquidacionCashea> PagosLiquidacionesCashea { get; set; }
        public DbSet<VentaIndividualCashea> VentasIndividualesCashea { get; set; }
        public DbSet<MovimientoBancario> MovimientosBancarios { get; set; }


        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "surtidor.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 2. Configuración de Llaves Primarias (Primary Keys)

            modelBuilder.Entity<TasaDiaria>()
                .HasKey(t => t.Id);

            // El identificador único del lote de pago es su IdPago
            modelBuilder.Entity<PagoLiquidacionCashea>()
                .HasKey(p => p.IdPago);

            // El identificador único de cada venta en la caja es su IdOrden
            modelBuilder.Entity<VentaIndividualCashea>()
                .HasKey(v => v.IdOrden);

            // 3. Configurar Llave Primaria para el Banco usando el Id autoincremental
            modelBuilder.Entity<MovimientoBancario>()
                .HasKey(b => b.Id);
        }
    }
}