import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",

    // Unity가 만들어 낸 WebGL 번들. 우리가 쓴 코드가 아니고 고칠 수도 없다 —
    // `webgl-sync.mjs`가 빌드마다 통째로 갈아 끼우므로 여기서 손을 대 봐야
    // 다음 빌드에 사라진다. 린트에 걸어 두면 우리 코드의 문제 6건이
    // 남의 코드 575건에 묻힌다(실제로 그랬다).
    "public/game/**",
    "public/hd2d/**",
  ]),
]);

export default eslintConfig;
