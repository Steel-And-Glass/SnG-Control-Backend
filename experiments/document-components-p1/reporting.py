"""Private review report; no original files or outputs belong in Git."""
from collections import Counter, defaultdict
from pathlib import Path


def write_report(results: list[dict], output: Path):
    text=['# P1 - Resultados del prototipo documental', '',
          'Prototipo experimental, sin IA/OCR. Todos los componentes y unidades son candidatos; productionEligible=false.', '',
          'Los ZIP son corpus independientes. La fuente de OP conocidas es la Lista de Estructuras de cada ZIP; no se consultó PostgreSQL.', '',
          '## Resumen', '', '| Radicado | OP | Filas aluminio | Unidades aluminio | Filas lista Vidrios | Unidades candidatas | Estado |',
          '|---|---:|---:|---:|---:|---:|---|']
    for r in results:
        by_kind={d['template']:d for d in r['documents']}
        a=by_kind.get('cut-v1',{});g=by_kind.get('rectangular-glass-v1',{})
        text.append(f"| {r['corpus']} | {len(r['knownOps'])} | {len(a.get('rows',[]))} | {a.get('calculatedTotal',0)} | {len(g.get('rows',[]))} | {g.get('calculatedTotal',0)} | {r['status']} |")
    text += ['', '## Matriz por OP', '', '| Radicado | OP | Filas aluminio | Unidades aluminio | Filas lista Vidrios | Unidades candidatas |', '|---|---|---:|---:|---:|---:|']
    for r in results:
        counts=defaultdict(lambda: [0,0,0,0])
        for d in r['documents']:
            for row in d['rows']:
                i=0 if row['material']=='aluminum' else 2
                counts[row['op']][i]+=1;counts[row['op']][i+1]+=row['quantity']
        for op,c in sorted(counts.items()):
            text.append(f"| {r['corpus']} | {op} | {' | '.join(map(str,c))} |")
    for r in results:
        text += ['', f"## {r['corpus']}", '', f"SHA-256 ZIP: `{r['archiveSha256']}`", '',
                 '### Inventario completo', '', '| Entrada | Bytes | SHA-256 | Tratamiento |', '|---|---:|---|---|']
        for f in r['inventory']:
            name=f['entry'].replace('|','\\|')
            text.append(f"| {name} | {f['bytes']} | {f['sha256']} | {f['status']} |")
        text += ['', '### Diferencias y campos no reconocidos', '']
        for issue in r['issues']:
            text.append(f"- {issue['code']}: {issue}")
        for d in r['documents']:
            text += [f"- **{d['document']}**: {len(d['rows'])} filas; total calculado {d['calculatedTotal']}; totales declarados {sorted({x['quantity'] for x in d['declaredTotals']})}."]
            counts=Counter(i['code'] for i in d['issues'])
            text.append('  Hallazgos: '+('; '.join(f'{k}: {v}' for k,v in counts.items()) or 'ninguno en las reglas implementadas')+'.')
            unknown=[x for x in d['lineAudit'] if x['classification']=='unclassified']
            text.append(f'  Líneas sin clasificar: {len(unknown)}. Ver result.json para ubicación y texto exactos.')
    text += ['', '## Interpretación obligatoria', '',
             '- 1162001: la lista de Vidrios declara 28, pero las filas suman 35. No se corrigió ninguno de esos valores.',
             '- 1162002 y 1162003: ANGU0061 no tiene descripción en el bloque; se preserva null y se informa.',
             '- 1162003: anotación LÁMINA MICROPERFORADA y fila sin pesos. Se conserva la anotación documental sin inventar su vínculo automático; el documento queda bloqueado. Los siete candidatos no equivalen a siete vidrios aprobados.',
             '- Ninguna lista de Vidrios declara explícitamente la unidad de LxH en el encabezado observado: dimensionUnit=null; no se inventa mm.',
             '- Materiales es suministro en barras, no una fuente adicional de componentes. Modulación, planos, secciones, tickets e imágenes se inventarían, pero no generan piezas.',
             '- Los nombres de entrada ZIP con codificación dañada se conservan tal como el ZIP los expone; no se renombra ni modifica el original.', '',
             '## Evidencia y límites', '',
             'Se contrastaron visualmente las tablas nuevas y se reutilizó la inspección P0 del primer paquete. La regresión del corpus comprueba 12 filas de referencia anotadas desde las páginas (6 aluminio y 6 lista Vidrios), conteos, integridad, coordenadas presentes e identidad determinista. La coincidencia de esos ejemplos no es una estimación estadística de precisión sobre documentos futuros.', '',
             'Hay 127 filas de aluminio y 21 de listas de Vidrios en las muestras. No se encontraron líneas sin clasificar en las seis fuentes objetivo bajo las plantillas implementadas. No se dispone de una doble anotación independiente de todas las celdas; no se declara precisión global del 100%. Los totales de aluminio coinciden con 260/62/59 y los de vidrio 35/7/7 se conservan como lectura, no como aprobación.', '',
             'Las posiciones son cajas de líneas en puntos PDF, origen inferior izquierdo; no segmentación certificada de cada celda. Se conserva orden textual PDFium para perfiles, con encabezados/paginación y fallos explícitos. Otras variantes, filas partidas, fuentes corruptas o texto fuera de orden necesitan nuevas pruebas. No hay reconocimiento de dibujos, OCR, consumo de tickets ni un sandbox de proceso/timeout productivo.', '',
             '## Integración futura con OpeningFilingRelease', '',
             'Usar VersionId + ManifestSha256 + DocumentId/Sha256 como fuente inmutable y autorizar el acceso por backend. Sustituir la referencia documental local de OP por los integrantes exactos del release; fallar ante OP extrañas, ausentes o ambiguas. Guardar borrador y versión de parser separados de la radicación, con revisiones y auditoría. La aprobación futura de Jefe de Operaciones deberá resolver alcance, unidades, discrepancias y material antes de persistir UUID de unidades/checklists. Los hashes del prototipo son identidades deterministas del corpus/fila/ordinal, no identidad operativa estable entre nuevas revisiones.', '',
             'No modificar la confirmación, el release ni la evidencia histórica. No generar tareas/checklists durante extracción. Reejecuciones y cambios de documento requieren idempotencia y reconciliación explícitas, sin duplicar piezas idénticas legítimas.', '',
             '## Archivos de salida', '',
             'Cada corpus-N/result.json contiene inventario, filas, unidades, coordenadas, anotaciones, totales, auditoría de líneas y errores. Cada corpus-N/matrix.csv agrupa OP/material/referencia/documento. summary.json resume los tres resultados. Este informe es REPORT.md. Los archivos son privados y externos a Git.']
    (output/'REPORT.md').write_text('\n'.join(text)+'\n',encoding='utf-8')
