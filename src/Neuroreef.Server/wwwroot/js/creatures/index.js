// Species models: near (detailed, animated) and far (simple) geometry and materials for each species.
// The near models live in three files by group; a shape without a near model uses its far model.

import * as THREE from 'three';
import * as K from './kit.js';
import { farGeometry } from './far.js';
import { motionFor, creatureMaterial } from './anim.js';
import * as grazers from './grazers.js';
import * as hunters from './hunters.js';
import * as water from './water_swarms.js';

export const MODELS = { ...grazers.MODELS, ...hunters.MODELS, ...water.MODELS };
export const MOTION = { ...grazers.MOTION, ...hunters.MOTION, ...water.MOTION };

/** { nearGeo, nearMat, farGeo, farMat, motion } for one species from the console metadata. */
export function buildSpecies(sp) {
  const model = MODELS[sp.shape];
  const parts = model ? model(K, sp) : [K.part(farGeometry(sp.shape))];
  const nearGeo = K.build(parts, sp.color);
  // hip height: the highest leg pivot (0 for legless models); the shader lifts the body as legs lengthen
  let hip = 0;
  const pa = nearGeo.attributes.aPart.array, pv = nearGeo.attributes.aPivot.array;
  for (let k = 0; k < pa.length; k++) if (pa[k] >= 2 && pa[k] <= 5) hip = Math.max(hip, pv[k * 3 + 1]);
  const motion = motionFor(sp, { hip, ...MOTION[sp.shape] });
  const glassy = sp.shape === 15;
  const nearMat = creatureMaterial(sp, motion, { transparent: glassy, opacity: glassy ? 0.75 : 1 });
  const farGeo = farGeometry(sp.shape);
  const farMat = lambert(sp, glassy);
  return { nearGeo, nearMat, farGeo, farMat, motion, triangles: K.triangles(nearGeo) };
}

function lambert(sp, glassy) {
  return new THREE.MeshLambertMaterial({ color: new THREE.Color(sp.color), flatShading: true, transparent: glassy, opacity: glassy ? 0.7 : 1,
    emissive: new THREE.Color(sp.color).multiplyScalar(0.18) });
}
