import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { allJsonSchemas } from "@sad/protocol/schemas";
import { emitCSharp, type JsonSchema } from "./emit.js";

/**
 * 프로토콜 C# 생성기.
 *
 * 출력물은 Unity 프로젝트가 그대로 가져다 쓴다. 손으로 고치면 다음 실행에서 덮어써지고,
 * 그게 의도다 — 스키마와 어긋난 C#이 살아남으면 안 된다.
 */
const OUT = resolve(
  process.argv[2] ?? "unity/Assets/Scripts/Generated/Protocol.cs",
);

const schemas = allJsonSchemas() as Record<string, JsonSchema>;
const source = emitCSharp(schemas, { namespace: "SoldierADay.Protocol" });

mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, source, "utf8");

console.log(`[csharpgen] ${Object.keys(schemas).length}개 타입 → ${OUT}`);
