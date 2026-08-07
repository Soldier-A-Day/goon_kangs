import { useRef, useSyncExternalStore } from "react";

/**
 * 브라우저에만 있는 값(`localStorage` · `navigator` …)을 SSR 안전하게 읽는다.
 *
 * ── 왜 필요한가 ────────────────────────────────────────────────────────
 * 이 앱은 페이지를 서버에서 먼저 그린다. 그런데 저장된 이름·설정·세션은
 * 서버에 없다. 그래서 지금까지는 어디서나 이렇게 썼다:
 *
 *     const [name, setName] = useState("");
 *     useEffect(() => { setName(loadName()); }, []);
 *
 * 동작은 한다. 다만 **mount 직후 반드시 한 번 더 렌더한다** — 빈 값으로
 * 한 번, 읽어온 값으로 또 한 번. 설정 화면이 "저장값을 읽기 전에 그리면
 * 기본값이 한 번 번쩍인다"고 자기 주석에 적어 둔 그 증상이 이것이다.
 * React 린트(`react-hooks/set-state-in-effect`)도 같은 이유로 이 모양을
 * 막고 `useSyncExternalStore`를 가리킨다.
 *
 * ── 왜 이 훅인가 ───────────────────────────────────────────────────────
 * `useSyncExternalStore`는 "React 밖에 있는 값을 읽되 서버에서는 다른 값을
 * 쓴다"를 위해 만들어졌다. 하이드레이션 때는 `serverValue`로 맞춰 그려
 * 불일치 경고가 없고, 그 직후 React가 클라이언트 값으로 한 번에 넘긴다.
 * 우리가 직접 `useEffect`로 하던 일을 React가 제 순서에 맞춰 하는 셈이다.
 *
 * ── 스냅샷을 캐시하는 이유 (빠뜨리면 무한 루프) ────────────────────────
 * `useSyncExternalStore`는 `getSnapshot()`이 **매번 같은 참조**를 돌려줄
 * 것을 요구한다. `loadSettings()`처럼 객체를 새로 만들어 돌려주는 함수를
 * 그대로 넘기면 렌더할 때마다 다른 참조가 나와 "값이 바뀌었다"로 읽히고,
 * React가 다시 렌더하고, 또 새 객체가 나오고… 무한 루프가 된다.
 * 그래서 처음 한 번만 읽어 `ref`에 담아 둔다.
 *
 * 값이 **읽은 뒤로 바뀌지 않는다**는 전제가 여기 깔려 있다. 지금 쓰는
 * 곳(저장된 이름 · 설정 · 세션 · `navigator.share` 지원 여부)은 전부
 * 그렇다. 다른 탭이 바꾼 값을 실시간으로 따라가야 한다면 이 훅이 아니라
 * `storage` 이벤트를 구독하는 진짜 store가 필요하다.
 *
 * @param read        브라우저에서 값을 읽는 함수. 최초 1회만 불린다
 * @param serverValue 서버 렌더·하이드레이션에 쓸 값
 */
export function useClientValue<T>(read: () => T, serverValue: T): T {
  // `undefined`를 값으로 쓰는 호출자가 있을 수 있어 "읽었는가"를 따로 든다
  const cache = useRef<{ value: T } | null>(null);
  const getSnapshot = () => (cache.current ??= { value: read() }).value;

  // 구독하지 않는다 — 이 값은 한 번 읽고 나면 바뀌지 않는다.
  // `useSyncExternalStore`는 구독 해제 함수를 요구하므로 빈 함수를 돌려준다
  return useSyncExternalStore(subscribeNever, getSnapshot, () => serverValue);
}

const subscribeNever = () => () => {};
