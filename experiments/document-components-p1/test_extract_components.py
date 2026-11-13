import io
from pathlib import Path
import tempfile
import unittest
import zipfile
from unittest.mock import patch
import extract_components as e


def lines(*texts):
    return [e.Line(1, i+1, text, (10, 800-i*12, 560, 810-i*12)) for i, text in enumerate(texts)]


HEADER = 'Pieza ID Numero Medida (mm) CT (mm) Angulo IZ Angulo DR Estructura'
ROW = 'OP100-001.0001.00.MAR.H 2 1470 1374 45 90 1'
GLASS_HEADER = 'Cantidad Medidas (LxH) Perímetro Área (m2) Tipo Estructura: Peso Unitario Peso Total'


def cut(extra=(), quantity=2):
    return lines(f'Lista de corte (No Optimizada) - {quantity} Piezas (1 of 1)',
                 '(SERIES) REF1', 'Longitud total: 3 m Peso Total: 2 kg PROFILE DESCRIPTION', HEADER, ROW, *extra)


def parse(ls, kind='aluminum', ops=None):
    return e.parse_document(ls, kind, 'doc', 'corpus', {'OP100'} if ops is None else ops)


def codes(result):
    return {i['code'] for i in result['issues']}


class ParserTests(unittest.TestCase):
    def test_complete_row_and_units(self):
        r=parse(cut()); self.assertEqual(r['calculatedTotal'],2)
        self.assertEqual(r['rows'][0]['measure'],'1470'); self.assertEqual(r['rows'][0]['ct'],'1374')
        self.assertEqual(r['rows'][0]['description'],'PROFILE DESCRIPTION')
        self.assertEqual(r['rows'][0]['leftAngle'],'45'); self.assertEqual(r['rows'][0]['rightAngle'],'90')
        self.assertEqual(len({u['id'] for u in r['units']}),2)
        self.assertEqual(r['status'],'review_required')
    def test_deterministic_identity(self):
        self.assertEqual(parse(cut()),parse(cut()))
    def test_identity_separates_corpora(self):
        a=parse(cut()); b=e.parse_document(cut(),'aluminum','doc','other',{'OP100'})
        self.assertNotEqual(a['units'][0]['id'],b['units'][0]['id'])
    def test_context_across_pages(self):
        ls=cut(); ls[0]=e.Line(1,1,'Lista de corte (No Optimizada) - 4 Piezas (1 of 2)',ls[0].bbox)
        ls += [e.Line(2,1,'Lista de corte (No Optimizada) - 4 Piezas (2 of 2)',(10,800,500,810)),
               e.Line(2,2,ROW.replace('0001','0002'),(10,780,500,790))]
        r=parse(ls);self.assertEqual(r['rows'][1]['reference'],'REF1')
        self.assertEqual(r['rows'][1]['profileSource']['page'],1);self.assertEqual(r['rows'][1]['source']['page'],2)
        self.assertNotIn('PAGE_COVERAGE_INVALID',codes(r))
    def test_missing_profile(self):
        self.assertIn('MISSING_PROFILE_CONTEXT',codes(parse(lines('Lista de corte (No Optimizada) - 2 Piezas (1 of 1)',HEADER,ROW))))
    def test_incomplete_row_not_silent(self):
        r=parse(cut([ROW.rsplit(' ',1)[0]])); self.assertIn('UNCLASSIFIED_LINE',codes(r));self.assertEqual(r['status'],'blocked')
    def test_unknown_op(self):
        self.assertIn('UNKNOWN_OP',codes(parse(cut(),ops={'OP200'})))
    def test_no_baseline(self):
        self.assertIn('NO_DOCUMENTARY_OP_BASELINE',codes(parse(cut(),ops=set())))
    def test_duplicated_read_same_location(self):
        ls=cut();ls.append(ls[-1]);r=parse(ls)
        self.assertIn('DUPLICATE_READ',codes(r));self.assertEqual(len(r['units']),2)
    def test_identical_glass_at_distinct_locations_preserved(self):
        row='1 200 x 300 1 m 0,1 m2 OP100 1 2 kg 2 kg'
        r=parse(lines('Lista Vidrios - Numero piezas: 2 (1 of 1)','(VIDRIOS) 08MM',GLASS_HEADER,row,row),'glass')
        self.assertEqual(len(r['rows']),2);self.assertEqual(len({x['id'] for x in r['units']}),2)
    def test_duplicate_piece_not_silently_removed(self):
        r=parse(cut([ROW],4));self.assertIn('DUPLICATE_PIECE_ID',codes(r));self.assertEqual(len(r['units']),4)
    def test_total_mismatch(self):
        self.assertIn('TOTAL_MISMATCH',codes(parse(cut(quantity=9))))
    def test_unknown_line(self):
        self.assertIn('UNCLASSIFIED_LINE',codes(parse(cut(['999 12 35 ???']))))
    def test_no_supply_bars_as_components(self):
        r=parse(cut(['Barras: 34 x 6000']));self.assertEqual(r['calculatedTotal'],2);self.assertEqual(r['status'],'blocked')
    def test_missing_page(self):
        ls=cut();ls[0]=e.Line(1,1,'Lista de corte (No Optimizada) - 2 Piezas (1 of 2)',ls[0].bbox)
        self.assertIn('PAGE_COVERAGE_INVALID',codes(parse(ls)))
    def test_wrong_header(self):
        ls=cut();ls[3]=e.Line(1,4,'Pieza ID Numero Medida (mm)',ls[3].bbox)
        self.assertIn('UNRECOGNIZED_TABLE_HEADER',codes(parse(ls)))
    def test_glass_annotation_and_weights(self):
        r=parse(lines('Lista Vidrios - Numero piezas: 1 (1 of 1)','(VIDRIOS) 08MM',GLASS_HEADER,
                      '1 975 x 384 2,7 m 0,4 m2 OP100 1','LÁMINA MICROPERFORADA'),'glass')
        self.assertIn('INCOMPLETE_ROW_WEIGHTS',codes(r));self.assertIn('MATERIAL_ANNOTATION_REQUIRES_REVIEW',codes(r))
        self.assertIsNone(r['rows'][0]['dimensionUnit']);self.assertFalse(r['rows'][0]['materialVerified'])
    def test_decimal_no_floating_rounding(self):
        r=parse([e.Line(x.page,x.ordinal,x.text.replace('1470','1721,9').replace('1374','1595,3'),x.bbox) for x in cut()])
        self.assertEqual(r['rows'][0]['measure'],'1721.9');self.assertEqual(r['rows'][0]['ct'],'1595.3')
    def test_missing_description_not_invented(self):
        ls=cut();ls[2]=e.Line(1,3,'Longitud total: 3 m Peso Total: 2 kg',ls[2].bbox)
        r=parse(ls);self.assertIsNone(r['rows'][0]['description']);self.assertIn('DESCRIPTION_NOT_PROVIDED',codes(r))
    def test_quantity_bound(self):
        ls=[e.Line(x.page,x.ordinal,x.text.replace(' 2 1470',' 100001 1470'),x.bbox) for x in cut()]
        r=parse(ls);self.assertIn('INVALID_QUANTITY',codes(r));self.assertEqual(r['units'],[])
    def test_position_required(self):
        ls=cut();ls[-1]=e.Line(1,5,ROW,None);self.assertIn('MISSING_POSITION',codes(parse(ls)))


def small_pdf(text):
    # Synthetic text-only PDF, not copied from customer documents.
    stream=f'BT /F1 12 Tf 30 700 Td ({text}) Tj ET'.encode('ascii')
    objs=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
          b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
          b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
          b'<< /Length '+str(len(stream)).encode()+b' >>\nstream\n'+stream+b'\nendstream']
    data=b'%PDF-1.4\n'; offsets=[0]
    for i,obj in enumerate(objs,1):
        offsets.append(len(data)); data+=f'{i} 0 obj\n'.encode()+obj+b'\nendobj\n'
    xref=len(data);data+=b'xref\n0 6\n0000000000 65535 f \n'
    data+=b''.join(f'{x:010d} 00000 n \n'.encode() for x in offsets[1:])
    return data+f'trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF'.encode()


class PdfAndArchiveTests(unittest.TestCase):
    def test_real_text_coordinates(self):
        ls,pages=e.pdf_lines(small_pdf('OP100 example'));self.assertEqual(pages,1)
        self.assertEqual(ls[0].text,'OP100 example');self.assertIsNotNone(ls[0].bbox)
        self.assertAlmostEqual(ls[0].bbox[0],30,delta=2)
    def archive(self,name,data):
        temp=tempfile.TemporaryDirectory();self.addCleanup(temp.cleanup);p=Path(temp.name)/'sample.zip'
        with zipfile.ZipFile(p,'w') as z:z.writestr(name,data)
        return p
    def test_path_traversal_rejected_without_extraction(self):
        with self.assertRaisesRegex(ValueError,'UNSAFE_ARCHIVE_ENTRY'):
            e.inspect_archive(self.archive('../outside.pdf',b'x'))
    def test_unknown_document(self):
        r=e.inspect_archive(self.archive('04 cut.pdf',small_pdf('Unrecognized template')))
        self.assertIn('UNRECOGNIZED_DOCUMENT',{x['code'] for x in r['issues']});self.assertFalse(r['productionEligible'])
    def test_corrupt_pdf_blocks(self):
        r=e.inspect_archive(self.archive('04 cut.pdf',b'broken'))
        self.assertIn('PDF_READ_FAILED',{x['code'] for x in r['issues']})
    def test_attachment_inventoried_never_executed(self):
        r=e.inspect_archive(self.archive('ticket.xls',b'not interpreted'))
        self.assertEqual(r['inventory'][0]['status'],'inventoried_not_interpreted')
    def test_archive_limit(self):
        p=self.archive('file',b'x'*20)
        with patch.object(e,'MAX_TOTAL',5),self.assertRaisesRegex(ValueError,'ARCHIVE_LIMIT'):
            e.inspect_archive(p)


if __name__ == '__main__':
    unittest.main()
