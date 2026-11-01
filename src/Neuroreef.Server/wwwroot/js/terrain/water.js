// Standing water's shader additions (2026-11-01): each lake and the sea fade out over their last half unit of depth (the
// ground under them shows through at the shore instead of a hard edge), with a thin broken line of foam where they lap
// the beach. The depth is the water's level less the drawn ground's (ground.js's triangle surface) under each pixel.

import { GLSL_COMMON } from './ground.js';

export function patchLakeMaterial(m, U) {
  m.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, U);
    sh.vertexShader = 'varying vec3 vLakeWP;\n' + sh.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\nvLakeWP = position;');
    sh.fragmentShader = 'uniform float uTime;\nvarying vec3 vLakeWP;\n' + GLSL_COMMON + sh.fragmentShader.replace('#include <color_fragment>', `#include <color_fragment>
      {
        float dep = vLakeWP.y - groundTri(vLakeWP.xz).x;
        float n = texture(tNoise, vLakeWP.xz / 45.0 + vec2(uTime * 0.004, 0.0)).r;
        float foam = (1.0 - smoothstep(0.02, 0.16 + 0.12 * n, dep)) * smoothstep(-0.02, 0.02, dep);
        diffuseColor.a *= mix(0.15, 1.0, smoothstep(0.0, 0.5, dep));
        diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.86, 0.92, 0.94), foam * 0.6);
        diffuseColor.a = max(diffuseColor.a, foam * 0.55);
      }`);
  };
  m.customProgramCacheKey = () => 'nr-lake-1';
}
