using System.Collections.Generic;
using SurtidorADM.Services;
using SurtidorADM.ViewModels;

namespace SurtidorADM.Models
{
    public static class SessionState
    {
        public static CasheaReporteDatos? LastDatosCashea { get; set; }
        public static List<ItemCotejo>? LastItemsCotejo { get; set; }
        public static List<DetalleDiferencia>? LastDetallesDiferencias { get; set; }
        public static string? LastReportPath { get; set; }
    }
}
