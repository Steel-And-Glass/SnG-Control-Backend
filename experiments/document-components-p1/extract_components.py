"""Offline P1 prototype. No database, network, OCR, AI or operational writes."""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
from dataclasses import dataclass
from contextlib import closing
from decimal import Decimal
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile

VERSION = 'p1.1'
MAX_ENTRY = 30 * 1024 * 1024
MAX_TOTAL = 300 * 1024 * 1024
MAX_PAGES = 100
MAX_UNITS = 100000
NUM = r'[0-9]+(?:,[0-9]+)?'
CUT = re.compile(rf'(?P<piece>(?P<op>OP[0-9]+)-[A-Z0-9.]+) (?P<quantity>[0-9]+) (?P<measure>{NUM}) (?P<ct>{NUM}) (?P<left>{NUM}) (?P<right>{NUM}) (?P<structure>[0-9]+)')
GLASS = re.compile(rf'(?P<quantity>[0-9]+) (?P<width>{NUM}) x (?P<height>{NUM}) (?P<perimeter>{NUM}) m (?P<area>{NUM}) m2 (?P<op>OP[0-9]+) (?P<structure>[0-9]+)(?: (?P<unitWeight>{NUM}) kg (?P<totalWeight>{NUM}) kg)?')
PROFILE = re.compile(r'\(([^)]+)\) ([A-Z0-9]+)')


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def identity(*parts) -> str:
    return digest(json.dumps(parts, ensure_ascii=True, separators=(',', ':')).encode())


def decimal(raw: str) -> str:
    return format(Decimal(raw.replace(',', '.')), 'f')


@dataclass(frozen=True)
class Line:
    page: int
    ordinal: int
    text: str
    bbox: tuple | None
    page_size: tuple = (595, 842)

    def source(self):
        return {'page': self.page, 'line': self.ordinal, 'bbox': self.bbox,
                'pageSize': self.page_size, 'coordinateSystem': 'PDF points; origin bottom-left'}


def pdf_lines(data: bytes) -> tuple[list[Line], int]:
    import pypdfium2 as pdfium
    result = []
    with pdfium.PdfDocument(data) as pdf:
        if len(pdf) > MAX_PAGES:
            raise ValueError('PAGE_LIMIT')
        for page_no in range(len(pdf)):
            with closing(pdf[page_no]) as page, closing(page.get_textpage()) as text:
                cursor = 0
                for ordinal, raw in enumerate(text.get_text_range().splitlines(), 1):
                    raw = raw.strip()
                    if not raw:
                        continue
                    with closing(text.search(raw, index=cursor, match_case=True)) as search:
                        match = search.get_next()
                    bbox = None
                    if match:
                        start, count = match
                        cursor = start + count
                        boxes = [text.get_charbox(i) for i in range(start, start + count)
                                 if text.get_text_range(i, 1).strip()]
                        if boxes:
                            bbox = tuple(round(x, 3) for x in (min(b[0] for b in boxes), min(b[1] for b in boxes),
                                                            max(b[2] for b in boxes), max(b[3] for b in boxes)))
                    result.append(Line(page_no + 1, ordinal, ' '.join(raw.split()), bbox, tuple(page.get_size())))
        return result, len(pdf)


def parse_document(lines: list[Line], kind: str, document_hash: str, corpus_hash: str,
                   known_ops: set[str]) -> dict:
    """Closed observed templates. Preserve unknown lines and fail closed."""
    rows, issues, classified = [], [], []
    profile = None
    seen_locations = set()
    declarations = []
    specs = [l for l in lines if l.text.startswith(('COMPOSICI', 'LAMINA', 'LÁMINA'))]
    headers = [l for l in lines if l.text.startswith('Pieza ID' if kind == 'aluminum' else 'Cantidad Medidas')]
    required = ('Numero', 'Medida (mm)', 'CT (mm)', 'Angulo IZ', 'Angulo DR', 'Estructura') if kind == 'aluminum' else ('Tipo', 'Estructura', 'Peso Unitario', 'Peso Total')

    def issue(code, line=None, severity='error', **details):
        issues.append({'code': code, 'severity': severity, 'source': line.source() if line else None, **details})

    if not headers or any(not all(s in h.text for s in required) for h in headers):
        issue('UNRECOGNIZED_TABLE_HEADER')
    if not known_ops:
        issue('NO_DOCUMENTARY_OP_BASELINE')
    if kind == 'glass':
        issue('DIMENSION_UNIT_NOT_EXPLICIT', severity='warning')
        if any(not l.text.startswith('COMPOSICI') for l in specs):
            issue('MATERIAL_ANNOTATION_REQUIRES_REVIEW')
    title = r'Lista de corte \(No Optimizada\) - ([0-9]+) Piezas' if kind == 'aluminum' else r'Lista Vidrios - Numero piezas: ([0-9]+)'
    for line in lines:
        s = line.text
        classification = None
        location = (line.page, line.bbox, s)
        if line.bbox is not None and location in seen_locations:
            issue('DUPLICATE_READ', line)
            classified.append({**line.source(), 'classification': 'duplicate_read', 'text': s})
            continue
        seen_locations.add(location)
        total = re.search(title, s)
        if total:
            declarations.append({'quantity': int(total[1]), 'source': line.source()})
            classification = 'document_total'
        block = PROFILE.fullmatch(s)
        if block:
            profile = {'series': block[1], 'reference': block[2], 'description': None, 'source': line.source()}
            classification = 'profile_header'
        if s.startswith('Longitud total:'):
            classification = 'block_summary'
            desc = re.match(rf'Longitud total: {NUM} m Peso Total: {NUM} kg(?: (.*))?$', s)
            if kind == 'aluminum':
                if profile and desc:
                    profile['description'] = desc[1]
                else:
                    issue('UNRECOGNIZED_PROFILE_SUMMARY', line)
        match = (CUT if kind == 'aluminum' else GLASS).fullmatch(s)
        if match:
            classification = 'component'
            g = match.groupdict()
            q = int(g['quantity'])
            if q < 1 or q > MAX_UNITS:
                issue('INVALID_QUANTITY', line)
            source = line.source()
            row = {'id': identity(VERSION, corpus_hash, document_hash, source),
                   'op': g['op'], 'quantity': q, 'material': kind, 'structure': g['structure'],
                   'source': source, 'originalText': s, 'documentSha256': document_hash}
            if line.bbox is None:
                issue('MISSING_POSITION', line)
            if g['op'] not in known_ops:
                issue('UNKNOWN_OP', line, op=g['op'])
            if kind == 'aluminum':
                if profile is None:
                    issue('MISSING_PROFILE_CONTEXT', line)
                row.update(pieceId=g['piece'], series=profile['series'] if profile else None,
                           reference=profile['reference'] if profile else None,
                           description=profile['description'] if profile else None,
                           profileSource=profile['source'] if profile else None,
                           measure=decimal(g['measure']), ct=decimal(g['ct']), dimensionUnit='mm',
                           leftAngle=decimal(g['left']), rightAngle=decimal(g['right']))
                if not row['description']:
                    issue('DESCRIPTION_NOT_PROVIDED', line, 'warning')
                if any(Decimal(row[k]) <= 0 for k in ('measure', 'ct')) or any(not 0 <= Decimal(row[k]) <= 180 for k in ('leftAngle', 'rightAngle')):
                    issue('INVALID_DIMENSION_OR_ANGLE', line)
            else:
                row.update(reference=profile['reference'] if profile else None,
                           composition=[{'text': l.text, 'source': l.source()} for l in specs if l.text.startswith('COMPOSICI')],
                           compositionScope='document-level; not verified for individual rows',
                           width=decimal(g['width']), height=decimal(g['height']),
                           originalDimensions=f"{g['width']} x {g['height']}", dimensionUnit=None,
                           perimeter=decimal(g['perimeter']), area=decimal(g['area']),
                           unitWeight=decimal(g['unitWeight']) if g['unitWeight'] else None,
                           totalWeight=decimal(g['totalWeight']) if g['totalWeight'] else None,
                           materialVerified=False)
                if not profile:
                    issue('MISSING_GLASS_SPECIFICATION', line)
                if not g['unitWeight']:
                    issue('INCOMPLETE_ROW_WEIGHTS', line)
                if any(Decimal(row[k]) <= 0 for k in ('width', 'height')):
                    issue('INVALID_DIMENSION', line)
            rows.append(row)
        if classification is None:
            if line in specs:
                classification = 'document_annotation'
            elif s.startswith(('Pieza ID', 'Cantidad Medidas', 'Lista vidrios rectangular')):
                classification = 'table_header'
            elif s.startswith(('Pedido/Grupo:', 'Cliente/Cantiere:', 'Descripción:', 'Fecha/Hora:', 'S&G',
                               'Clase:', 'ACABADO ALUMINIO:', 'i Punta alta', 'Punta alta')):
                classification = 'document_metadata'
            else:
                classification = 'unclassified'
                issue('UNCLASSIFIED_LINE', line, text=s)
        classified.append({**line.source(), 'classification': classification, 'text': s})
    page_labels = [tuple(map(int, m)) for l in lines for m in re.findall(r'\(([0-9]+) of ([0-9]+)\)', l.text)]
    actual_pages = sorted({l.page for l in lines})
    if not page_labels or len({n for _, n in page_labels}) != 1 or [p for p, _ in page_labels] != list(range(1, page_labels[0][1] + 1)) or actual_pages != [p for p, _ in page_labels]:
        issue('PAGE_COVERAGE_INVALID')
    if not declarations:
        issue('MISSING_DECLARED_TOTAL')
    declared_values = sorted({t['quantity'] for t in declarations})
    calculated = sum(r['quantity'] for r in rows)
    if declared_values and (len(declared_values) != 1 or declared_values[0] != calculated):
        issue('TOTAL_MISMATCH', declared=declared_values, calculated=calculated)
    if not rows:
        issue('NO_COMPONENTS')
    if kind == 'aluminum':
        duplicates = [key for key, count in Counter(r['pieceId'] for r in rows).items() if count > 1]
        if duplicates:
            issue('DUPLICATE_PIECE_ID', pieceIds=duplicates)
    if calculated > MAX_UNITS:
        issue('UNIT_LIMIT')
        units = []
    else:
        units = [{'id': identity(r['id'], ordinal), 'componentId': r['id'], 'ordinal': ordinal,
                  'op': r['op'], 'material': r['material'], 'reference': r['reference'],
                  'documentSha256': document_hash} for r in rows for ordinal in range(1, r['quantity'] + 1)]
    return {'status': 'blocked' if any(i['severity'] == 'error' for i in issues) else 'review_required',
            'template': 'cut-v1' if kind == 'aluminum' else 'rectangular-glass-v1',
            'rows': rows, 'units': units, 'issues': issues, 'lineAudit': classified,
            'documentAnnotations': [{'text': l.text, 'source': l.source(), 'rowAssociation': None} for l in specs if not l.text.startswith('COMPOSICI')],
            'declaredTotals': declarations, 'calculatedTotal': calculated,
            'unitMeaning': 'candidates only; never approved production units'}


def inspect_archive(path: Path) -> dict:
    if path.stat().st_size > MAX_TOTAL:
        raise ValueError('ARCHIVE_LIMIT')
    archive_bytes = path.read_bytes()
    if len(archive_bytes) > MAX_TOTAL:
        raise ValueError('ARCHIVE_LIMIT')
    archive_hash = digest(archive_bytes)
    inventory, targets, structure_ops, errors = [], [], set(), []
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if len(entries) > 500 or sum(x.file_size for x in entries) > MAX_TOTAL:
            raise ValueError('ARCHIVE_EXPANSION_LIMIT')
        names = set()
        for info in entries:
            p = PurePosixPath(info.filename)
            if p.is_absolute() or '..' in p.parts or '\\' in info.filename or ':' in info.filename or stat.S_ISLNK(info.external_attr >> 16):
                raise ValueError('UNSAFE_ARCHIVE_ENTRY')
            if info.filename.casefold() in names:
                raise ValueError('DUPLICATE_ARCHIVE_ENTRY')
            names.add(info.filename.casefold())
            if info.is_dir():
                continue
            if info.file_size > MAX_ENTRY or info.flag_bits & 1:
                raise ValueError('UNSUPPORTED_ENTRY')
            data = archive.read(info)  # CRC checked; never extract archive paths.
            item = {'entry': info.filename, 'sha256': digest(data), 'bytes': len(data)}
            inventory.append(item)
            if p.suffix.lower() != '.pdf':
                item['status'] = 'inventoried_not_interpreted'
                continue
            try:
                lines, pages = pdf_lines(data)
            except Exception as exc:
                item.update(status='pdf_read_failed', error=type(exc).__name__)
                errors.append({'code': 'PDF_READ_FAILED', 'document': info.filename})
                continue
            item.update(pages=pages, textCharacters=sum(len(l.text) for l in lines))
            text = '\n'.join(l.text for l in lines)
            if 'Lista de corte (No Optimizada)' in text:
                kind = 'aluminum'
            elif 'Lista Vidrios - Numero piezas:' in text:
                kind = 'glass'
            elif 'Lista Estructuras' in text:
                ops = set(re.findall(r'Tipo: (OP[0-9]+)\b', text))
                structure_ops.update(ops)
                item.update(status='op_baseline', ops=sorted(ops))
                continue
            else:
                auxiliary = p.name.lower().startswith(('02 ', '03 ', '06 ')) or 'modul' in str(p).lower()
                item['status'] = 'auxiliary_not_component_source' if auxiliary else 'unrecognized_document'
                if not auxiliary:
                    errors.append({'code': 'UNRECOGNIZED_DOCUMENT', 'document': info.filename})
                continue
            item.update(status='component_source', material=kind)
            targets.append((item, lines, kind))
    docs = []
    for item, lines, kind in targets:
        result = parse_document(lines, kind, item['sha256'], archive_hash, structure_ops)
        docs.append({'document': item['entry'], 'sha256': item['sha256'], **result})
    for kind in ('aluminum', 'glass'):
        count = sum(item['material'] == kind for item, _, _ in targets)
        if count != 1:
            errors.append({'code': 'AMBIGUOUS_OR_MISSING_DOCUMENT_SET', 'material': kind, 'count': count})
        observed = {r['op'] for d in docs for r in d['rows'] if r['material'] == kind}
        if structure_ops - observed:
            errors.append({'code': 'MISSING_OP_COMPONENTS', 'material': kind, 'ops': sorted(structure_ops-observed)})
    matrix = defaultdict(lambda: {'rows': 0, 'units': 0})
    for d in docs:
        for r in d['rows']:
            v = matrix[(r['op'], r['material'], r['reference'], d['document'])]
            v['rows'] += 1
            v['units'] += r['quantity']
    return {'prototypeVersion': VERSION, 'corpus': path.stem, 'archiveSha256': archive_hash,
            'status': 'blocked' if errors or any(d['status'] == 'blocked' for d in docs) else 'review_required',
            'productionEligible': False, 'knownOpsSource': 'Structures PDF in this archive only',
            'knownOps': sorted(structure_ops), 'inventory': inventory, 'documents': docs, 'issues': errors,
            'matrix': [{'op': k[0], 'material': k[1], 'reference': k[2], 'document': k[3], **v}
                       for k, v in sorted(matrix.items(), key=lambda kv: str(kv[0]))]}


def write_result(result: dict, folder: Path):
    folder.mkdir(parents=True, exist_ok=False)
    (folder/'result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    import csv
    with (folder/'matrix.csv').open('w', encoding='utf-8', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=['op', 'material', 'reference', 'document', 'rows', 'units'])
        writer.writeheader()
        writer.writerows(result['matrix'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('archives', type=Path, nargs='+')
    args = parser.parse_args()
    # Refuse outputs in any Git worktree, including the sample directory if versioned.
    output = args.output.resolve()
    if any((p/'.git').exists() for p in (output, *output.parents)):
        parser.error('Outputs must be outside Git worktrees')
    output.mkdir(parents=True, exist_ok=False)
    reports = []
    full_results = []
    for n, path in enumerate(args.archives, 1):
        result = inspect_archive(path)
        full_results.append(result)
        write_result(result, output/f'corpus-{n}')
        reports.append({'corpus': result['corpus'], 'status': result['status'], 'ops': len(result['knownOps']),
                        'documents': [{'template': d['template'], 'rows': len(d['rows']), 'units': d['calculatedTotal'],
                                       'issueCounts': dict(Counter(i['code'] for i in d['issues']))} for d in result['documents']]})
    (output/'summary.json').write_text(json.dumps(reports, indent=2), encoding='utf-8')
    from reporting import write_report
    write_report(full_results, output)
    print(json.dumps(reports))
    return 2 if any(x['status'] == 'blocked' for x in reports) else 0


if __name__ == '__main__':
    raise SystemExit(main())
