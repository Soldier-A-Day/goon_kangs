import Link from "next/link";
import { ROLE_LABELS, type Role } from "@/lib/api";
import { HIDDEN_LABELS, RANK_LABELS, fetchRecord, outcomeLabel } from "@/lib/records";

export const revalidate = 0;

/**
 * QST-05 하달 장부.
 *
 * 런 종료 시 공개된다 — 누가 몇 건을 넘겼고 누가 몇 건을 받았는지가
 * **뒤바뀐 계급과 나란히** 표시된다. 짬을 많이 받은 이병이 먼저 승급하는 역전(6.2 경로 1)과
 * 후송으로 계급이 리셋된 역전(경로 2)이 이 표에서 눈에 보여야 한다.
 */
export default async function LedgerPage({
  params,
}: {
  params: Promise<{ runId: string }>;
}) {
  const { runId } = await params;
  const record = await fetchRecord(runId);

  if (!record) {
    return (
      <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-6 py-14">
        <h1 className="text-2xl font-extrabold">기록을 찾지 못했다</h1>
        <Link href="/records" className="text-accent underline underline-offset-4">
          기록 목록으로
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-8 px-6 py-14">
      <header className="flex flex-col gap-2 border-t-2 border-ink pt-3">
        <span className="label">하달 장부 · {record.runId}</span>
        <h1 className="text-3xl font-extrabold tracking-tight">{outcomeLabel(record)}</h1>
        <p className="text-sm text-ink-2">
          D-{record.finishedAtDay}까지 · {record.season === "cold" ? "혹한기" : "혹서기"} ·
          최종 군기 {record.discipline}
          {record.failedAt && ` · 조건 ${record.failedAt}에서 무너졌다`}
        </p>
      </header>

      <div className="overflow-x-auto border border-rule">
        <table className="w-full min-w-[36rem] border-collapse text-sm tabular-nums">
          <thead>
            <tr className="bg-paper-2">
              {["분대원", "최종 계급", "복무 점수", "넘긴 건수", "받은 건수", "후송"].map(
                (head) => (
                  <th
                    key={head}
                    className="label border-b border-rule px-3 py-2 text-left"
                  >
                    {head}
                  </th>
                ),
              )}
            </tr>
          </thead>
          <tbody>
            {record.members.map((member) => (
              <tr key={member.name} className="border-b border-rule-2 last:border-0">
                <td className="px-3 py-2">
                  <b className="font-bold">{member.name}</b>
                  <span className="label ml-2">
                    {ROLE_LABELS[member.role as Role]?.name ?? member.role}
                  </span>
                </td>
                <td className="px-3 py-2">{RANK_LABELS[member.rank] ?? member.rank}</td>
                <td className="px-3 py-2">{member.serviceScore}</td>
                <td className="px-3 py-2">{member.delegationsGiven}</td>
                <td
                  className="px-3 py-2"
                  style={{
                    color: member.delegationsReceived > 0 ? "var(--accent)" : undefined,
                  }}
                >
                  {member.delegationsReceived}
                </td>
                <td
                  className="px-3 py-2"
                  style={{ color: member.evacuations > 0 ? "var(--alert)" : undefined }}
                >
                  {member.evacuations || "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="border-l-[3px] border-accent bg-paper-3 px-4 py-3 text-sm text-ink-2">
        복무 점수는 하달한 사람이 아니라 <b className="text-ink">수행한 사람</b>에게 간다.
        짬을 많이 받은 쪽이 먼저 승급하는 이유이며, 이 표는 그 역전이 실제로 일어났는지를
        보여준다.
      </p>

      {record.hidden.length > 0 && (
        <section className="flex flex-col gap-2">
          <span className="label">달성한 히든</span>
          <ul className="flex flex-wrap gap-2">
            {record.hidden.map((id) => (
              <li key={id} className="border border-rule bg-paper-3 px-3 py-1 text-sm">
                {HIDDEN_LABELS[id] ?? id}
              </li>
            ))}
          </ul>
        </section>
      )}

      <Link href="/records" className="text-accent underline underline-offset-4">
        기록 목록으로
      </Link>
    </main>
  );
}
