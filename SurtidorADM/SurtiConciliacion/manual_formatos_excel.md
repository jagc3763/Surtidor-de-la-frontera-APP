# Guía de Formatos de Excel: SurtidorADM

Para que el sistema de conciliación lea los archivos de Excel de forma automática y correcta, los documentos deben cumplir con una estructura de columnas específica. Esta guía detalla cómo configurar y guardar cada archivo.

---

## Reglas Generales de Guardado
1. **Formato de Archivo**: Todos los archivos deben estar guardados en formato **Libro de Excel (`.xlsx`)**. No se admiten formatos antiguos (`.xls`) o archivos de texto delimitados (`.csv`).
2. **Hojas de Cálculo**: El sistema busca palabras clave en el nombre de las pestañas para procesarlas. Si no encuentra las palabras clave, procesará la **primera pestaña** por defecto.
3. **Encabezados**: La primera fila del archivo se asume como encabezado y es ignorada automáticamente. Los datos reales deben iniciar a partir de la **fila 2**.
4. **Fechas**: Se admiten los siguientes formatos de fecha:
   - `dd/MM/yyyy HH:mm:ss` (ej: `15/05/2026 14:30:00`)
   - `dd/MM/yyyy HH:mm` (ej: `15/05/2026 14:30`)
   - `dd/MM/yyyy` (ej: `15/05/2026`)
   - `yyyy-MM-dd` (ej: `2026-05-15`)

---

## 1. Archivo de Lotes Cashea (Liquidaciones)
Este archivo contiene las transferencias consolidadas enviadas por Cashea a tu banco.

* **Palabra clave de la pestaña**: El nombre de la pestaña debe contener alguna de estas palabras: `reporte cashea`, `pago`, `lote` o `liquidaci`.
* **Estructura de Columnas**:

| Columna | Letra | Campo Requerido | Tipo de Dato / Ejemplo |
| :--- | :---: | :--- | :--- |
| **Columna 1** | **A** | Fecha de Liquidación | Fecha (ej. `01/05/2026`) |
| **Columna 2** | **B** | Referencia Bancaria | Texto/Número (ej. `12155364679`) |
| **Columna 3** | **C** | Total Depositado Bs | Número Decimal (ej. `300.50`) |
| **Columna 5** | **E** | Total Depositado USD | Número Decimal (ej. `8.23`) |
| **Columna 6** | **F** | Estado | Texto (ej. `CONCILIADO`) |
| **Columna 8** | **H** | Nro Cuota Pagada | Número Entero (`1`, `2` o `3`) |
| **Columna 10** | **J** | Número de Orden | Texto/Número (ej. `93667491`) |

---

## 2. Archivo de Ventas Cashea (Ventas Individuales)
Contiene el desglose de cada factura, la inicial que pagó el cliente y las cuotas de crédito financiadas.

* **Palabra clave de la pestaña**: El nombre de la pestaña debe contener alguna de estas palabras: `venta`, `factura` o `individual`.
* **Estructura de Columnas**:

| Columna | Letra | Campo Requerido | Tipo de Dato / Ejemplo |
| :--- | :---: | :--- | :--- |
| **Columna 1** | **A** | ID de Orden | Texto/Número (ej. `101153366`) |
| **Columna 2** | **B** | Nro Factura | Texto/Número (ej. `1553`) |
| **Columna 3** | **C** | Sucursal | Texto (ej. `Sucursal San Cristobal`) |
| **Columna 4** | **D** | Venta Total USD | Número Decimal (ej. `47.20`) |
| **Columna 5** | **E** | Fecha de Compra | Fecha (ej. `25/04/2026`) |
| **Columna 6** | **F** | Pagado en Caja USD | Número Decimal (ej. `18.88`) |
| **Columna 7** | **G** | Monto Financiado USD | Número Decimal (ej. `28.32`) |
| **Columna 8** | **H** | Estatus | Texto (ej. `ACTIVO`) |
| **Columna 11** | **K** | Fecha de Cuota 1 | Fecha (ej. `10/05/2026`) |
| **Columna 12** | **L** | Monto de Cuota 1 | Número Decimal (ej. `9.44`) |
| **Columna 13** | **M** | Fecha de Cuota 2 | Fecha (ej. `25/05/2026`) |
| **Columna 14** | **N** | Monto de Cuota 2 | Número Decimal (ej. `9.44`) |
| **Columna 15** | **O** | Fecha de Cuota 3 | Fecha (ej. `10/06/2026`) |
| **Columna 16** | **P** | Monto de Cuota 3 | Número Decimal (ej. `9.44`) |

---

## 3. Estado de Cuenta de Banesco
Contiene los movimientos bancarios de la cuenta del comercio.

* **Palabra clave de la pestaña**: El nombre de la pestaña debe contener alguna de estas palabras: `extracto`, `banco`, `banesco` o `movimiento`.
* **Estructura de Columnas**:

| Columna | Letra | Campo Requerido | Tipo de Dato / Ejemplo |
| :--- | :---: | :--- | :--- |
| **Columna 1** | **A** | Fecha del Movimiento | Fecha (ej. `01/05/2026 08:30:15`) |
| **Columna 2** | **B** | Referencia Bancaria | Texto/Número (ej. `12155364679`) |
| **Columna 3** | **C** | Descripción | Texto (ej. `TRANSFERENCIA RECIBIDA`) |
| **Columna 4** | **D** | Monto Abonado (VES) | Número Decimal (ej. `300.50`) |

> [!NOTE]
> El sistema ignora automáticamente los débitos, cargos y comisiones bancarias. Solo procesará registros en la Columna D que tengan un monto estrictamente mayor a cero (`> 0`).

---

## 4. Archivo del Histórico de Tasas del BCV
Se utiliza para cargar de forma masiva el histórico oficial del BCV.

* **Nombre de las Hojas (Pestañas)**: Cada pestaña del libro de Excel representa un día y debe llamarse exactamente en formato **`ddMMyyyy`** (8 dígitos numéricos).
  - *Ejemplo*: Una pestaña llamada `01052026` representa el 1 de mayo de 2026.
* **Celda de Lectura**: El valor de la tasa oficial en Bolívares debe estar escrito únicamente en la celda **`G15`** de la hoja correspondiente. El sistema ignorará todo el resto de la hoja.
