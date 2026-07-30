import Link from "next/link";
import { ROLE_LABELS, type Role } from "@/lib/api";
import { RANK_LABELS, fetchRecords, outcomeLabel } from "@/lib/records";

// 기록은 런이 끝날 때마다 늘어난다. 캐시하면 방금 끝낸 런이 안 보인다.
export const revalidate = 0;

export default async function RecordsPage() {
  const result = await fetchRecords();

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-8 px-6 py-14">
      <header className="flex flex-col gap-2 border-t-2 border-ink pt-3">
        <span className="label">기록</span>
        <h1 className="text-3xl font-extrabold tracking-tight">분대 기록</h1>
        <p className="text-sm text-ink-2">
          최근 런의 결과다. 며칠을 버텼는지, 어느 조건에서 무너졌는지가 남는다.
        </p>
      </header>

      {!result.ok ? (
        <p className="border-l-[3px] border-alert bg-paper-3 px-4 py-3 text-sm text-alert">
          {result.reason === "notConfigured"
            ? "기록 저장소가 설정되지 않았다. NEXT_PUBLIC_SUPABASE_URL 과 키가 필요하다."
            : "기록을 불러오지 못했다."}
        </p>
      ) : result.records.length === 0 ? (
        <p className="text-sm text-ink-2">아직 끝난 런이 없다.</p>
      ) : (
        <div className="overflow-x-auto border border-rule">
          <table className="w-full min-w-[42rem] border-collapse text-sm tabular-nums">
            <thead>
              <tr className="bg-paper-2">
                {["결과", "생존", "계절", "군기", "히든", "분대", ""].map((head) => (
                  <th
                    key={head}
                    className="label border-b border-rule px-3 py-2 text-left"
                  >
                    {head}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {result.records.map((record) => (
                <tr key={record.runId} className="border-b border-rule-2 last:border-0">
                  <td className="px-3 py-2">
                    <b
                      className="font-bold"
                      style={{
                        color:
                          record.status === "cleared"
                            ? "var(--accent)"
                            : "var(--alert)",
                      }}
                    >
                      {outcomeLabel(record)}
                    </b>
                    {record.failedAt && (
                      <span className="label ml-2">조건 {record.failedAt}</span>
                    )}
                  </td>
                  <td className="px-3 py-2">D-{record.finishedAtDay}</td>
                  <td className="px-3 py-2">
                    {record.season === "cold" ? "혹한기" : "혹서기"}
                  </td>
                  <td className="px-3 py-2">{record.discipline}</td>
                  <td className="px-3 py-2">{record.hidden.length}</td>
                  <td className="px-3 py-2 text-ink-2">
                    {record.members
                      .map(
                        (member) =>
                          `${member.name}(${ROLE_LABELS[member.role as Role]?.name ?? member.role} ${RANK_LABELS[member.rank] ?? member.rank})`,
                      )
                      .join(" · ")}
                  </td>
                  <td className="px-3 py-2">
                    <Link
                      href={`/ledger/${record.runId}`}
                      className="text-accent underline underline-offset-4"
                    >
                      장부
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Link href="/" className="text-accent underline underline-offset-4">
        처음으로
      </Link>
    </main>
  );
}
