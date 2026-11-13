# P1 - Prototipo offline de componentes documentales

Experimento aislado. No es parte del API, de la solución .NET ni del frontend. No abre conexiones de base de datos, interpreta dibujos, usa OCR/IA, modifica documentos, crea tareas/checklists o implementa roles. Nunca devuelve productionEligible=true.

## Archivos creados

- `extract_components.py`: inventario ZIP en memoria, lectura PDFium con posiciones, plantillas cerradas, validaciones, expansión determinista y CLI.
- `reporting.py`: informe Markdown y matriz agregada a partir de los resultados.
- `test_extract_components.py`: suite sintética, incluidos PDF mínimo y ZIP adversos, sin datos privados.
- `verify_corpus.py`: regresión optativa de los tres ZIP externos; cifras y 12 filas contrastadas visualmente, sin incluir documentos originales.
- `requirements.txt`: única dependencia experimental fijada; no altera dependencias .NET/Next.
- `.gitignore`: excluye caches y entorno virtual.
- `README.md`: uso, contrato y limitaciones.

No se versionan ZIP, PDF, imágenes, texto extraído ni salidas. **El resultado incluye contenido privado**: guardarlo fuera de repositorios con acceso local apropiado. La CLI rechaza una carpeta de salida dentro de un worktree Git y exige una carpeta nueva para evitar sobrescrituras. No se modifican los ZIP.

## Ejecución

Python 3.11+ y pypdfium2 5.15.2. Crear un entorno aislado externo o reutilizar uno existente con la dependencia exacta:

```powershell
python -m venv <carpeta-externa>/venv
<carpeta-externa>/venv/Scripts/python -m pip install -r requirements.txt
<carpeta-externa>/venv/Scripts/python extract_components.py --output <salida-externa-nueva> <zip1> <zip2> <zip3>
```

Código de salida 0: requiere revisión, sin bloqueos de las reglas conocidas; **no significa apto para Producción**. Código 2: se generaron resultados con bloqueos documentales. Otros errores/excepciones: ejecución incompleta, no consumir como éxito. Los bloques de candidatos permanecen disponibles incluso cuando el documento está bloqueado.

```powershell
python -m unittest discover -s . -v
python verify_corpus.py <carpeta-externa-con-los-tres-ZIP>
```

La regresión optativa espera los nombres `Radicado 1162001.zip`, `Radicado 1162002.zip`, `Radicado 1162003.zip`. Es un oráculo de estas muestras, no una condición que el extractor use para alterar resultados. La CLI no espera 18 OP ni fija cantidades de las muestras.

## Contrato experimental

`result.json` por corpus contiene el hash del ZIP, inventario de todos los archivos con hash, OP observadas en Estructuras, documentos objetivo y matriz. Cada documento tiene `rows`, `units`, `issues`, `lineAudit`, totales declarados/calculados, anotaciones y estado. `matrix.csv` agrupa OP/material/referencia/documento. `REPORT.md` y `summary.json` consolidan resultados.

En aluminio: Piece ID original, OP, serie/referencia/descripción, cantidad, Medida y CT separados, ángulos y estructura. Un bloque puede continuar entre páginas. Descripción ausente es null con aviso. En listas Vidrios: se conserva cantidad/LxH, composición documental y pesos disponibles; dimensiones sin unidad explícita no se transforman. Las anotaciones no se asignan automáticamente a filas. `material=glass` significa procedencia de la lista Vidrios; `materialVerified=false` evita confundir esa categoría con material validado, especialmente para lámina microperforada.

Las magnitudes se normalizan con Decimal y se serializan como cadenas; `originalText` conserva los valores de origen. Las ubicaciones contienen página, ordinal y caja de línea (puntos PDF, origen inferior izquierdo). La referencia de perfil conserva además su ubicación, aunque proceda de otra página.

ID de fila: SHA-256 de versión del prototipo, hash del corpus, hash del PDF y ubicación. ID de unidad: hash de ID de fila y ordinal. Son estables en ejecuciones idénticas, distintos para filas idénticas en ubicaciones diferentes. No son IDs de base de datos ni prometen continuidad entre nuevas revisiones/exportaciones. No se elimina una pieza solo por compartir medidas con otra. Repetición exacta de lectura en la misma ubicación se señala y no se expande dos veces; Piece ID repetido en otra ubicación bloquea y conserva las filas candidatas.

## Seguridad y límites

ZIP sin extracción física: rechaza traversal, rutas absolutas, symlinks, entradas repetidas y cifrado. Límites: 500 entradas, 30 MiB por entrada, 300 MiB total/ZIP, 100 páginas/PDF y 100000 unidades/documento. Los documentos no objetivo se inventarían explícitamente; barras de suministro nunca se suman como componentes. Plantilla desconocida, línea no clasificada, OP fuera del corpus, falta de referencia/posición/página y discrepancia de totales bloquean.

Se trata de un proceso local para muestras proporcionadas, **no de un servicio endurecido para archivos hostiles**. PDFium es código nativo; falta aislamiento de proceso, timeout/memoria estrictos, cancelación, pruebas adicionales de archivos adversos y empaquetado productivo. Los límites anteriores no sustituyen esas medidas. No agregar este CLI a una solicitud HTTP.

Solo se reconocen las plantillas textuales observadas. Se usa texto con cajas de ubicación; no reconstrucción general de tablas por geometría ni validación celda a celda. Un encabezado que falta puede bloquear por falta de contexto; una exportación que omite silenciosamente un encabezado intermedio requiere controles adicionales/corpus. No se infiere semántica de dibujos o totales mediante pesos/áreas.

## Integración futura

Partir de un release confirmado, versión y hashes exactos, verificar autorización y pertenencia StructureId/OP desde ese release. Persistir borradores independientes con trazabilidad y versión de parser; aprobación explícita y autorizada antes de generar unidades/checklists. Reconciliar revisiones sin reabrir evidencia ni duplicar producción. No asociar estas muestras a la base local. Las pruebas existentes .NET no se modifican y este experimento no requiere migración, build del frontend ni smoke de PostgreSQL.
