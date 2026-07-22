using System;

namespace SurtidorADM.Models
{
    public class ItemMensajeChat
    {
        public string Contenido { get; set; } = string.Empty;
        public bool EsUsuario { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;

        // Propiedades de ayuda para la visualización en la UI
        public string Alineacion => EsUsuario ? "Right" : "Left";
        public string ColorBurbuja => EsUsuario ? "#3B82F6" : "#E5E7EB"; // Azul para usuario, Gris claro para la IA
        public string ColorTexto => EsUsuario ? "White" : "Black";
    }
}
