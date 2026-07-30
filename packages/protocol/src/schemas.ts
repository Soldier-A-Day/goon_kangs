import { z } from "zod";
import {
  intentSchema,
  lobbyStateSchema,
  serverEventSchema,
  serverMessageSchema,
  snapshotSchema,
} from "./index.js";

/**
 * 코드 생성 대상 목록.
 *
 * ARCH-02가 요구하는 "단일 정의 → 양쪽 생성"의 입력이다. 여기 없는 타입은
 * C#으로 나가지 않으므로, Unity가 알아야 할 것을 추가할 때는 이 목록에 넣는다.
 */
export const CODEGEN_TARGETS = {
  Snapshot: snapshotSchema,
  ServerEvent: serverEventSchema,
  ServerMessage: serverMessageSchema,
  LobbyState: lobbyStateSchema,
  Intent: intentSchema,
} as const;

export type CodegenTargetName = keyof typeof CODEGEN_TARGETS;

/** zod 정의를 JSON Schema로 변환한다. C# 생성기의 입력이 된다. */
export function toJsonSchema(name: CodegenTargetName): unknown {
  return z.toJSONSchema(CODEGEN_TARGETS[name], { io: "output" });
}

export function allJsonSchemas(): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const name of Object.keys(CODEGEN_TARGETS) as CodegenTargetName[]) {
    out[name] = toJsonSchema(name);
  }
  return out;
}
