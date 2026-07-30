-- SOLDIER : A DAY — 영속 기록 스키마
-- 기획서 표 18-1의 "영속 메타 진행" 자리. 런 스냅샷(휘발성)은 Redis에 있고 여기 오지 않는다.
--
-- 접근 모델
--   쓰기: 게임서버만. service_role 키를 쓰므로 RLS를 우회한다
--   읽기: 누구나. 리더보드는 공개 기록이다
--
-- 그래서 SELECT 정책만 만들고 INSERT/UPDATE/DELETE 정책은 만들지 않는다.
-- 정책이 없으면 service_role 외에는 쓸 수 없다 — 그게 의도다.

create table if not exists public.runs (
  run_id            text primary key,
  finished_at_day   int not null,
  status            text not null check (status in ('cleared', 'discharged', 'disbanded')),
  season            text not null check (season in ('cold', 'hot')),
  difficulty        text not null,
  ending_id         text,
  ending_label      text,
  discipline        int not null,
  -- 달성한 히든 퀘스트 id. 4개면 분대 기록 엔딩이다 (META-02)
  hidden            text[] not null default '{}',
  -- 퇴소한 런이 어느 조건에서 무너졌는지 (JDG-01)
  failed_at         text check (failed_at in ('A', 'B', 'C', 'D')),
  created_at        timestamptz not null default now()
);

-- 분대원을 별도 테이블로 두는 이유는 조회 때문이다.
-- 보직별 완주 횟수, 계급 분포, 하달 장부 집계가 전부 이 테이블의 질의다 —
-- jsonb 한 칸에 넣으면 그게 안 된다. 관계형 저장소를 쓰는 이유이기도 하다.
create table if not exists public.run_members (
  run_id                 text not null references public.runs(run_id) on delete cascade,
  name                   text not null,
  role                   text not null check (role in ('rifle', 'comms', 'medic', 'admin')),
  rank                   text not null check (rank in ('private', 'pfc', 'corporal', 'sergeant')),
  service_score          int not null,
  evacuations            int not null default 0,
  delegations_given      int not null default 0,
  delegations_received   int not null default 0,
  primary key (run_id, role)
);

-- 리더보드는 최근순, 그리고 "전역한 런만" 보는 경우가 잦다 (RecordQuery.status)
create index if not exists runs_created_at_idx on public.runs (created_at desc);
create index if not exists runs_status_idx on public.runs (status, created_at desc);
create index if not exists run_members_role_idx on public.run_members (role);

-- 노출 스키마(public)의 모든 테이블에는 RLS를 켠다
alter table public.runs enable row level security;
alter table public.run_members enable row level security;

-- 공개 읽기. 소유권 개념이 아직 없다 — 계정이 붙으면 그때 개인 기록에 조건을 건다
drop policy if exists "runs are public" on public.runs;
create policy "runs are public" on public.runs
  for select to anon, authenticated using (true);

drop policy if exists "run members are public" on public.run_members;
create policy "run members are public" on public.run_members
  for select to anon, authenticated using (true);

-- SQL로 만든 테이블은 Data API에 자동 노출되지 않을 수 있다. 명시적으로 읽기 권한을 준다.
grant select on public.runs to anon, authenticated;
grant select on public.run_members to anon, authenticated;
