# SurtidorADM

SurtidorADM es un sistema inteligente de administración y conciliación financiera diseñado a medida para **El Surtidor de la Frontera, C.A.** Permite auditar y cotejar de manera precisa los reportes de ventas y liquidaciones del aliado **Cashea** frente a los registros de caja y los estados de cuenta bancarios reales (Banesco).

## 🚀 Módulos Principales
1. **Conciliación de Lotes Cashea:** Cruce inteligente de transacciones bancarias contra reportes de liquidaciones, identificando descuadres de tasa cambiaria (BCV), diferencias de depósitos en bolívares, pagos omitidos y depósitos huérfanos.
2. **Auditoría de Cierre Mensual:** Panel interactivo de comparación que al hacer doble clic despliega ventanas de análisis detallado sobre variaciones por prorrateos cambiarios y clasificaciones de cuotas adelantadas.
3. **Control Bancario e Historial de Tasas:** Registro contable diario con histórico de tasas BCV e ingresos bancarios en bolívares y dólares.

## 🛠️ Stack Tecnológico
* **Framework:** .NET 10.0-windows (WPF)
* **Lenguaje:** C# 14
* **Base de Datos:** SQLite 3 (Entity Framework Core 10)
* **Lectura Excel:** ClosedXML
* **Estilo Visual:** Diseño premium con paleta de colores corporativos e interfaces interactivas fluidas.

## 🔄 Sistema de Auto-Actualizaciones
El sistema incorpora un servicio inteligente de auto-actualización en caliente que se conecta a GitHub para buscar nuevas versiones, permitiendo actualizar a los usuarios sin interrumpir o vaciar sus bases de datos locales.
