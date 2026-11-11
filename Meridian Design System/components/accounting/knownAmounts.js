// Internal presentation safeguards. An unavailable supplied amount is not an empty
// debit/credit counterpart and must never silently become a known zero in proof.
import { toNumber } from '../core/format.js';
import { conditionMeta } from '../operations/conditions.js';
export function knownAmount(input) {
  const envelope=input&&typeof input==='object'&&'value' in input?input:null;
  if(envelope?.condition&&['missing','na','restricted'].includes(conditionMeta(envelope.condition).key))return null;
  const value=envelope?envelope.value:input;
  if(value==null||(typeof value==='string'&&value.trim()===''))return null;
  const number=toNumber(value);
  return Number.isFinite(number)?number:null;
}
export function sumKnown(values) {
  const numbers=values.map(knownAmount);
  return numbers.some(number=>number===null)?null:numbers.reduce((sum,number)=>sum+number,0);
}
export function displayAmount(value) {
  return knownAmount(value)===null?{value:null,condition:value?.condition||'missing'}:value;
}
export function optionalSide(value) {
  return value===undefined||(typeof value==='string'&&value.trim()==='')?0:knownAmount(value);
}
