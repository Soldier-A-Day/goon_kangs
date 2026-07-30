import Link from "next/link";

const FACTS = [
  { label: "장르", value: "협동 생존 RPG / 시뮬" },
  { label: "인원", value: "4인 (1개 분대)" },
  { label: "1회 플레이", value: "18일 · 100~130분" },
  { label: "승리 조건", value: "18일차 전역 심사 통과" },
];

export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col justify-center gap-10 px-6 py-16">
      <div className="flex flex-col gap-5">
        <div className="flex flex-wrap items-center gap-4">
          <span className="label border border-alert px-2 py-1 text-alert">
            개발 빌드 · 대외주의
          </span>
          <span className="label">문서번호 SAD-GDD-001</span>
        </div>

        <h1 className="text-5xl font-extrabold leading-none tracking-tight sm:text-7xl">
          SOLDIER<span className="text-accent"> : </span>A DAY
        </h1>
        <p className="max-w-xl text-lg text-ink-2">
          하루의 일과를 전부 끝내야 다음 날이 온다. 보직마다 할 일이 다르고, 혼자서는 끝낼 수
          없는 일이 매일 하나 이상 섞여 있다.
        </p>
      </div>

      <div className="grid gap-px border border-rule bg-rule sm:grid-cols-4">
        {FACTS.map((fact) => (
          <div key={fact.label} className="flex flex-col gap-1 bg-paper px-4 py-3">
            <span className="label">{fact.label}</span>
            <b className="text-sm font-bold">{fact.value}</b>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap gap-3">
        <Link
          href="/lobby"
          className="border-2 border-ink bg-ink px-6 py-3 font-bold text-paper transition-opacity hover:opacity-80"
        >
          분대 편성하기
        </Link>
        <Link
          href="/lobby?mode=join"
          className="border-2 border-ink px-6 py-3 font-bold transition-colors hover:bg-paper-2"
        >
          초대 코드로 입장
        </Link>
        <Link
          href="/records"
          className="border-2 border-rule px-6 py-3 font-bold text-ink-2 transition-colors hover:bg-paper-2"
        >
          분대 기록
        </Link>
      </div>
    </main>
  );
}
