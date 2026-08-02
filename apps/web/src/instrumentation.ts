import * as Sentry from "@sentry/nextjs";

/**
 * 서버 런타임 에러 수집 (H-1) — `instrumentation-client.ts`의 서버 짝.
 *
 * Node·Edge 두 런타임 모두에서 이 파일이 도는데, 여기서 부르는 `Sentry.init`은
 * 어느 쪽이든 같은 DSN을 쓴다 — 클라이언트용으로 이미 공개(`NEXT_PUBLIC_`)된
 * 값이라 서버 전용 DSN을 따로 두지 않아도 노출 범위가 늘지 않는다.
 *
 * DSN이 없으면 `register`가 아무 것도 하지 않는다 — 클라이언트 쪽과 같은
 * 이유로, "초기화 안 함"이 곧 "무동작 보증"이다.
 */
export async function register() {
  if (!process.env.NEXT_PUBLIC_SENTRY_DSN) return;

  if (process.env.NEXT_RUNTIME === "nodejs") {
    Sentry.init({ dsn: process.env.NEXT_PUBLIC_SENTRY_DSN, tracesSampleRate: 0 });
  }

  if (process.env.NEXT_RUNTIME === "edge") {
    Sentry.init({ dsn: process.env.NEXT_PUBLIC_SENTRY_DSN, tracesSampleRate: 0 });
  }
}

/**
 * 서버 렌더링·라우트 핸들러 에러를 Sentry로 넘긴다. DSN이 없을 때는
 * `Sentry.captureRequestError`를 아예 부르지 않는다 — `register`가 init을
 * 건너뛴 상태에서 이 훅만 호출돼도 SDK 내부 경고가 콘솔에 남을 수 있어서다.
 */
export function onRequestError(
  ...args: Parameters<typeof Sentry.captureRequestError>
) {
  if (!process.env.NEXT_PUBLIC_SENTRY_DSN) return;
  return Sentry.captureRequestError(...args);
}
