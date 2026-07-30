import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { createRngState, nextFloat, nextInt, sample, step } from "../src/index.js";
import { fullSquad } from "./helpers.js";

const SRC_DIR = fileURLToPath(new URL("../src", import.meta.url));

describe("RNG", () => {
  it("같은 시드는 같은 수열을 낸다", () => {
    const draw = (seed: number) => {
      let rng = createRngState(seed);
      const out: number[] = [];
      for (let i = 0; i < 50; i += 1) {
        const [value, next] = nextFloat(rng);
        rng = next;
        out.push(value);
      }
      return out;
    };
    expect(draw(42)).toEqual(draw(42));
    expect(draw(42)).not.toEqual(draw(43));
  });

  it("nextInt는 양 끝을 포함한 범위 안에 들어온다", () => {
    let rng = createRngState(7);
    for (let i = 0; i < 500; i += 1) {
      const [value, next] = nextInt(rng, 3, 5);
      rng = next;
      expect(value).toBeGreaterThanOrEqual(3);
      expect(value).toBeLessThanOrEqual(5);
    }
  });

  it("sample은 중복 없이 뽑고 원본을 건드리지 않는다", () => {
    const source = ["a", "b", "c", "d"] as const;
    const [taken] = sample(createRngState(9), source, 3);
    expect(new Set(taken).size).toBe(3);
    expect(source).toEqual(["a", "b", "c", "d"]);
  });
});

describe("런 재현성", () => {
  it("같은 시드 + 같은 입력열이면 같은 상태가 나온다", () => {
    const play = () => {
      let state = fullSquad({ seed: 20260730 });
      state = step(state, { type: "beginDay" }).state;
      for (let i = 0; i < 400; i += 1) {
        state = step(state, { type: "tick", elapsedMs: 1000 }).state;
      }
      return state;
    };
    expect(JSON.stringify(play())).toEqual(JSON.stringify(play()));
  });

  it("step은 입력 상태를 변형하지 않는다", () => {
    const before = fullSquad();
    const snapshot = JSON.stringify(before);
    step(before, { type: "beginDay" });
    step(before, { type: "tick", elapsedMs: 5000 });
    expect(JSON.stringify(before)).toEqual(snapshot);
  });
});

describe("결정론 가드", () => {
  it("sim 소스에는 시계와 전역 난수가 존재하지 않는다", () => {
    const banned = [/Math\.random/, /Date\.now/, /new Date\b/, /performance\.now/];
    for (const file of walk(SRC_DIR)) {
      const source = stripComments(readFileSync(file, "utf8"));
      for (const pattern of banned) {
        expect(
          pattern.test(source),
          `${file} 에 ${pattern} 가 있다 — sim은 결정론이어야 한다`,
        ).toBe(false);
      }
    }
  });
});

/** 주석은 검사 대상이 아니다 — 금지 이유를 주석에 적는 것까지 막을 필요는 없다 */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/.*$/gm, "");
}

function walk(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}
