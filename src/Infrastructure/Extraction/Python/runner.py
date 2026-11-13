"""Private stdin/stdout protocol. Runs outside HTTP; never receives credentials or storage paths."""
import sys, os, json, base64, hashlib, importlib.util

def limit(memory_mb):
    if os.name == 'nt':
        import ctypes as c
        from ctypes import wintypes as w
        class Basic(c.Structure):
            _fields_=[('processTime',c.c_int64),('jobTime',c.c_int64),('flags',w.DWORD),('minWs',c.c_size_t),('maxWs',c.c_size_t),('active',w.DWORD),('affinity',c.c_size_t),('priority',w.DWORD),('scheduling',w.DWORD)]
        class IO(c.Structure):
            _fields_=[(n,c.c_uint64) for n in ['ro','wo','oo','rt','wt','ot']]
        class Extended(c.Structure):
            _fields_=[('basic',Basic),('io',IO),('processMemory',c.c_size_t),('jobMemory',c.c_size_t),('peakProcess',c.c_size_t),('peakJob',c.c_size_t)]
        k=c.WinDLL('kernel32',use_last_error=True)
        k.CreateJobObjectW.restype=w.HANDLE
        k.CreateJobObjectW.argtypes=[c.c_void_p,w.LPCWSTR]
        k.SetInformationJobObject.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD]
        k.AssignProcessToJobObject.argtypes=[w.HANDLE,w.HANDLE]
        k.GetCurrentProcess.restype=w.HANDLE
        job=k.CreateJobObjectW(None,None)
        info=Extended();info.basic.flags=0x100|0x8;info.processMemory=memory_mb*1024*1024;info.basic.active=1
        if not job or not k.SetInformationJobObject(job,9,c.byref(info),c.sizeof(info)) or not k.AssignProcessToJobObject(job,k.GetCurrentProcess()):
            raise RuntimeError('RESOURCE_LIMIT_UNAVAILABLE')
        # Job handle intentionally lives until process exit; no child process permitted.
    else:
        import resource
        resource.setrlimit(resource.RLIMIT_AS,(memory_mb*1024*1024,)*2)
        resource.setrlimit(resource.RLIMIT_CPU,(90,90))
        resource.setrlimit(resource.RLIMIT_FSIZE,(0,0))

limit(int(sys.argv[1]))
raw=sys.stdin.buffer.read(75000001)
if len(raw)>75000000: raise ValueError('INPUT_LIMIT')
data=json.loads(raw)
spec=importlib.util.spec_from_file_location('sng_parser',os.path.join(os.path.dirname(__file__),'parser.py'))
parser=importlib.util.module_from_spec(spec);sys.modules[spec.name]=parser;spec.loader.exec_module(parser)
parser.MAX_PAGES=data['maxPages']
from importlib.metadata import version
if version('pypdfium2')!='5.15.2':raise RuntimeError('PROCESSOR_VERSION_UNSUPPORTED')
results=[]
if len(data['documents'])!=2 or len(data['ops'])>10000: raise ValueError('SCOPE_LIMIT')
for doc in data['documents']:
    content=base64.b64decode(doc['bytes'],validate=True)
    if len(content)!=doc['size'] or hashlib.sha256(content).hexdigest().upper()!=doc['sha256']: raise ValueError('INTEGRITY_ERROR')
    try:
        lines,pages=parser.pdf_lines(content)
        result=parser.parse_document(lines,doc['kind'],doc['sha256'],data['manifestHash'],set(data['ops']))
    except MemoryError:
        raise
    except Exception:
        results.append({'documentId':doc['id'],'sha256':doc['sha256'],'kind':doc['kind'],'template':'unreadable',
                        'rows':[],'issues':[{'code':'DOCUMENT_UNREADABLE_OR_LIMIT','severity':'error','source':None}],
                        'declaredTotals':[],'calculatedTotal':0,'lineAudit':[]})
        continue
    if pages!=len(set(x.page for x in lines)):
        result['issues'].append({'code':'EMPTY_PAGE','severity':'error','source':None})
    absent=set(data['ops'])-{r['op'] for r in result['rows']}
    if absent:result['issues'].append({'code':'MISSING_OP_COMPONENTS','severity':'error','ops':sorted(absent),'source':None})
    # Category is not a verified material. No +4, rounding or dimensional conversion.
    if doc['kind']=='glass':
        result['issues'].append({'code':'MATERIAL_UNVERIFIED','severity':'warning','source':None})
    result.pop('units',None);result.pop('unitMeaning',None)
    results.append({'documentId':doc['id'],'sha256':doc['sha256'],'kind':doc['kind'],**result})
output=json.dumps({'version':parser.VERSION,'documents':results},ensure_ascii=True,separators=(',',':'))
if len(output)>16000000:raise ValueError('OUTPUT_LIMIT')
sys.stdout.write(output)
