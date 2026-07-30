import { TOTAL_REQUIRED_PER_MEMBER } from "@sad/sim";
import { runBatch, type BatchReport } from "./simulate.js";

/**
 * 사용법:
 *   npm run sim                                  기본 1000회 × 정확도 3종
 *   npm run sim -- --runs 5000 --accuracy 0.985
 *   npm run sim -- --difficulty relaxed
 */
function main(): void {
  const args = parseArgs(process.argv.slice(2));
  const accuracies = args.accuracy ? [args.accuracy] : [0.98, 0.985, 0.99];
  const runs = args.runs ?? 1000;
  const difficulty = args.difficulty ?? "regular";

  const judgedUnits = TOTAL_REQUIRED_PER_MEMBER * 4;
  console.log("");
  console.log(`SOLDIER : A DAY — 헤드리스 밸런싱 (${difficulty})`);
  console.log(
    `1인 필수 ${TOTAL_REQUIRED_PER_MEMBER}건 × 4인 = 판정 대상 ${judgedUnits}건 · 런당 ${runs}회`,
  );
  console.log("");
  console.log("  정확도   완주율   목표 15~25%");
  console.log("  ------   ------   -----------");

  const reports: BatchReport[] = [];
  for (const accuracy of accuracies) {
    const report = runBatch(runs, { accuracy }, { difficulty });
    reports.push(report);
    const rate = (report.clearRate * 100).toFixed(1).padStart(5);
    const mark = inTargetBand(report.clearRate) ? "○ 목표 구간" : "× 이탈";
    console.log(`  ${(accuracy * 100).toFixed(1)}%    ${rate}%   ${mark}`);
  }

  const primary = reports[0];
  if (!primary) return;

  console.log("");
  console.log("며칠차에 죽는가 (정확도 " + (primary.accuracy * 100).toFixed(1) + "%)");
  const peak = Math.max(...primary.deathsByDay);
  primary.deathsByDay.forEach((count, day) => {
    if (day === 0 || count === 0) return;
    const bar = "█".repeat(Math.round((count / peak) * 40));
    console.log(`  D-${String(day).padStart(2, "0")}  ${bar} ${count}`);
  });

  console.log("");
  console.log("깨진 조건");
  for (const [condition, count] of Object.entries(primary.failuresByCondition)) {
    if (count === 0) continue;
    console.log(`  조건 ${condition}  ${count}`);
  }
  console.log("");
}

function inTargetBand(rate: number): boolean {
  return rate >= 0.15 && rate <= 0.25;
}

interface Args {
  runs?: number;
  accuracy?: number;
  difficulty?: "regular" | "relaxed";
}

function parseArgs(argv: readonly string[]): Args {
  const args: Args = {};
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i];
    const value = argv[i + 1];
    if (!key || value === undefined) continue;
    if (key === "--runs") args.runs = Number(value);
    if (key === "--accuracy") args.accuracy = Number(value);
    if (key === "--difficulty" && (value === "regular" || value === "relaxed")) {
      args.difficulty = value;
    }
  }
  return args;
}

main();
