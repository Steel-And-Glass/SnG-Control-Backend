"""Opt-in regression against external customer ZIPs. Never writes samples to Git."""
import argparse
from collections import Counter
from pathlib import Path
import extract_components as e


def verify(folder: Path):
    expected=[('1162001',10,78,260,13,35),('1162002',3,23,62,3,7),('1162003',5,26,59,5,7)]
    # Manually checked against rendered pages, not values learned during the test.
    aluminum=[('1162001','OP5782-010.0001.00.MAR.H',2,'1470','1374','MARC0047',1),
              ('1162001','OP5773-001.0053.00.RIP.H',1,'2673','2673','ADAPT2135',3),
              ('1162002','OP6024-013.0005.00.BAT.H',2,'2173','2060.6','NAVE2295',1),
              ('1162002','OP6022-011.0000.03.JOL.H',1,'2539','2539','ANGU0061',2),
              ('1162003','OP6066-017.0005.00.COR.H',6,'2143','2015','NAVE1754',2),
              ('1162003','OP6065-016.0005.00.JUN.H',2,'357.6','357.6','PISA1881',2)]
    glasses=[('1162001','OP5773',12,'1258','2566'),('1162001','OP5780',2,'193','1090'),
             ('1162002','OP6022',3,'848','2370'),('1162002','OP6024',1,'370','2081'),
             ('1162003','OP6065',1,'975','384'),('1162003','OP6066',3,'925','2036')]
    all_ops=set(); results={}
    for rad,ops,ar,au,gr,gu in expected:
        path=folder/f'Radicado {rad}.zip'; before=e.digest(path.read_bytes())
        r=e.inspect_archive(path); results[rad]=r
        assert r==e.inspect_archive(path), 'Nondeterministic run'
        assert before==e.digest(path.read_bytes()), 'Source modified'
        assert len(r['knownOps'])==ops
        assert not all_ops.intersection(r['knownOps']), 'Unexpected cross-corpus OP overlap'
        all_ops.update(r['knownOps'])
        a,g=r['documents']
        assert (len(a['rows']),a['calculatedTotal'],len(g['rows']),g['calculatedTotal'])==(ar,au,gr,gu)
        assert not r['productionEligible']
        for d in r['documents']:
            assert not any(i['code'] in ('UNCLASSIFIED_LINE','UNKNOWN_OP','MISSING_POSITION','PAGE_COVERAGE_INVALID') for i in d['issues'])
            assert all(row['source']['bbox'] for row in d['rows'])
            assert len(d['units'])==d['calculatedTotal']
            assert len({u['id'] for u in d['units']})==len(d['units'])
        assert all(item['sha256'] for item in r['inventory'])
    assert len(all_ops)==18
    for rad,piece,q,measure,ct,ref,page in aluminum:
        row=next(r for r in results[rad]['documents'][0]['rows'] if r['pieceId']==piece)
        assert (row['quantity'],row['measure'],row['ct'],row['reference'],row['source']['page'])==(q,measure,ct,ref,page)
    for rad,op,q,w,h in glasses:
        assert any((r['op'],r['quantity'],r['width'],r['height'])==(op,q,w,h) for r in results[rad]['documents'][1]['rows'])
    assert 'TOTAL_MISMATCH' in {i['code'] for i in results['1162001']['documents'][1]['issues']}
    assert results['1162002']['status']=='review_required'
    g=results['1162003']['documents'][1]
    assert {'INCOMPLETE_ROW_WEIGHTS','MATERIAL_ANNOTATION_REQUIRES_REVIEW'} <= {i['code'] for i in g['issues']}
    assert g['documentAnnotations'][0]['rowAssociation'] is None
    print('PASS: 3 independent ZIPs; 18 OP; 127 aluminum + 21 glass-candidate rows; 12 visual golden samples; deterministic rerun; original hashes unchanged.')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('samples',type=Path)
    verify(p.parse_args().samples)
