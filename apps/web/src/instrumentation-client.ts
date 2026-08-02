import * as Sentry from "@sentry/nextjs";

/**
 * 브라우저 런타임 에러 수집 (H-1).
 *
 * 배포 후 뭐가 깨졌는지 알 방법이 지금 콘솔 로그뿐이다 — 플레이어가 신고하지
 * 않으면 아무도 모른다. 계정·DSN 발급은 사용자 몫이므로 여기서는 "env만
 * 채우면 켜진다" 상태만 만든다.
 *
 * DSN이 없으면 `Sentry.init`을 **아예 호출하지 않는다.** 빈 문자열로 init을
 * 부르면 SDK가 이벤트를 만들 때마다 "DSN not configured" 류 경고를 콘솔에
 * 남기는데, 그 경고 자체가 미설정 상태의 소음이 되어 버린다 — 완전 무동작은
 * "빈 DSN으로 켜기"가 아니라 "켜지 않기"로만 만들 수 있다.
 *
 * 소스맵 업로드 등 빌드 통합(`withSentryConfig`)은 범위 밖이다 — 여기서는
 * 런타임 에러 수집만 배선한다.
 */
const dsn = process.env.NEXT_PUBLIC_SENTRY_DSN;

if (dsn) {
  Sentry.init({
    dsn,
    // 트레이스 수집은 범위 밖 — 지금은 "뭐가 깨졌는지"만 알면 된다
    tracesSampleRate: 0,
  });
}
