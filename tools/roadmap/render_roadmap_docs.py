#!/usr/bin/env python3
from __future__ import annotations
import argparse, datetime as dt, locale, os, re, unicodedata
from typing import Any

YAML_LIBRARY = "ruamel.yaml"
YAML_VERSION_PIN = "0.18.x"
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def _enforce_environment() -> None:
    os.environ["TZ"] = "UTC"
    import time
    if hasattr(time, "tzset"): time.tzset()
    locale.setlocale(locale.LC_ALL, "C")


def _n(s:str)->str: return unicodedata.normalize("NFC", s)

def _strict_date(s:str)->str:
    if not DATE_RE.match(s): raise ValueError(f"Date must be YYYY-MM-DD only: {s}")
    dt.date.fromisoformat(s); return s


def _parse_simple_yaml(path:str)->dict[str,Any]:
    root:dict[str,Any] = {}
    stack=[(0,root)]
    for raw in open(path,encoding='utf-8'):
        if not raw.strip() or raw.lstrip().startswith('#'): continue
        indent=len(raw)-len(raw.lstrip(' ')); line=raw.strip()
        while stack and indent < stack[-1][0]: stack.pop()
        cur=stack[-1][1]
        k,v=[x.strip() for x in line.split(':',1)]
        if v=='':
            cur[k]={}; stack.append((indent+2,cur[k]))
        else:
            if (v.startswith('"') and v.endswith('"')) or (v.startswith("'") and v.endswith("'")): v=v[1:-1]
            cur[k]=v
    return root


def _normalize(value:Any, date_fields:set[str])->Any:
    if isinstance(value,dict):
        out={}
        for rk in sorted(value.keys(), key=lambda k:_n(str(k))):
            if not isinstance(rk,str): raise TypeError(f"Non-string key is ambiguous: {rk!r}")
            k=_n(rk); item=_normalize(value[rk],date_fields)
            if k in date_fields and isinstance(item,str): item=_strict_date(item)
            out[k]=item
        return out
    if isinstance(value,list): return [_normalize(v,date_fields) for v in value]
    if isinstance(value,str): return _n(value)
    raise TypeError(f"Unsupported scalar type: {type(value)!r}")


def _dump_yaml(value:Any, indent:int=0)->str:
    lines=[]
    for k,v in value.items():
        if isinstance(v,dict): lines.append(' '*indent+f"{k}:"); lines.append(_dump_yaml(v,indent+2))
        else: lines.append(' '*indent+f"{k}: {v}")
    return '\n'.join(lines)


def render(input_path:str,output_path:str,date_fields:set[str])->None:
    _enforce_environment(); data=_parse_simple_yaml(input_path); norm=_normalize(data,date_fields)
    with open(output_path,'w',encoding='utf-8',newline='\n') as f: f.write(_dump_yaml(norm)+"\n")

if __name__=='__main__':
    ap=argparse.ArgumentParser(); ap.add_argument('input'); ap.add_argument('output'); ap.add_argument('--date-field',action='append',default=[])
    a=ap.parse_args(); render(a.input,a.output,set(a.date_field))
