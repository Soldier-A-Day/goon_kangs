"use client";

import Link from "next/link";
import { useParams } from "next/navigation";

/**
 * 게임 화면 자리. W5에서 DOM 디버그 클라이언트가 여기에 들어간다 —
 * 시간대 바, 6스탯 링, 수첩, 하달 창, 퀵 커맨드 라디얼.
 */
export default function PlayPage() {
  const params = useParams<{ code: string }>();
  const code = (params.code ?? "").toUpperCase();

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-1 flex-col justify-center gap-6 px-6 py-14">
      <span className="label">{code} · 진행 중</span>
      <h1 className="text-3xl font-extrabold tracking-tight">런이 시작됐다</h1>
      <p className="text-ink-2">
        서버는 이미 하루를 돌리고 있다 — 시간대 타이머, 기온 롤, 퀘스트 배정, 점호 판정이
        20Hz로 진행 중이다. 이 화면(HUD)은 다음 단계에서 붙는다.
      </p>
      <Link href="/" className="text-accent underline underline-offset-4">
        처음으로
      </Link>
    </main>
  );
}
