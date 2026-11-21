// Internal presentation safeguards. An unavailable supplied amount is not an empty
// debit/credit counterpart and must never silently become a known zero in proof.
import { conditionMeta } from '../operations/conditions.js';
export function knownAmount(input) {
  const envelope=input&&typeof input==='object'&&'value' in input?input:null;
  if(envelope?.condition&&['missing','na','restricted'].includes(conditionMeta(envelope.condition).key))return null;
  const value=envelope?envelope.value:input;
  if(value==null||(typeof value==='string'&&value.trim()===''))return null;
  if(typeof value==='number')return Number.isFinite(value)?value:null;
  if(typeof value!=='string')return null;
  let text=value.trim().replace(/\u2212/g,'-');
  const negative=text.startsWith('(')&&text.endsWith(')');
  if(negative)text=text.slice(1,-1).trim();
  // Complete decimal/grouped accounting notation only. Display coercion must not
  // turn malformed prefixes (12oops) or exponent text (1e3) into proof inputs.
  const match=/^([+-]?)\s*([$€£¥]?)\s*((?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d*)?|\.\d+)$/.exec(text);
  if(!match||(negative&&match[1]))return null;
  const number=Number((negative?'-':match[1])+match[3].replace(/,/g,''));
  return Number.isFinite(number)?number:null;
}
export function sumKnown(values) {
  const numbers=values.map(knownAmount);
  return numbers.some(number=>number===null)?null:numbers.reduce((sum,number)=>sum+number,0);
}
export function displayAmount(value) {
  if(knownAmount(value)!==null)return value;
  const envelope=value&&typeof value==='object'&&'value' in value?value:null;
  if(envelope&&conditionMeta(envelope.condition).key!=='restricted')return{...envelope,value:null,condition:envelope.condition||'missing'};
  return{value:null,condition:envelope?.condition||'missing'};
}
export function optionalSide(value) {
  return value===undefined||(typeof value==='string'&&value.trim()==='')?0:knownAmount(value);
}
