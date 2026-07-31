/**
 * 블록아웃용 최소 메시 라이브러리.
 *
 * 목적은 예쁜 모델이 아니라 **예산에 맞는 지오메트리**다. M0의 목적이
 * 성능 게이트(19.0)이므로, 실루엣과 폴리 수가 맞으면 게이트는 제 역할을 한다.
 *
 * 에셋 파일을 저장소에 넣는 대신 **에셋을 만드는 코드**를 넣는다. 예산이 바뀌면
 * 다시 뽑으면 되고, 무엇이 왜 그 폴리 수인지가 코드에 남는다 — 바이너리 파일은
 * 그걸 설명하지 못한다.
 */
export interface Vec3 {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

export const v = (x: number, y: number, z: number): Vec3 => ({ x, y, z });

export class Mesh {
  readonly positions: Vec3[] = [];
  /** 0-기반 정점 인덱스 3개씩 */
  readonly indices: number[] = [];

  get triangleCount(): number {
    return this.indices.length / 3;
  }

  vertex(p: Vec3): number {
    this.positions.push(p);
    return this.positions.length - 1;
  }

  triangle(a: number, b: number, c: number): void {
    this.indices.push(a, b, c);
  }

  quad(a: number, b: number, c: number, d: number): void {
    this.triangle(a, b, c);
    this.triangle(a, c, d);
  }

  /** 다른 메시를 옮겨 붙인다. 인덱스는 자동으로 다시 매긴다 */
  merge(other: Mesh, offset: Vec3 = v(0, 0, 0), scale: Vec3 = v(1, 1, 1)): void {
    const base = this.positions.length;
    for (const p of other.positions) {
      this.positions.push(
        v(p.x * scale.x + offset.x, p.y * scale.y + offset.y, p.z * scale.z + offset.z),
      );
    }
    for (const i of other.indices) this.indices.push(base + i);
  }
}

/**
 * 축 정렬 상자. 6면 × 2 = 12 삼각형.
 *
 * 면을 나누는 `segments`가 있는 이유는 예산을 채우기 위해서다 — 실제 에셋은
 * 디테일 때문에 폴리가 늘지만 블록아웃은 그럴 게 없다. 부하를 재는 것이
 * 목적이므로 **같은 폴리 수를 같은 자리에** 놓는 편이 낫다.
 */
export function box(size: Vec3, segments = 1): Mesh {
  const mesh = new Mesh();
  const hx = size.x / 2;
  const hy = size.y / 2;
  const hz = size.z / 2;
  const n = Math.max(1, Math.floor(segments));

  // 각 면을 n×n 격자로 나눈다. 면마다 정점을 따로 두어 하드 엣지를 유지한다 —
  // 블록아웃에서 모서리가 뭉개지면 실루엣 판단이 안 된다.
  const face = (
    origin: Vec3,
    right: Vec3,
    up: Vec3,
  ) => {
    const start = mesh.positions.length;
    for (let i = 0; i <= n; i += 1) {
      for (let j = 0; j <= n; j += 1) {
        const s = i / n;
        const t = j / n;
        mesh.vertex(
          v(
            origin.x + right.x * s + up.x * t,
            origin.y + right.y * s + up.y * t,
            origin.z + right.z * s + up.z * t,
          ),
        );
      }
    }
    for (let i = 0; i < n; i += 1) {
      for (let j = 0; j < n; j += 1) {
        const a = start + i * (n + 1) + j;
        mesh.quad(a, a + 1, a + n + 2, a + n + 1);
      }
    }
  };

  face(v(-hx, -hy, hz), v(size.x, 0, 0), v(0, size.y, 0)); // 앞
  face(v(hx, -hy, -hz), v(-size.x, 0, 0), v(0, size.y, 0)); // 뒤
  face(v(-hx, -hy, -hz), v(0, 0, size.z), v(0, size.y, 0)); // 좌
  face(v(hx, -hy, hz), v(0, 0, -size.z), v(0, size.y, 0)); // 우
  face(v(-hx, hy, -hz), v(size.x, 0, 0), v(0, 0, size.z)); // 상
  face(v(-hx, -hy, hz), v(size.x, 0, 0), v(0, 0, -size.z)); // 하

  return mesh;
}

/** Y축 원기둥. 뚜껑 포함 시 sides × 4, 미포함 시 sides × 2 삼각형 */
export function cylinder(radius: number, height: number, sides: number, caps = true): Mesh {
  const mesh = new Mesh();
  const n = Math.max(3, Math.floor(sides));
  const hy = height / 2;

  for (let i = 0; i < n; i += 1) {
    const angle = (i / n) * Math.PI * 2;
    const x = Math.cos(angle) * radius;
    const z = Math.sin(angle) * radius;
    mesh.vertex(v(x, -hy, z));
    mesh.vertex(v(x, hy, z));
  }

  for (let i = 0; i < n; i += 1) {
    const a = i * 2;
    const b = ((i + 1) % n) * 2;
    mesh.quad(a, b, b + 1, a + 1);
  }

  if (caps) {
    const top = mesh.vertex(v(0, hy, 0));
    const bottom = mesh.vertex(v(0, -hy, 0));
    for (let i = 0; i < n; i += 1) {
      const a = i * 2;
      const b = ((i + 1) % n) * 2;
      mesh.triangle(top, a + 1, b + 1);
      mesh.triangle(bottom, b, a);
    }
  }

  return mesh;
}

/**
 * 예산에 맞추기 — **넘지 않으면서 가장 가까운 값**을 찾는다.
 *
 * 이등분 탐색으로 짰다가 바꿨다. 모듈마다 `detail`에 배율을 곱하고 반올림하므로
 * 폴리 수가 계단식으로 튀고, 계단 폭이 모듈마다 다르다. 이등분은 단조 증가를
 * 전제하는데 그 전제가 성립하지 않아 **생활관이 예산의 53%에서 멈췄다** —
 * 답이 아니라 탐색이 걸린 자리였다.
 *
 * 그래서 촘촘히 훑는다. 블록아웃 생성은 몇 밀리초라 아낄 이유가 없고,
 * 답이 맞는 것이 빠른 것보다 중요하다.
 */
export function fitToBudget(
  budget: number,
  build: (detail: number) => Mesh,
  maxDetail = 64,
  step = 0.25,
): { mesh: Mesh; detail: number } {
  let best = build(1);
  let bestDetail = 1;

  if (best.triangleCount > budget) {
    throw new Error(
      `최소 디테일에서도 예산 초과: ${best.triangleCount} > ${budget}. ` +
        "블록아웃 구성이 예산보다 크다 — 모듈을 줄이거나 예산을 다시 본다",
    );
  }

  for (let detail = 1 + step; detail <= maxDetail; detail += step) {
    const candidate = build(detail);

    // 계단 하나를 건너뛸 뿐 총량은 대체로 단조 증가한다. 예산을 크게 넘어선
    // 뒤에도 계속 지으면 디테일 제곱으로 커지는 메시를 수백 번 만들게 된다 —
    // 처음에 그렇게 짰다가 생성이 5분을 넘겼다. 여유를 두고 끊는다.
    if (candidate.triangleCount > budget * 1.5) break;

    if (candidate.triangleCount <= budget && candidate.triangleCount > best.triangleCount) {
      best = candidate;
      bestDetail = detail;
    }
  }

  return { mesh: best, detail: bestDetail };
}

/**
 * OBJ로 쓴다.
 *
 * FBX가 아니라 OBJ인 이유는 **텍스트라서 diff가 보이기 때문**이다. 생성기를
 * 고쳤을 때 무엇이 달라졌는지 저장소에서 읽을 수 있다. 대신 리그를 담지 못하므로
 * 캐릭터·피복은 Unity 쪽에서 만든다.
 */
export function toObj(mesh: Mesh, header: string): string {
  const lines: string[] = [];
  lines.push(`# ${header}`);
  lines.push(`# 삼각형 ${mesh.triangleCount} · 정점 ${mesh.positions.length}`);
  lines.push("# 생성: tools/blockout — 손으로 고치지 말 것, 생성기를 고칠 것");
  lines.push("o blockout");

  for (const p of mesh.positions) {
    lines.push(`v ${p.x.toFixed(4)} ${p.y.toFixed(4)} ${p.z.toFixed(4)}`);
  }
  for (let i = 0; i < mesh.indices.length; i += 3) {
    // OBJ 인덱스는 1-기반이다
    lines.push(`f ${mesh.indices[i] + 1} ${mesh.indices[i + 1] + 1} ${mesh.indices[i + 2] + 1}`);
  }

  return lines.join("\n") + "\n";
}
