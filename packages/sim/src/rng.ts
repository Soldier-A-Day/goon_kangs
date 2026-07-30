/**
 * 결정론적 난수. 상태를 인자로 받고 새 상태를 돌려주는 순수 함수 형태다.
 *
 * `Math.random()`을 쓰지 않는 이유는 두 가지다.
 * 1. 같은 시드 + 같은 입력열이면 언제나 같은 런이 나와야 밸런싱 시뮬레이터(tools/simrunner)가 성립한다.
 * 2. RNG 상태가 RunState 안에 들어가야 서버가 스냅샷을 저장했다 복구해도 이후 롤이 어긋나지 않는다.
 */

export interface RngState {
  /** mulberry32 내부 카운터 */
  readonly s: number;
}

export function createRngState(seed: number): RngState {
  return { s: seed | 0 };
}

/** [0, 1) 실수 */
export function nextFloat(state: RngState): readonly [number, RngState] {
  const a = (state.s + 0x6d2b79f5) | 0;
  let t = Math.imul(a ^ (a >>> 15), 1 | a);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  const value = ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  return [value, { s: a }];
}

/** [min, max] 정수 (양 끝 포함) */
export function nextInt(
  state: RngState,
  min: number,
  max: number,
): readonly [number, RngState] {
  const [value, next] = nextFloat(state);
  return [min + Math.floor(value * (max - min + 1)), next];
}

/** 확률 p로 true */
export function roll(
  state: RngState,
  probability: number,
): readonly [boolean, RngState] {
  const [value, next] = nextFloat(state);
  return [value < probability, next];
}

/** 목록에서 하나 뽑기. 빈 목록은 호출자 실수이므로 던진다. */
export function pick<T>(
  state: RngState,
  items: readonly T[],
): readonly [T, RngState] {
  if (items.length === 0) {
    throw new Error("pick: 빈 목록에서 뽑을 수 없다");
  }
  const [index, next] = nextInt(state, 0, items.length - 1);
  return [items[index] as T, next];
}

/** 목록에서 n개를 중복 없이 뽑기. n이 길이보다 크면 전체를 섞어서 돌려준다. */
export function sample<T>(
  state: RngState,
  items: readonly T[],
  count: number,
): readonly [T[], RngState] {
  const pool = [...items];
  const taken: T[] = [];
  let rng = state;
  const n = Math.min(count, pool.length);
  for (let i = 0; i < n; i += 1) {
    const [index, next] = nextInt(rng, 0, pool.length - 1);
    rng = next;
    taken.push(pool.splice(index, 1)[0] as T);
  }
  return [taken, rng];
}
