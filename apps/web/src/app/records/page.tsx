import Link from "next/link";
import { HTTP_BASE, ROLE_LABELS, type Role } from "@/lib/api";
import { ENDING_LABELS, RANK_LABELS, type RunRecord } from "@/lib/records";

async function fetchRecords(): Promise<RunRecord[] | null> {
  try {
    const response = await fetch(`${HTTP_BASE}/records?limit=50`, { cache: "no-store" });
    if (!response.ok) return null;
    return (await response.json()) as RunRecord[];
  } catch {
    return null;
  }
}

export default async function RecordsPage() {
  const records = await fetchRecords();

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-8 px-6 py-14">
      <header className="flex flex-col gap-2 border-t-2 border-ink pt-3">
        <span className="label">기록</span>
        <h1 className="text-3xl font-extrabold tracking-tight">분대 기록</h1>
        <p className="text-sm text-ink-2">
          최근 런의 결과다. 며칠을 버텼는지, 어느 조건에서 무너졌는지가 남는다.
        </p>
      </header>

      {records === null ? (
        <p className="border-l-[3px] border-alert bg-paper-3 px-4 py-3 text-sm text-alert">
          게임 서버에 붙지 못했다. `npm run dev:server` 가 떠 있는지 확인하라.
        </p>
      ) : records.length === 0 ? (
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
              {records.map((record) => (
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
                      {record.ending
                        ? (ENDING_LABELS[record.ending.id] ?? record.ending.label)
                        : record.status === "disbanded"
                          ? "분대 해체"
                          : "퇴소"}
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
